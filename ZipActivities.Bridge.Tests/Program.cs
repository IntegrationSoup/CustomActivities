using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;

namespace ZipActivities.Bridge.Tests
{
    // A failed extension step says why (T46): only a sentence that a library wrote on purpose
    // reaches the Host, cleaned and capped; every other failure stays masked.
    internal static class Checks
    {
        private static int checks;

        private static int Main()
        {
            string root = Path.Combine(Path.GetTempPath(), "zip-bridge-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Runtime();
                Zip(root);
                Console.WriteLine($"ZIP bridge checks PASS: {checks} checks.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
            finally { try { Directory.Delete(root, true); } catch { } }
        }

        private static void Runtime()
        {
            var provider = new BridgeProvider("fixture", "Fixture", (operation, payload) => throw new NotSupportedException(), typeof(Explains), typeof(Hides));

            var explained = Fail(provider, "fixture", "Explains", Params("Reason", "The folder 'C:\\in'\r\n\twas\u0007 not\u202E found."));
            Check(explained.ErrorMessage == "The folder 'C:\\in' was not found.", "a library's own sentence passes through, with control and format characters removed and white space made single");
            var failure = PersistentRunnerJson.Deserialize<ExtensionFailure>(explained.PayloadJson);
            Check(failure.Message == explained.ErrorMessage && failure.Code == "ExecutionFailed" && failure.ExecutionState == "Unknown", "code and state are unchanged, so the Host treats the failure as before");

            var long_ = Fail(provider, "fixture", "Explains", Params("Reason", new string('x', 1000)));
            Check(long_.ErrorMessage == new string('x', BridgeProvider.ReasonLimit) + "\u2026", "a long sentence is cut at the limit with an ellipsis");

            var blank = Fail(provider, "fixture", "Explains", Params("Reason", " \r\n\t "));
            Check(blank.ErrorMessage == "Extension execution failed (ExtensionReasonException).", "an empty sentence stays masked");

            var hidden = Fail(provider, "fixture", "Hides", Params());
            Check(hidden.ErrorMessage == "Extension execution failed (InvalidOperationException)." && !hidden.PayloadJson.Contains("hunter2"), "any other exception stays masked, its text never sent");

            string surrogate = new string('y', BridgeProvider.ReasonLimit - 1) + "\U0001F600";
            Check(BridgeProvider.CleanReason(surrogate) == new string('y', BridgeProvider.ReasonLimit - 1) + "\u2026", "the cut never splits a surrogate pair");
            Check(typeof(ExtensionReasonException).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).All(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(string)),
                "the reason exception takes only a sentence, never an inner exception");
        }

        private static void Zip(string root)
        {
            var handler = (PersistentRunnerRequestHandler)Delegate.CreateDelegate(typeof(PersistentRunnerRequestHandler), typeof(ZipActivities.Runner.Program).GetMethod("HandleServerRequest", BindingFlags.Static | BindingFlags.NonPublic));
            var provider = new BridgeProvider("popokey.zipactivities", "ZipActivities", handler, typeof(CreateZipFile), typeof(CreateZipMessage), typeof(ExtractZipFile));
            const string id = "popokey.zipactivities";

            string source = Path.Combine(root, "source"); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "a.txt"), "a");
            string missing = Path.Combine(root, "no-such-folder");
            string zip = Path.Combine(root, "out.zip");

            Check(Fail(provider, id, "Create ZIP File", Params("ZIP File Path", zip)).ErrorMessage == "The parameter 'Source Directory' is required.", "a missing required parameter is named");
            Check(Fail(provider, id, "Create ZIP File", Params("Source Directory", source, "ZIP File Path", zip, "Overwrite Existing File", "maybe")).ErrorMessage == "The parameter 'Overwrite Existing File' must be true or false.", "a yes/no parameter that is neither is named");
            Check(Fail(provider, id, "Create ZIP File", Params("Source Directory", missing, "ZIP File Path", zip)).ErrorMessage == $"The source directory '{missing}' was not found.", "a missing source folder is named");
            Check(Fail(provider, id, "Create ZIP Message", Params("Source Directory", missing)).ErrorMessage == $"The source directory '{missing}' was not found.", "a missing source folder is named for a ZIP message");

            Check(Succeed(provider, id, "Create ZIP File", Params("Source Directory", source, "ZIP File Path", zip)), "a ZIP file is still created through the bridge");
            Check(Fail(provider, id, "Create ZIP File", Params("Source Directory", source, "ZIP File Path", zip)).ErrorMessage == $"The ZIP file '{zip}' already exists. Turn on Overwrite Existing File to replace it.", "an existing ZIP file is named");
            Check(Succeed(provider, id, "Create ZIP File", Params("Source Directory", source, "ZIP File Path", zip, "Overwrite Existing File", "true")), "overwrite still replaces it");

