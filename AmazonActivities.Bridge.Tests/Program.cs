using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;

namespace AmazonActivities.Bridge.Tests
{
    // A failed AWS S3 Upload step says why (T46): the library names the S3 refusals it can decide from the
    // AWS error code, never with the keys or the SDK's text in the sentence; everything else stays masked.
    // S3 is a local HttpListener on 127.0.0.1 that answers with S3's XML error bodies: no network, no account.
    internal static class Checks
    {
        private const string Id = "popokey.awsactivities";
        private const string Bucket = "my-test-bucket";
        private const string KeyId = "AKIAFAKEKEYID0000000";
        private const string Secret = "fakeSecretAccessKey/ValueThatMustNeverShow0000";
        private const string SdkText = "SDK-OWN-WORDING-MUST-NOT-APPEAR";
        private static int checks;
        private static int status = 200;
        private static string code;
        private static int uploads;

        private static int Main()
        {
            var listener = new HttpListener();
            int port = FreePort();
            string url = $"http://127.0.0.1:{port}/";
            listener.Prefixes.Add(url);
            listener.Start();
            var serving = new System.Threading.Thread(() => Serve(listener)) { IsBackground = true };
            serving.Start();
            try
            {
                Runner.Program.ServiceUrlOverride = url.TrimEnd('/');
                var handler = (PersistentRunnerRequestHandler)Delegate.CreateDelegate(typeof(PersistentRunnerRequestHandler), typeof(Runner.Program).GetMethod("HandleServerRequest", BindingFlags.Static | BindingFlags.NonPublic));
                var provider = new BridgeProvider(Id, "AmazonActivities", handler, typeof(S3Sender));

                status = 200; code = null;
                var ok = Process(provider, Params());
                Check(ok.Success && uploads == 1, "an upload still works through the bridge");

                Check(Fail(provider, Params(("Bucket Name", " "))).ErrorMessage == "The parameter 'Bucket Name' is required.", "a blank Bucket Name is named");
                Check(Fail(provider, Params(("Secret Access Key", null))).ErrorMessage == "The parameter 'Secret Access Key' is required.", "a missing Secret Access Key is named");

                Named(provider, 404, "NoSuchBucket", $"The S3 bucket '{Bucket}' was not found.");
                Named(provider, 403, "InvalidAccessKeyId", "AWS did not recognise the Access Key ID.");
                Named(provider, 403, "SignatureDoesNotMatch", "AWS could not verify the request's signature. Either the Secret Access Key does not match this Access Key ID, or something between this machine and AWS changed the request.");
                Named(provider, 403, "AccessDenied", $"AWS refused access to the S3 bucket '{Bucket}' for this Access Key ID.");

                // Left masked: not in the list, or a listed code with a status that does not fit it.
                foreach (var (s, c) in new[] { (500, "InternalError"), (403, "RequestTimeTooSkewed"), (400, "AuthorizationHeaderMalformed"), (301, "PermanentRedirect"), (404, "NoSuchKey"), (404, "AccessDenied") })
                {
                    var masked = RunWith(provider, s, c);
                    Check(!masked.Success && masked.ErrorMessage.StartsWith("Extension execution failed (") && Clean(masked), $"{s} {c} stays masked");
                }

                Console.WriteLine($"AWS bridge checks PASS: {checks} checks.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
            finally { Runner.Program.ServiceUrlOverride = null; listener.Abort(); }
        }

        private static void Named(BridgeProvider provider, int s, string c, string sentence)
        {
            var response = RunWith(provider, s, c);
            Check(!response.Success && response.ErrorMessage == sentence, $"{c} is named: {sentence}");
            Check(Clean(response), $"{c}: neither key, secret nor the SDK's text is in the response");
        }

        private static bool Clean(ExtensionResponseEnvelope r) =>
            new[] { r.ErrorMessage, r.PayloadJson }.All(t => !t.Contains(KeyId) && !t.Contains(Secret) && !t.Contains(SdkText) && !t.Contains("127.0.0.1"));

        private static ExtensionResponseEnvelope RunWith(BridgeProvider provider, int s, string c)
        {
            status = s; code = c;
            return Process(provider, Params());
        }

        private static void Serve(HttpListener listener)
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try { context = listener.GetContext(); } catch { return; }
                try
                {
                    byte[] received;
                    using (var body = new MemoryStream()) { context.Request.InputStream.CopyTo(body); received = body.ToArray(); }
                    context.Response.StatusCode = status;
                    if (code == null) { uploads++; context.Response.Headers["ETag"] = "\"" + BitConverter.ToString(System.Security.Cryptography.MD5.HashData(Unchunk(received))).Replace("-", "").ToLowerInvariant() + "\""; }
                    else
                    {
                        byte[] xml = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Error><Code>{code}</Code><Message>{SdkText}</Message><RequestId>R1</RequestId><HostId>H1</HostId></Error>");
                        context.Response.ContentType = "application/xml";
                        context.Response.OutputStream.Write(xml, 0, xml.Length);
                    }
                    context.Response.Close();
                }
                catch { }
            }
        }

