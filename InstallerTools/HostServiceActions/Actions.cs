using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using WixToolset.Dtf.WindowsInstaller;

namespace Popokey.Installer
{
    public static class Actions
    {
        [CustomAction]
        public static ActionResult CaptureHostServices(Session session) => Run(session, () =>
        {
            // MSI product enumeration is read-only. Never use Win32_Product or open an
            // installed package/session, which could invoke maintenance/repair behavior.
            List<Tuple<string, Version>> products = ReadHostProducts();
            bool destructive = !string.IsNullOrEmpty(session["Installed"]) ||
                !string.IsNullOrEmpty(session["WIX_UPGRADE_DETECTED"]) ||
                !string.IsNullOrEmpty(session["REMOVE"]);
            string provider = session["HostServiceProviderId"];
            string runner = session["HostServiceRunnerName"];
            if (!System.Text.RegularExpressions.Regex.IsMatch(provider, "^[a-z0-9.-]+$") ||
                Path.GetFileName(runner) != runner || !runner.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invalid installer provider identity.");
            using (RegistryKey machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey registration = machine.OpenSubKey(@"SOFTWARE\Popokey\IntegrationSoup\ExtensionProviders\" + provider))
                destructive |= registration != null;

            var data = new CustomActionData { ["Destructive"] = destructive ? "1" : "0", ["Runner"] = runner,
                ["Upgrade"] = string.IsNullOrEmpty(session["WIX_UPGRADE_DETECTED"]) ? "0" : "1" };
            bool providerRunning = ProviderRunning(runner);
            foreach (string name in ServicePolicy.ServiceNames)
            {
                HostState state = ReadState(name);
                string image = ReadImagePath(name);
                HostKind kind = ServicePolicy.Identify(image, products);
                ServiceDecision decision = ServicePolicy.Decide(kind, state, destructive, providerRunning);
                session.Log("Host service policy: {0}, {1}, {2}, destructive={3}, decision={4}", name, kind, state, destructive, decision);
                if (decision == ServiceDecision.Block) throw Blocked(name, kind);
                data[name + "Kind"] = kind.ToString();
                data[name + "State"] = state.ToString();
                data[name + "Image"] = image ?? string.Empty;
            }
            if (providerRunning && !ServicePolicy.ServiceNames.Any(name =>
                data[name + "Kind"] == HostKind.V4.ToString() && data[name + "State"] == HostState.Running.ToString()))
                throw new InvalidOperationException("An extension runner is active outside a running confirmed V4 service. Close its owning host before retrying; no runner was terminated.");
            GuardCachedServiceControls(session["WIX_UPGRADE_DETECTED"], data);
            // Overwrite, rather than trusting command-line properties supplied by a caller.
            foreach (string action in new[] { "StopLegacyHostServices", "RestoreLegacyHostServices", "RollbackLegacyHostServices", "RollbackStopLegacyHostServices" })
                session[action] = data.ToString();
        });

        [CustomAction]
        public static ActionResult StopLegacyHostServices(Session session) => Run(session, () =>
        {
            CustomActionData data = session.CustomActionData;
            bool activeProvider = ProviderRunning(data["Runner"]);
            // Revalidate all services before controlling any of them. This also protects
            // against starting a v5/unknown host after the immediate capture action.
            foreach (string name in ServicePolicy.ServiceNames)
            {
                EnsureSameRegistration(name, data);
                HostKind kind = Parse<HostKind>(data[name + "Kind"]);
                HostState before = Parse<HostState>(data[name + "State"]);
                HostState now = ReadState(name);
                bool stoppedByOldUninstall = data["Upgrade"] == "1" && kind == HostKind.V4 && before == HostState.Running && now == HostState.Stopped;
                if (now != before && !stoppedByOldUninstall) throw new InvalidOperationException("Host service state changed during installation; retry when stable.");
                if (ServicePolicy.Decide(kind, now, data["Destructive"] == "1", activeProvider) == ServiceDecision.Block)
                    throw Blocked(name, kind);
            }
            foreach (string name in ServicePolicy.ServiceNames)
            {
                if (!ServicePolicy.NeedsRestore(Parse<HostKind>(data[name + "Kind"]), Parse<HostState>(data[name + "State"]))) continue;
                using (var service = new ServiceController(name))
                {
                    if (service.Status == ServiceControllerStatus.Stopped) continue;
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                }
            }
            // V4's parent-monitoring runners may take a moment to exit. Do not proceed
            // into old-package removal while a same-provider runner is still alive.
            var deadline = Stopwatch.StartNew();
            while (ProviderRunning(data["Runner"]) && deadline.Elapsed < TimeSpan.FromSeconds(10))
                System.Threading.Thread.Sleep(200);
            if (ProviderRunning(data["Runner"])) throw new InvalidOperationException("An extension runner is still active. Close its owning host and retry; no runner was terminated.");
        });

        [CustomAction]
        public static ActionResult RestoreLegacyHostServices(Session session) => Restore(session);

        [CustomAction]
        public static ActionResult RollbackLegacyHostServices(Session session) => Restore(session);

        [CustomAction]
        public static ActionResult RollbackStopLegacyHostServices(Session session) => Run(session, () =>
        {
            // If failure occurs after successful service restoration, release NEW
            // files before MSI rolls them back. The earlier rollback action (or the
            // old package's rollback during upgrades) restores the original service.
            var data = session.CustomActionData;
            foreach (string name in ServicePolicy.ServiceNames)
            {
                if (!ServicePolicy.NeedsRestore(Parse<HostKind>(data[name + "Kind"]), Parse<HostState>(data[name + "State"]))) continue;
                EnsureSameRegistration(name, data);
                using (var service = new ServiceController(name))
                {
                    if (service.Status == ServiceControllerStatus.Stopped) continue;
                    service.Stop();
                    service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(60));
                }
            }
        });

        private static ActionResult Restore(Session session) => Run(session, () =>
        {
            // A stopped/absent service never receives Start, on success OR rollback.
            // This action is scheduled after restored files during rollback.
            CustomActionData data = session.CustomActionData;
            foreach (string name in ServicePolicy.ServiceNames)
            {
                if (!ServicePolicy.NeedsRestore(Parse<HostKind>(data[name + "Kind"]), Parse<HostState>(data[name + "State"]))) continue;
                EnsureSameRegistration(name, data);
                using (var service = new ServiceController(name))
                {
                    if (service.Status == ServiceControllerStatus.Running) continue;
                    if (service.Status != ServiceControllerStatus.Stopped) throw new InvalidOperationException("Host service is not stable for restoration.");
                    service.Start();
                    service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(60));
                }
            }
        });

        internal static List<Tuple<string, Version>> ReadHostProducts()
        {
            var result = new List<Tuple<string, Version>>();
            foreach (ProductInstallation product in ProductInstallation.GetRelatedProducts(ServicePolicy.HostUpgradeCode))
            {
                if (!product.IsInstalled) continue;
                string path = new ComponentInstallation(ServicePolicy.HostExeComponent, product.ProductCode).Path;
                if (!string.IsNullOrWhiteSpace(path)) result.Add(Tuple.Create(path, product.ProductVersion));
            }
            return result;
        }

        private static void GuardCachedServiceControls(string relatedProducts, CustomActionData data)
        {
            foreach (string productCode in relatedProducts.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                // Open only the cached database read-only, never an installation session.
                // Its native controls remain effective during old-product rollback.
                string cache = new ProductInstallation(productCode).LocalPackage;
                using (var database = new Database(cache, DatabaseOpenMode.ReadOnly))
                {
                    if (!database.Tables.Contains("ServiceControl")) continue;
                    using (View view = database.OpenView("SELECT `Name`, `Event` FROM `ServiceControl`"))
                    {
                        view.Execute();
                        Record row;
                        while ((row = view.Fetch()) != null)
                        using (row)
                        {
                            if (row.GetInteger(2) == 0) continue;
                            string name = row.GetString(1);
                            if (!ServicePolicy.ServiceNames.Contains(name) ||
                                !ServicePolicy.CachedControlsAllowed(Parse<HostKind>(data[name + "Kind"]), Parse<HostState>(data[name + "State"])))
                                throw new InvalidOperationException("The cached previous activity installer contains service controls that cannot preserve this host's initial state on rollback. This upgrade is blocked before removal. Use a separately validated legacy-package transition; no service was stopped or started.");
                        }
                    }
                }
            }
        }

        private static bool ProviderRunning(string runner)
        {
            // Read-only and deliberately conservative: a runner with this name in an
            // interactive/test host also prevents destructive file operations. Never kill it.
            Process[] processes = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(runner));
            try { return processes.Length != 0; }
            finally { foreach (Process process in processes) process.Dispose(); }
        }

