using System;
using System.Collections.Generic;
using Popokey.Installer;

internal static class Program
{
    private static int checks;
    private static void Main()
    {
        const string exe = @"C:\Program Files (x86)\Popokey\Integration Host Server\HL7SoupIntegrationHost.exe";
        string image = "\"" + exe + "\" --service";
        Check(ServicePolicy.Identify(image, Product(exe, "4.0.0.0")) == HostKind.V4, "Actual V4 installed product identity");
        Check(ServicePolicy.Identify(image, Product(exe, "5.0.0.0")) == HostKind.V5, "Actual V5 installed product identity");
        Check(ServicePolicy.Identify(image, Product(@"D:\dev\HL7SoupIntegrationHost.exe", "5.0.0.0")) == HostKind.Unknown, "Development checkout is not installed service");
        Check(ServicePolicy.Identify(image, Product(exe, "6.0.0.0")) == HostKind.Unknown, "Unreviewed future host is not assumed supported");
        Check(ServicePolicy.Identify(image, new[] { Tuple.Create(exe, new Version(4,0)), Tuple.Create(exe, new Version(5,0)) }) == HostKind.Unknown, "Ambiguous product registrations");
        Check(ServicePolicy.Identify(exe, Product(exe, "4.0.0.0")) == HostKind.Unknown, "Ambiguous unquoted service command");
        Check(ServicePolicy.Identify(null, Product(exe, "4.0.0.0")) == HostKind.Unknown, "No registered service image");
        Check(ServicePolicy.Identify(image, new Tuple<string, Version>[0]) == HostKind.Unknown, "No host MSI identity");
        foreach (HostKind kind in Enum.GetValues(typeof(HostKind)))
        foreach (bool destructive in new[] { false, true })
        {
            Check(ServicePolicy.Decide(kind, HostState.Stopped, destructive, false) == ServiceDecision.LeaveAlone, "Stopped services stay stopped");
            Check(ServicePolicy.Decide(kind, HostState.Absent, destructive, false) == ServiceDecision.LeaveAlone, "Absent service untouched");
            Check(ServicePolicy.Decide(kind, HostState.Transitional, destructive, false) == ServiceDecision.Block, "Pending/paused service is not guessed");
            Check(!ServicePolicy.NeedsRestore(kind, HostState.Stopped), "Success/rollback never starts an originally stopped service");
            Check(!ServicePolicy.NeedsRestore(kind, HostState.Absent), "Success/rollback never creates an absent service");
        }
        Check(ServicePolicy.Decide(HostKind.V4, HostState.Running, false, false) == ServiceDecision.RestartV4, "V4 discovery cache needs fresh install restart");
        Check(ServicePolicy.Decide(HostKind.V4, HostState.Running, true, true) == ServiceDecision.RestartV4, "V4 active runner drains through parent shutdown");
        Check(ServicePolicy.NeedsRestore(HostKind.V4, HostState.Running), "Previously running V4 restored after install/uninstall/rollback");
        Check(ServicePolicy.Decide(HostKind.V5, HostState.Running, false, false) == ServiceDecision.LeaveAlone, "V5 fresh discovery needs no restart");
        Check(ServicePolicy.Decide(HostKind.V5, HostState.Running, true, false) == ServiceDecision.Block, "V5 upgrade/repair/uninstall does not silently restart");
        Check(ServicePolicy.Decide(HostKind.Unknown, HostState.Running, false, false) == ServiceDecision.Block, "Unknown running host is not treated as V4");
        Check(ServicePolicy.Decide(HostKind.V5, HostState.Stopped, true, true) == ServiceDecision.Block, "Interactive provider still holding payload prevents removal");
        Check(!ServicePolicy.NeedsRestore(HostKind.V5, HostState.Running), "No automatic V5 restart on success/rollback");
        Check(!ServicePolicy.NeedsRestore(HostKind.Unknown, HostState.Running), "No automatic unknown-host restart on success/rollback");
        foreach (HostKind kind in Enum.GetValues(typeof(HostKind)))
        {
            Check(!ServicePolicy.CachedControlsAllowed(kind, HostState.Stopped), "Cached native rollback must not start an initially stopped service");
            Check(ServicePolicy.CachedControlsAllowed(kind, HostState.Absent), "Cached service control cannot create an absent service");
        }
        Check(ServicePolicy.CachedControlsAllowed(HostKind.V4, HostState.Running), "Cached V4 controls only restore the initially running V4 host");
        Check(!ServicePolicy.CachedControlsAllowed(HostKind.V5, HostState.Running), "Cached controls cannot restart V5");
        Check(!ServicePolicy.CachedControlsAllowed(HostKind.Unknown, HostState.Running), "Cached controls cannot restart an unknown host");
        Console.WriteLine(checks + " service policy and installed-host identity checks passed; no Windows service operations executed.");
    }
    private static IEnumerable<Tuple<string, Version>> Product(string path, string version) => new[] { Tuple.Create(path, new Version(version)) };
    private static void Check(bool value, string message) { checks++; if (!value) throw new InvalidOperationException(message); }
}