        // The SDK checks the ETag against the MD5 of what it sent; it may send aws-chunked framing.
        private static byte[] Unchunk(byte[] raw)
        {
            string head = Encoding.ASCII.GetString(raw, 0, Math.Min(raw.Length, 80));
            if (!head.Contains(";chunk-signature=")) return raw;
            var output = new MemoryStream();
            int pos = 0;
            while (pos < raw.Length)
            {
                int eol = Array.IndexOf(raw, (byte)'\r', pos);
                int size = Convert.ToInt32(Encoding.ASCII.GetString(raw, pos, eol - pos).Split(';')[0], 16);
                if (size == 0) break;
                output.Write(raw, eol + 2, size);
                pos = eol + 2 + size + 2;
            }
            return output.ToArray();
        }

        private static int FreePort()
        {
            var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            int port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private static ExtensionResponseEnvelope Fail(BridgeProvider provider, List<ExtensionNamedValue> values)
        {
            var response = Process(provider, values);
            Check(!response.Success, "the step fails");
            return response;
        }

        private static ExtensionResponseEnvelope Process(BridgeProvider provider, List<ExtensionNamedValue> values)
        {
            var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(provider, ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = Id }).PayloadJson);
            var request = new ExtensionInvokeRequest { ProviderId = Id, TypeName = catalog.Extensions.Single().TypeName, Kind = "Activity",
                SessionId = Guid.NewGuid().ToString("N"), Phase = "Prepare", DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(60).ToString("O"),
                Message = new ExtensionMessage { MessageType = 13, Text = "hello" }, ResponseMessage = new ExtensionMessage { MessageType = 13, Text = "" } };
            Call(provider, ExtensionBridgeProtocol.Invoke, request);
            request.Phase = "Process"; request.Parameters = values;
            var response = provider.Dispatch(Envelope(ExtensionBridgeProtocol.Invoke, request));
            if (response.Success) { request.Phase = "Close"; Call(provider, ExtensionBridgeProtocol.Invoke, request); }
            return response;
        }

        private static List<ExtensionNamedValue> Params(params (string Name, string Value)[] overrides)
        {
            var all = new Dictionary<string, string> { ["Bucket Name"] = Bucket, ["File Name"] = "out/test.txt", ["Region"] = "us-west-1", ["Access Key ID"] = KeyId, ["Secret Access Key"] = Secret };
            foreach (var o in overrides) all[o.Name] = o.Value;
            return all.Where(p => p.Value != null).Select(p => new ExtensionNamedValue { Name = p.Key, Value = p.Value }).ToList();
        }

        private static ExtensionRequestEnvelope Envelope<T>(string operation, T value) => new ExtensionRequestEnvelope { RequestId = Guid.NewGuid().ToString("N"), Operation = operation, PayloadJson = PersistentRunnerJson.Serialize(value) };

        private static ExtensionResponseEnvelope Call<T>(BridgeProvider provider, string operation, T value)
        {
            var response = provider.Dispatch(Envelope(operation, value)); if (!response.Success) throw new Exception(response.ErrorMessage); return response;
        }

        private static void Check(bool success, string description) { if (!success) throw new Exception("Failed: " + description); checks++; }
    }
}