        private static HostState ReadState(string name)
        {
            try
            {
                using (var service = new ServiceController(name))
                {
                    switch (service.Status)
                    {
                        case ServiceControllerStatus.Running: return HostState.Running;
                        case ServiceControllerStatus.Stopped: return HostState.Stopped;
                        default: return HostState.Transitional;
                    }
                }
            }
            catch (InvalidOperationException ex) when (ex.InnerException is Win32Exception native && native.NativeErrorCode == 1060)
            { return HostState.Absent; }
        }

        private static string ReadImagePath(string name)
        {
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                return key?.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        }

        private static void EnsureSameRegistration(string name, CustomActionData data)
        {
            if (!string.Equals(ReadImagePath(name) ?? string.Empty, data[name + "Image"], StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Host service registration changed during installation; no service control was attempted.");
        }

        private static T Parse<T>(string value) where T : struct
        {
            if (!Enum.TryParse(value, out T parsed) || !Enum.IsDefined(typeof(T), parsed)) throw new InvalidOperationException("Invalid captured service state.");
            return parsed;
        }

        private static Exception Blocked(string name, HostKind kind) => new InvalidOperationException(
            "Automatic restart is allowed only for a confirmed running Integration Soup V4 host. " +
            "This operation cannot safely replace an active provider for " + name + " (" + kind + "). " +
            "Close the affected host/runner before retrying. No service was stopped or started.");

        private static ActionResult Run(Session session, Action action)
        {
            try { action(); return ActionResult.Success; }
            catch (Exception ex)
            {
                session.Log("Host service policy failed: {0}", ex);
                using (var record = new Record(1))
                {
                    record[0] = "[1]";
                    record[1] = ex.Message;
                    session.Message(InstallMessage.Error, record);
                }
                return ActionResult.Failure;
            }
        }
    }
}