            string absent = Path.Combine(root, "absent.zip");
            Check(Fail(provider, id, "Extract ZIP File", Params("ZIP File Path", absent, "Destination Directory", Path.Combine(root, "x"))).ErrorMessage == $"The ZIP file '{absent}' was not found.", "a missing ZIP file is named");

            string damaged = Path.Combine(root, "damaged.zip"); File.WriteAllText(damaged, "not a zip");
            Check(Fail(provider, id, "Extract ZIP File", Params("ZIP File Path", damaged, "Destination Directory", Path.Combine(root, "y"))).ErrorMessage == $"The ZIP file '{damaged}' is damaged, or holds an entry that cannot be extracted safely.", "a damaged ZIP file is named, without .NET's own text");

            string unsafeZip = Path.Combine(root, "unsafe.zip");
            using (var archive = ZipFile.Open(unsafeZip, ZipArchiveMode.Create)) archive.CreateEntry("../escape.txt");
            Check(Fail(provider, id, "Extract ZIP File", Params("ZIP File Path", unsafeZip, "Destination Directory", Path.Combine(root, "z"))).ErrorMessage == $"The ZIP file '{unsafeZip}' is damaged, or holds an entry that cannot be extracted safely.", "an entry that would leave the destination is named");
            Check(!File.Exists(Path.Combine(root, "escape.txt")), "and nothing was written outside the destination");

            string destination = Path.Combine(root, "dest"); Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "a.txt"), "already here");
            Check(Fail(provider, id, "Extract ZIP File", Params("ZIP File Path", zip, "Destination Directory", destination)).ErrorMessage == "Extension execution failed (InvalidOperationException).",
                "a failure the library does not name for certain (an existing destination file) stays masked");
            Check(Succeed(provider, id, "Extract ZIP File", Params("ZIP File Path", zip, "Destination Directory", destination, "Overwrite Existing Files", "true")), "extraction still works through the bridge");
        }

        private static ExtensionResponseEnvelope Fail(BridgeProvider provider, string providerId, string name, List<ExtensionNamedValue> values)
        {
            var response = Process(provider, providerId, name, values);
            Check(!response.Success, name + " fails");
            return response;
        }

        private static bool Succeed(BridgeProvider provider, string providerId, string name, List<ExtensionNamedValue> values)
        {
            var response = Process(provider, providerId, name, values);
            if (!response.Success) throw new Exception("Failed: " + name + " should succeed: " + response.ErrorMessage);
            return true;
        }

        private static ExtensionResponseEnvelope Process(BridgeProvider provider, string providerId, string name, List<ExtensionNamedValue> values)
        {
            var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(provider, ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = providerId }).PayloadJson);
            var request = new ExtensionInvokeRequest { ProviderId = providerId, TypeName = catalog.Extensions.Single(d => d.DisplayName == name).TypeName, Kind = "Activity",
                SessionId = Guid.NewGuid().ToString("N"), Phase = "Prepare", DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(60).ToString("O"), ResponseMessage = new ExtensionMessage { MessageType = 13, Text = "" } };
            Call(provider, ExtensionBridgeProtocol.Invoke, request);
            request.Phase = "Process"; request.Parameters = values;
            var response = provider.Dispatch(Envelope(ExtensionBridgeProtocol.Invoke, request));
            if (response.Success) { request.Phase = "Close"; Call(provider, ExtensionBridgeProtocol.Invoke, request); }
            return response;
        }

        private static List<ExtensionNamedValue> Params(params string[] pairs)
        {
            var values = new List<ExtensionNamedValue>();
            for (int i = 0; i < pairs.Length; i += 2) values.Add(new ExtensionNamedValue { Name = pairs[i], Value = pairs[i + 1] });
            return values;
        }

        private static ExtensionRequestEnvelope Envelope<T>(string operation, T value) => new ExtensionRequestEnvelope { RequestId = Guid.NewGuid().ToString("N"), Operation = operation, PayloadJson = PersistentRunnerJson.Serialize(value) };

        private static ExtensionResponseEnvelope Call<T>(BridgeProvider provider, string operation, T value)
        {
            var response = provider.Dispatch(Envelope(operation, value)); if (!response.Success) throw new Exception(response.ErrorMessage); return response;
        }

        private static void Check(bool success, string description) { if (!success) throw new Exception("Failed: " + description); checks++; }

        [System.ComponentModel.DisplayName("Explains")]
        public sealed class Explains : CustomActivity
        {
            public override void Process(IWorkflowInstance workflow, IActivityInstance activity, Dictionary<string, string> parameters)
            {
                throw new ExtensionReasonException(parameters["Reason"]);
            }
        }

        [System.ComponentModel.DisplayName("Hides")]
        public sealed class Hides : CustomActivity
        {
            public override void Process(IWorkflowInstance workflow, IActivityInstance activity, Dictionary<string, string> parameters)
            {
                throw new InvalidOperationException("password=hunter2");
            }
        }
    }
}
