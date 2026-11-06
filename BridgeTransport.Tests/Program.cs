using System;
using System.Collections.Generic;
using System.Threading;
using HL7Soup.Integrations;
using Popokey.ExtensionRunners;

internal static class RuntimeTests
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "--fixture-parent") { Thread.Sleep(Timeout.Infinite); return 0; }
            if (args.Length > 0 && args[0] == "--server")
                return BridgeRunner.RunIfRequested(args, (operation, payload) => throw new NotSupportedException(),
                    () => new BridgeProvider("fixture", "Fixture", (operation, payload) => throw new NotSupportedException(), typeof(Probe))) ?? 2;
            TransportFixture.Run();
            return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
    }

    public sealed class Probe : CustomActivity
    {
        public override void Process(IWorkflowInstance workflow, IActivityInstance activity, Dictionary<string, string> parameters)
        {
            Console.WriteLine("PROBE_STARTED");
            // Existing activities may ignore cancellation. Their result must
            // still be discarded without replay while .control stays responsive.
            Thread.Sleep(1000);
            activity.ResponseMessage.SetText("done");
        }
    }
}
