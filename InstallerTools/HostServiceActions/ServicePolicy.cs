using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Popokey.Installer
{
    internal enum HostKind { Unknown, V4, V5 }
    internal enum HostState { Absent, Stopped, Running, Transitional }
    internal enum ServiceDecision { LeaveAlone, RestartV4, Block }

    internal static class ServicePolicy
    {
        // These identifiers are shared by the actual HL7Soup 4.0 branch and v5 main.
        internal const string HostUpgradeCode = "{59974DE9-ADD2-402C-8216-C4E3303A143F}";
        internal const string HostExeComponent = "{0319DCB1-FE86-46FA-99E1-16565C068EEF}";
        internal static readonly string[] ServiceNames = { "IntegrationSoupHostService", "HL7SoupIntegrationHostServer" };

        internal static HostKind Identify(string serviceImagePath, IEnumerable<Tuple<string, Version>> products)
        {
            string executable = ExecutablePath(serviceImagePath);
            if (executable == null) return HostKind.Unknown;
            var matches = products.Where(product => !string.IsNullOrWhiteSpace(product.Item1) &&
                string.Equals(Path.GetFullPath(product.Item1), executable, StringComparison.OrdinalIgnoreCase)).ToList();
            // Multiple registrations are ambiguous even if their versions happen to agree.
            if (matches.Count != 1 || matches[0].Item2 == null) return HostKind.Unknown;
            return matches[0].Item2.Major == 4 ? HostKind.V4 :
                matches[0].Item2.Major == 5 ? HostKind.V5 : HostKind.Unknown;
        }

        internal static string ExecutablePath(string imagePath)
        {
            if (string.IsNullOrWhiteSpace(imagePath)) return null;
            string value = Environment.ExpandEnvironmentVariables(imagePath.Trim());
            if (value.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = value.IndexOf('"', 1);
                if (end < 0) return null;
                value = value.Substring(1, end - 1);
            }
            else if (!value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || value.Contains(" "))
                return null; // Do not guess Windows' ambiguous unquoted path/arguments parsing.
            return Path.IsPathRooted(value) ? Path.GetFullPath(value) : null;
        }

        internal static ServiceDecision Decide(HostKind host, HostState state, bool destructive, bool activeProvider)
        {
            if (state == HostState.Transitional) return ServiceDecision.Block;
            if (state == HostState.Absent) return ServiceDecision.LeaveAlone;
            // Never delete/replace a leased runner on behalf of a live interactive or v5 host.
            if (activeProvider && host != HostKind.V4) return ServiceDecision.Block;
            if (state == HostState.Absent || state == HostState.Stopped) return ServiceDecision.LeaveAlone;
            if (host == HostKind.V4) return ServiceDecision.RestartV4;
            if (host == HostKind.V5 && !destructive && !activeProvider) return ServiceDecision.LeaveAlone;
            return ServiceDecision.Block;
        }

        internal static bool NeedsRestore(HostKind host, HostState before) => host == HostKind.V4 && before == HostState.Running;
    }
}
