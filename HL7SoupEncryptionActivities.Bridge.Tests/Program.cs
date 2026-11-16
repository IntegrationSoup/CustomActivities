using HL7Soup.Integrations.ExtensionBridge;
using HL7SoupEncryptionActivities;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HL7SoupEncryptionActivities.Bridge.Tests
{
    // A failed Encrypt or Decrypt Message step says why (T46): the library names the failures its own checks
    // decide, never with the key or the message in the sentence; everything else stays masked.
    internal static class Checks
    {
        private const string Id = "popokey.encryptionactivities";
        private const string Key = "correct horse battery staple";
        private const string OtherKey = "a different key entirely";
        private const string Secret = "MSH|^~\\&|patient-name-here";
        private static int checks;

        private static int Main()
        {
            try
            {
                var handler = (PersistentRunnerRequestHandler)Delegate.CreateDelegate(typeof(PersistentRunnerRequestHandler), typeof(HL7SoupEncryptionActivities.Runner.Program).GetMethod("HandleServerRequest", BindingFlags.Static | BindingFlags.NonPublic));
                var provider = new BridgeProvider(Id, "HL7SoupEncryptionActivities", handler, typeof(EncryptMessage), typeof(DecryptMessage));

                var encrypted = Process(provider, "Encrypt Message", Secret, Params("Encryption Key", Key));
                Check(encrypted.Success, "a message is still encrypted through the bridge");
                string cipher = PersistentRunnerJson.Deserialize<ExtensionInvokeResult>(encrypted.PayloadJson).ResponseMessage.Text;
                var decrypted = Process(provider, "Decrypt Message", cipher, Params("Encryption Key", Key));
                Check(decrypted.Success && PersistentRunnerJson.Deserialize<ExtensionInvokeResult>(decrypted.PayloadJson).ResponseMessage.Text == Secret, "and decrypted back with the same key");

                Check(Fail(provider, "Encrypt Message", Secret, Params()).ErrorMessage == "The parameter 'Encryption Key' is required.", "a missing key is named for Encrypt Message");
                Check(Fail(provider, "Decrypt Message", cipher, Params("Encryption Key", "  ")).ErrorMessage == "The parameter 'Encryption Key' is required.", "a blank key is named for Decrypt Message");

                var notBase64 = Fail(provider, "Decrypt Message", Secret, Params("Encryption Key", Key));
                Check(notBase64.ErrorMessage == "The message cannot be decrypted: it is not encrypted text (it is not Base64).", "text that is not Base64 is named");

                var wrongKey = Fail(provider, "Decrypt Message", cipher, Params("Encryption Key", OtherKey));
                Check(wrongKey.ErrorMessage == WrongKeyOrChangedText, "a different key is named as the key or a changed text");
                Check(!wrongKey.ErrorMessage.Contains(OtherKey) && !wrongKey.PayloadJson.Contains(OtherKey) && !wrongKey.PayloadJson.Contains(cipher) && !wrongKey.PayloadJson.Contains("Padding"),
                    "the sentence holds neither the key, the message nor .NET's own text");

                Check(Fail(provider, "Decrypt Message", "AAAA", Params("Encryption Key", Key)).ErrorMessage == WrongKeyOrChangedText, "Base64 that is not a whole cipher block is named the same way");
                string cut = cipher.Substring(0, cipher.Length - 4) + (cipher.EndsWith("AAAA") ? "BBBB" : "AAAA");
                var changed = Process(provider, "Decrypt Message", cut, Params("Encryption Key", Key));
                Check(changed.Success || changed.ErrorMessage == WrongKeyOrChangedText, "a changed last block is either decrypted (padding happens to hold) or named, never shown raw");

                Console.WriteLine($"Encryption bridge checks PASS: {checks} checks.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        private static string WrongKeyOrChangedText => "The message could not be decrypted with this Encryption Key. Either the key differs from the one it was encrypted with, or the text is not the unchanged output of an Encrypt Message step.";

        private static ExtensionResponseEnvelope Fail(BridgeProvider provider, string name, string text, List<ExtensionNamedValue> values)
        {
            var response = Process(provider, name, text, values);
            Check(!response.Success, name + " fails");
            return response;
        }

        private static ExtensionResponseEnvelope Process(BridgeProvider provider, string name, string text, List<ExtensionNamedValue> values)
        {
            var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(provider, ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = Id }).PayloadJson);
            var request = new ExtensionInvokeRequest { ProviderId = Id, TypeName = catalog.Extensions.Single(d => d.DisplayName == name).TypeName, Kind = "Activity",
                SessionId = Guid.NewGuid().ToString("N"), Phase = "Prepare", DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(60).ToString("O"),
                Message = new ExtensionMessage { MessageType = 13, Text = text }, ResponseMessage = new ExtensionMessage { MessageType = 13, Text = "" } };
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
