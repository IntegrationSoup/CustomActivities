using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Linq;
using ValidateTransformer;

namespace ValidateTransformer.Bridge.Tests
{
    // A failed Validate step says why (T46): the library's own sentences reach the Host through the bridge;
    // the parser's text never does.
    internal static class Checks
    {
        private const string Id = "popokey.validatehl7transformer";
        private const int Hl7 = 1, Json = 11, Text = 13;
        private static int checks;

        private static int Main()
        {
            try
            {
                // HL7 messages become the stand-in below, as the real runner makes them its parser-backed message.
                SnapshotMessage.TypedFactory = value => value.MessageType == Hl7 && StandInHl7.Parses ? new StandInHl7(value) : value.MessageType == Json ? new SnapshotJsonMessage(value) : new SnapshotMessage(value);
                var provider = new BridgeProvider(Id, "ValidateTransformer", (operation, payload) => throw new NotSupportedException(), typeof(ValidateHL7Transformer), typeof(ValidateHL7ToAckTransformer));
                const string toJson = "Validate HL7 Message";
                const string toAck = "Validate HL7 Message to HL7 ACK";
                string message = "MSH|^~\\&|A|B|C|D|20260101||ADT^A01|1|P|2.5\rPID|1||patient-id-here";

                StandInHl7.Answer = _ => "{\"Errors\":[]}";
                Check(Process(provider, toJson, Hl7, message, Json, Params("Profile", "Default")).Success, "a valid message still validates to JSON through the bridge");

                Check(Fail(provider, toJson, Hl7, message, Json, Params()).ErrorMessage == "A validation Profile is required.", "a missing profile is named");
                Check(Fail(provider, toAck, Hl7, message, Hl7, Params("Profile", "Default", "Error if invalid", "maybe")).ErrorMessage == "Error if invalid must be true or false.", "an Error if invalid value that is neither is named");
                Check(Fail(provider, toJson, Text, "", Json, Params("Profile", "Default")).ErrorMessage == "ValidateHL7Transformer requires an HL7 input message. Bind an HL7 message to the activity or run it from an HL7 receiver.", "no input message is named");
                StandInHl7.Parses = false;
                Check(Fail(provider, toJson, Text, message, Json, Params("Profile", "Default")).ErrorMessage == "ValidateHL7Transformer could not parse the activity input as HL7.", "input that cannot be made HL7 is named");
                StandInHl7.Parses = true;
                Check(Fail(provider, toJson, Hl7, message, Text, Params("Profile", "Default")).ErrorMessage == "ValidateHL7Transformer requires a JSON response message.", "a response message that is not JSON is named");
                Check(Fail(provider, toAck, Hl7, message, Json, Params("Profile", "Default")).ErrorMessage == "ValidateHL7ToAckTransformer requires an HL7 response message.", "a response message that is not HL7 is named for the ACK step");

                StandInHl7.Answer = profile => throw new Exception("No highlighters found for profile " + profile);
                var unknown = Fail(provider, toJson, Hl7, message, Json, Params("Profile", "No Such Profile"));
                Check(unknown.ErrorMessage == "The validation profile 'No Such Profile' was not found." && !unknown.PayloadJson.Contains("highlighters"), "an unknown profile is named in the library's own words");

                StandInHl7.Answer = _ => throw new Exception("No highlighters found for profile Something Else");
                Check(Fail(provider, toJson, Hl7, message, Json, Params("Profile", "No Such Profile")).ErrorMessage == "Extension execution failed (Exception).", "the parser's words for a different profile are not taken as this one; it stays masked");
                StandInHl7.Answer = _ => throw new InvalidOperationException("password=hunter2");
                var other = Fail(provider, toJson, Hl7, message, Json, Params("Profile", "Default"));
                Check(other.ErrorMessage == "Extension execution failed (InvalidOperationException)." && !other.PayloadJson.Contains("hunter2"), "any other parser failure stays masked, its text never sent");

                StandInHl7.Answer = _ => "{\"Errors\":[]}";
                StandInHl7.HasMsh = false;
                Check(Fail(provider, toAck, Hl7, message, Hl7, Params("Profile", "Default")).ErrorMessage == "Cannot generate an HL7 ACK because the inbound message does not have an MSH segment.", "a message without an MSH segment is named for the ACK step");
                StandInHl7.HasMsh = true;
                Check(Process(provider, toAck, Hl7, message, Hl7, Params("Profile", "Default")).Success, "an ACK is still built through the bridge");

                Console.WriteLine($"Validate bridge checks PASS: {checks} checks.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        private sealed class StandInHl7 : SnapshotMessage, IHL7Message
        {
            internal static Func<string, string> Answer;
            internal static bool HasMsh = true;
            internal static bool Parses = true;
            internal StandInHl7(ExtensionMessage value) : base(value) { }
            public string ValidateWithHighlighters(string profileName) => Answer(profileName);
            public IHL7Segment GetSegment(string name) => HasMsh && name == "MSH" && (Text ?? "").StartsWith("MSH") ? new Segment(Text.Split('\r')[0]) : null;
            private sealed class Segment : IHL7Segment
            {
                private readonly string[] fields;
                internal Segment(string text) { Text = text; fields = text.Split('|'); }
                public string Text { get; }
                public string GetFieldValue(int field) => field == 1 ? "|" : field < fields.Length + 1 && field >= 2 ? fields[field - 1] : "";
                public string GetComponentValue(int field, int component) { var parts = GetFieldValue(field).Split('^'); return component <= parts.Length ? parts[component - 1] : ""; }
            }
        }

        private static ExtensionResponseEnvelope Fail(BridgeProvider provider, string name, int inType, string text, int outType, List<ExtensionNamedValue> values)
        {
            var response = Process(provider, name, inType, text, outType, values);
            Check(!response.Success, name + " fails");
            return response;
        }

        private static ExtensionResponseEnvelope Process(BridgeProvider provider, string name, int inType, string text, int outType, List<ExtensionNamedValue> values)
        {
            var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(provider, ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = Id }).PayloadJson);
            var request = new ExtensionInvokeRequest { ProviderId = Id, TypeName = catalog.Extensions.Single(d => d.DisplayName == name).TypeName, Kind = "Activity",
                SessionId = Guid.NewGuid().ToString("N"), Phase = "Prepare", DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(60).ToString("O"),
                Message = new ExtensionMessage { MessageType = inType, Text = text }, ResponseMessage = new ExtensionMessage { MessageType = outType, Text = "" } };
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
    }
}
