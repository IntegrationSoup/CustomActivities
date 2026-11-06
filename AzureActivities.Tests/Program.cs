using Azure.Storage.Blobs;
using HL7Soup.Integrations.ExtensionBridge;
using Newtonsoft.Json.Linq;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace AzureActivities.Tests
{
    internal static class Checks
    {
        private static int checks;
        private static FakeTransport transport;
        private static BridgeProvider provider;
        private static int lastWireBytes;
        private const string Connection = "BlobEndpoint=https://fixture.blob.core.windows.net/;SharedAccessSignature=sv=2021-12-02&sr=c&sp=r&sig=SYNTHETIC-NOT-A-SECRET";

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--server") return LegacyChecks.Server(args);
                if (args.Length > 0 && args[0] == "--legacy") return LegacyChecks.Run(args[1], args[2]);
                transport = new FakeTransport();
                Func<string, string, BlobContainerClient> factory = (connection, container) =>
                {
                    var options = new BlobClientOptions { Transport = transport }; options.Retry.MaxRetries = 0;
                    return new BlobContainerClient(connection, container, options);
                };
                Runner.Program.ContainerClientFactory = factory;
                Runner.BlobDownloadOperation.ContainerClientFactory = factory;
                var handler = (PersistentRunnerRequestHandler)Delegate.CreateDelegate(typeof(PersistentRunnerRequestHandler), typeof(Runner.Program).GetMethod("HandleServerRequest", BindingFlags.Static | BindingFlags.NonPublic));
                provider = new BridgeProvider("popokey.azureactivities", "AzureActivities", handler, typeof(BlobSender), typeof(BlobDownload));
                var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = "popokey.azureactivities" }).PayloadJson);
                Check(catalog.Extensions.Count == 2, "Upload and Download catalog contains no other new actions");
                var descriptor = catalog.Extensions.Single(d => d.DisplayName == "Azure Blob Download");
                Check(descriptor.TypeName.Split(',')[0] == typeof(BlobDownload).FullName, "download descriptor uses stable activity type");

                byte[] bytes = { 0, 255, 128, 13, 10 };
                Download(bytes, r => Check(r.Headers["If-Match"] == "\"v1\"", "supplied ETag checked"), r =>
                {
                    Check(r.Method == "GET" && r.Headers["If-Match"] == "\"v1\"", "GET pinned to inspected ETag");
                    Check(r.Uri.Contains("sp=r"), "read-only SAS reaches SDK");
                    Check(Uri.UnescapeDataString(new Uri(r.Uri).Query).Contains("versionid=v+1"), "version ID preserved");
                    Check(r.Uri.Contains("inbound/a%20%23%3F.bin"), "exact case and special characters preserved");
                });
                var binary = Run("Azure Blob Download", Params("File Name", "inbound/a #?.bin", "Version ID", "v+1", "Expected ETag", "\"v1\""), 14);
                Check(Convert.FromBase64String(binary.ResponseMessage.Text).SequenceEqual(bytes), "binary byte fidelity");
                var metadata = JObject.Parse(binary.VariableUpdates.Single(v => v.Name == "AzureBlobMetadataJson").Value);
                Check((int)metadata["size"] == bytes.Length && (string)metadata["name"] == "inbound/a #?.bin" && (string)metadata["contentType"] == "application/octet-stream", "metadata contains identity, size and content type");
                Download(bytes);
                Check(Convert.FromBase64String(Run("Azure Blob Download", Params("File Name", "a.dcm"), 16).ResponseMessage.Text).SequenceEqual(bytes), "DICOM byte fidelity");
                Download(Encoding.UTF8.GetBytes("MSH|^~\\&|caf\u00e9\r"));
                Check(Run("Azure Blob Download", Params("File Name", "a.hl7"), 13).ResponseMessage.Text == "MSH|^~\\&|caf\u00e9\r", "UTF-8 text fidelity");
                Download(new byte[] { 0xE9 });
                Check(Run("Azure Blob Download", Params("File Name", "a.txt", "Text Encoding", "windows-1252"), 13).ResponseMessage.Text == "\u00e9", "explicit code-page decoding");
                Download(new byte[] { 0xFF });
                Check(!RunFailure("Azure Blob Download", Params("File Name", "invalid.txt"), 13).Success, "invalid UTF-8 fails");
                Download(Array.Empty<byte>());
                Check(Run("Azure Blob Download", Params("File Name", "empty.bin"), 14).ResponseMessage.Text == "", "empty blob is valid");
                Download(new byte[] { 0xEF, 0xBB, 0xBF, 0x41 });
                Check(Run("Azure Blob Download", Params("File Name", "bom.txt"), 13).ResponseMessage.Text == "\uFEFFA", "UTF-8 BOM preserved as text character");
                Download(new byte[] { 1 }, r => Check(Uri.UnescapeDataString(new Uri(r.Uri).AbsolutePath) == "/inbox/ \u00e9/a ", "Unicode and significant spaces preserved on wire"));
                Check(Run("Azure Blob Download", Params("File Name", " \u00e9/a "), 14).ResponseMessage.Text == "AQ==", "Unicode and significant whitespace accepted");

                if (!args.Contains("--quick"))
                {
                byte[] maximumBinary = Enumerable.Repeat((byte)0xFF, 8 * 1024 * 1024).ToArray();
                Download(maximumBinary);
                Check(Convert.FromBase64String(Run("Azure Blob Download", Params("File Name", "max.bin"), 14).ResponseMessage.Text).SequenceEqual(maximumBinary), "maximum binary survives complete V5 nested envelope");
                Check(lastWireBytes < ExtensionBridgeProtocol.DefaultMaxMessageBytes, "base64 slash-heavy response fits default V5 limit");
                Console.WriteLine("Maximum binary V5 envelope bytes: " + lastWireBytes);
                int legacyBytes = Encoding.UTF8.GetByteCount(PersistentRunnerJson.Serialize(PersistentRunnerResponseEnvelope.CreateSuccess("fixture", PersistentRunnerJson.Serialize(new BlobDownloadResponse { Base64 = Convert.ToBase64String(maximumBinary), MetadataJson = "{}" }))));
                Check(legacyBytes < ExtensionBridgeProtocol.DefaultMaxMessageBytes, "maximum binary survives legacy nested envelope within conservative limit");
                Console.WriteLine("Maximum binary V4 envelope bytes: " + legacyBytes);
                Download(new byte[8 * 1024 * 1024]);
                Check(Run("Azure Blob Download", Params("File Name", "max.txt"), 13).ResponseMessage.Text.Length == 8 * 1024 * 1024, "maximum control-heavy text survives nested envelope");
                Check(lastWireBytes < ExtensionBridgeProtocol.DefaultMaxMessageBytes, "escaped text response fits default V5 limit");
                Console.WriteLine("Maximum text V5 envelope bytes: " + lastWireBytes);
                }

                Check(!RunFailure("Azure Blob Download", Params("File Name", "a", "Maximum Download MB", "0")).Success, "invalid lower size rejected before network");
                Check(!RunFailure("Azure Blob Download", Params("File Name", "a", "Maximum Download MB", "9")).Success, "invalid upper size rejected before network");
                Check(!RunFailure("Azure Blob Download", Params()).Success, "missing exact name rejected before network");
                var badCredential = Params("File Name", "a");
                badCredential.Single(p => p.Name == "Connection String").Value = "invalid";
                Check(!RunFailure("Azure Blob Download", badCredential).Success, "invalid credential format fails without content read");
                badCredential.Single(p => p.Name == "Connection String").Value = "";
                Check(!RunFailure("Azure Blob Download", badCredential).Success, "missing credential fails before network");
                Check(!RunFailure("Azure Blob Download", Params("File Name", "a", "Text Encoding", "not-an-encoding"), 13).Success, "invalid encoding rejected before network");
                transport.Enqueue(r => Check(r.Method == "HEAD", "size check uses properties only"), 200, null, 2 * 1024 * 1024);
                Check(!RunFailure("Azure Blob Download", Params("File Name", "large.bin", "Maximum Download MB", "1")).Success, "oversize metadata prevents content read");
                transport.Enqueue(r => {}, 200, null, 1);
                transport.Enqueue(r => {}, 200, new byte[1024 * 1024 + 1]);
                Check(!RunFailure("Azure Blob Download", Params("File Name", "growing.bin", "Maximum Download MB", "1")).Success, "stream size also bounded");
                transport.Enqueue(r => {}, 200, null, 10);
                transport.Enqueue(r => {}, 200, new byte[] { 1 });
                Check(!RunFailure("Azure Blob Download", Params("File Name", "truncated.bin")).Success, "incomplete content fails");
                foreach (int status in new[] { 403, 404, 412, 503 })
                {
                    transport.Enqueue(r => Check(r.Method == "HEAD", "failure path remains read-only"), status, "<Error><Code>Synthetic</Code><Message>DO-NOT-ECHO-SIGNED-SECRET</Message></Error>");
                    var failure = RunFailure("Azure Blob Download", Params("File Name", "a"));
                    Check(!failure.Success && !failure.ErrorMessage.Contains("DO-NOT-ECHO"), "Azure failure propagated without remote body");
                }
                transport.Enqueue(r => {}, 200, null, 1);
                transport.Enqueue(r => Check(r.Headers["If-Match"] == "\"v1\"", "read rejects an object changed after HEAD"), 412, "<Error><Code>ConditionNotMet</Code></Error>");
                Check(!RunFailure("Azure Blob Download", Params("File Name", "changed")).Success, "changed object is not silently downloaded");

                // Existing workflows omit the new optional parameter. Only new container creation changes.
                Upload(null, false);
                Upload("Private", false);
                Upload("Blob", false);
                Upload(null, true);
                Upload("Blob", true);
                Upload("Private", true, true);
                Upload("Private", true, true, "\"v1\"");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "invalid.bin"), 13, "not base64!", 14).Success, "invalid Binary content fails before Azure requests");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "invalid.dcm"), 13, "not base64!", 16).Success, "invalid DICOM content fails before Azure requests");
                transport.Enqueue(r => Check(r.Method == "PUT" && r.Body.SequenceEqual(new byte[] { 0, 255, 1 }), "Binary upload preserves raw bytes without container provisioning"), 201, null);
                Check(RunFailure("Azure Blob Upload", Params("File Name", "raw.bin"), 13, "AP8B", 14).Success, "default upload uses existing container only");
                transport.Enqueue(r => Check(r.Method == "PUT" && !r.Uri.Contains("comp=block") && r.Body.Length == 8 * 1024 * 1024, "maximum upload is single PUT without staged blocks"), 201, null);
                Check(Run("Azure Blob Upload", Params("File Name", "large", "Overwrite Existing Blob", "True"), 13, new string('a', 8 * 1024 * 1024)).ResponseMessage.Text == "Code Executed Successfully", "maximum bounded upload succeeds");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "too-large"), 13, new string('a', 8 * 1024 * 1024 + 1)).Success, "larger upload rejected before container or blob write");
                transport.Enqueue(r => Check(r.Method == "PUT" && r.Headers["If-None-Match"] == "*", "existing blob guarded against implicit overwrite"), 412, "<Error><Code>ConditionNotMet</Code></Error>");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "exists")).Success, "default fails when blob exists without deleting it");
                transport.Enqueue(r => Check(r.Headers["If-Match"] == "\"stale\"", "replacement ETag is checked atomically"), 412, "<Error><Code>ConditionNotMet</Code></Error>");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "exists", "Overwrite Existing Blob", "True", "Expected ETag", "\"stale\"")).Success, "changed blob not replaced");
                transport.Enqueue(r => Check(r.Method == "PUT", "failed replacement never deletes old blob or snapshots"), 503, "<Error><Code>ServerBusy</Code></Error>");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "exists", "Overwrite Existing Blob", "True")).Success, "failed write is surfaced without replay");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "Overwrite Existing Blob", "yes")).Success, "invalid overwrite mode rejected before network");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "Expected ETag", "\"v1\"")).Success, "ETag requires explicit replacement");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "New Container Access", "Container")).Success, "unsupported access value rejected before network");
                transport.Enqueue(r => Check(r.Headers["x-ms-blob-public-access"] == "blob", "public access is explicit"), 409, "<Error><Code>PublicAccessNotPermitted</Code><Message>Account disallows public access</Message></Error>");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "Create Container If Missing", "True", "New Container Access", "Blob")).Success, "account public-access denial fails without upload or permission fallback");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "New Container Access", "Blob")).Success, "public creation requires explicit provision option");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "Create Container If Missing", "yes")).Success, "invalid provisioning setting rejected before network");
                Check(transport.Pending == 0, "all expected SDK requests consumed; no unexpected retries");
                Console.WriteLine("PASS " + checks + " Azure SDK/bridge checks; fake transport only, no cloud network.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        private static void Download(byte[] bytes, Action<CapturedRequest> head = null, Action<CapturedRequest> get = null)
        {
            transport.Enqueue(r => { Check(r.Method == "HEAD", "download starts with read-only HEAD"); head?.Invoke(r); }, 200, null, bytes.Length);
            transport.Enqueue(r => { Check(r.Method == "GET" && r.Headers["If-Match"] == "\"v1\"", "download GET has inspected ETag"); get?.Invoke(r); }, 200, bytes);
        }
        private static void Upload(string access, bool existing, bool overwrite = false, string etag = null)
        {
            transport.Enqueue(r =>
            {
                Check(r.Method == "PUT" && r.Uri.Contains("restype=container") && !r.Uri.Contains("comp=acl"), "create request never changes existing ACL");
                Check(access == "Blob" ? r.Headers["x-ms-blob-public-access"] == "blob" : !r.Headers.ContainsKey("x-ms-blob-public-access"), "new container access uses private default or explicit blob read");
            }, existing ? 409 : 201, existing ? "<Error><Code>ContainerAlreadyExists</Code></Error>" : null);
            transport.Enqueue(r =>
            {
                Check(r.Method == "PUT" && r.Body.SequenceEqual(Encoding.UTF8.GetBytes("synthetic")), "upload sends content without deleting snapshots or prior content");
                Check(!r.Headers.Keys.Any(k => k.StartsWith("x-ms-meta-", StringComparison.OrdinalIgnoreCase)) && !r.Headers.ContainsKey("x-ms-tags"), "replacement sends default properties without metadata or tag merge");
                Check(!overwrite ? r.Headers["If-None-Match"] == "*" : !r.Headers.ContainsKey("If-None-Match"), "create-only is default; replacement is explicit");
                if (etag != null) Check(r.Headers["If-Match"] == etag, "replacement uses requested ETag");
            }, 201, null);
            var values = Params("File Name", "out.hl7", "Create Container If Missing", "True");
            if (access != null) values.Add(new ExtensionNamedValue { Name = "New Container Access", Value = access });
            if (overwrite) values.Add(new ExtensionNamedValue { Name = "Overwrite Existing Blob", Value = "True" });
            if (etag != null) values.Add(new ExtensionNamedValue { Name = "Expected ETag", Value = etag });
            var result = Run("Azure Blob Upload", values, 13, "synthetic");
            Check(result.ResponseMessage.Text == "Code Executed Successfully", "upload succeeds for new or pre-existing container");
            Check(result.VariableUpdates.Any(v => v.Name == "AzureBlobETag" && v.Value == "\"v1\""), "uploaded ETag returned for later conditional work");
        }
        private static List<ExtensionNamedValue> Params(params string[] extras)
        {
            var values = new List<ExtensionNamedValue> { new ExtensionNamedValue { Name = "Connection String", Value = Connection }, new ExtensionNamedValue { Name = "Container Name", Value = "inbox" } };
            for (int i = 0; i < extras.Length; i += 2) values.Add(new ExtensionNamedValue { Name = extras[i], Value = extras[i + 1] });
            return values;
        }
        private static ExtensionInvokeResult Run(string name, List<ExtensionNamedValue> values, int outputType = 14, string input = "")
        {
            var response = RunFailure(name, values, outputType, input);
            if (!response.Success) throw new Exception(response.ErrorMessage);
            return PersistentRunnerJson.Deserialize<ExtensionInvokeResult>(response.PayloadJson);
        }
        private static ExtensionResponseEnvelope RunFailure(string name, List<ExtensionNamedValue> values, int outputType = 14, string input = "", int inputType = 13)
        {
            var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = "popokey.azureactivities" }).PayloadJson);
            var request = new ExtensionInvokeRequest { ProviderId = "popokey.azureactivities", TypeName = catalog.Extensions.Single(d => d.DisplayName == name).TypeName,
                Kind = "Activity", SessionId = Guid.NewGuid().ToString("N"), Phase = "Prepare", DeadlineUtc = DateTimeOffset.UtcNow.AddMinutes(2).ToString("O") };
            Call(ExtensionBridgeProtocol.Invoke, request);
            request.Phase = "Process"; request.Parameters = values; request.Message = name == "Azure Blob Upload" ? new ExtensionMessage { MessageType = inputType, Text = input } : null; request.ResponseMessage = new ExtensionMessage { MessageType = outputType, Text = "" };
            byte[] wire = BridgeWire.DispatchJson(Encoding.UTF8.GetBytes(PersistentRunnerJson.Serialize(Envelope(ExtensionBridgeProtocol.Invoke, request))), provider, ExtensionBridgeProtocol.DefaultMaxMessageBytes);
            lastWireBytes = wire.Length;
            var response = PersistentRunnerJson.Deserialize<ExtensionResponseEnvelope>(Encoding.UTF8.GetString(wire));
            if (response.Success) { request.Phase = "Close"; Call(ExtensionBridgeProtocol.Invoke, request); }
            return response;
        }
        private static ExtensionRequestEnvelope Envelope<T>(string operation, T value) => new ExtensionRequestEnvelope { RequestId = Guid.NewGuid().ToString("N"), Operation = operation, PayloadJson = PersistentRunnerJson.Serialize(value) };
        private static ExtensionResponseEnvelope Call<T>(string operation, T value)
        {
            var response = provider.Dispatch(Envelope(operation, value)); if (!response.Success) throw new Exception(response.ErrorMessage); return response;
        }
        private static void Check(bool success, string description) { if (!success) throw new Exception("Failed: " + description); checks++; }
    }
}
