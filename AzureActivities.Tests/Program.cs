using Azure.Storage.Blobs;
using HL7Soup.Integrations.ExtensionBridge;
using Newtonsoft.Json.Linq;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

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
                Func<string, string, BlobClientOptions, BlobContainerClient> factory = (connection, container, options) =>
                {
                    Check(!options.TrimBlobNameSlashes, "production options preserve name slashes");
                    options.Transport = transport;
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
                Download(new byte[] { 1 }, r => Check(Uri.UnescapeDataString(new Uri(r.Uri).AbsolutePath) == "/inbox/   ", "whitespace-only blob identity preserved"));
                Check(Run("Azure Blob Download", Params("File Name", "   "), 14).ResponseMessage.Text == "AQ==", "nonempty whitespace-only blob name is valid");
                foreach (string key in new[] { "/key", "key/", "/key/", "/", "///", "a/%2e%2e/\u00e9 #?" })
                {
                    foreach (bool versioned in new[] { false, true })
                    {
                        Action<CapturedRequest> identity = r =>
                        {
                            Check(Uri.UnescapeDataString(new Uri(r.Uri).AbsolutePath) == "/inbox/" + key, "slashes preserved for HEAD/GET, including version-qualified names");
                            Check(!versioned || Uri.UnescapeDataString(new Uri(r.Uri).Query).Contains("versionid=v+1"), "version survives without rebuilding the path");
                        };
                        Download(new byte[] { 1 }, identity, identity);
                        var values = Params("File Name", key);
                        if (versioned) values.Add(new ExtensionNamedValue { Name = "Version ID", Value = "v+1" });
                        Check(Run("Azure Blob Download", values).ResponseMessage.Text == "AQ==", "slash-bearing object downloaded");
                    }
                    transport.Enqueue(r => Check(Uri.UnescapeDataString(new Uri(r.Uri).AbsolutePath) == "/inbox/" + key, "upload preserves slash-bearing identity"), 201, null);
                    Check(Run("Azure Blob Upload", Params("File Name", key), 13, "synthetic").ResponseMessage.Text == "Code Executed Successfully", "slash-bearing object upload");
                }
                foreach (string key in new[] { ".", "..", "a/../b", "../other-container/blob" })
                {
                    Check(!RunFailure("Azure Blob Download", Params("File Name", key)).Success, "URI-normalized alias rejected before read");
                    Check(!RunFailure("Azure Blob Upload", Params("File Name", key, "Create Container If Missing", "True")).Success, "URI-normalized alias rejected before container creation or write");
                }
                var versionWithSas = Params("File Name", "/key/", "Version ID", "v+1");
                versionWithSas.Single(p => p.Name == "Connection String").Value = "BlobEndpoint=https://fixture.blob.core.windows.net/;SharedAccessSignature=sv=2021-12-02&sr=c&sp=r&sig=A%2B%2F%3D";
                Action<CapturedRequest> sas = r => Check(new Uri(r.Uri).Query.Contains("sig=A%2B%2F%3D") && Uri.UnescapeDataString(new Uri(r.Uri).AbsolutePath) == "/inbox//key/", "version query preserves encoded SAS signature and exact path");
                Download(new byte[] { 1 }, sas, sas);
                Check(Run("Azure Blob Download", versionWithSas).ResponseMessage.Text == "AQ==", "version-qualified SAS read");
                var versionWithKey = Params("File Name", "/key/", "Version ID", "v+1");
                versionWithKey.Single(p => p.Name == "Connection String").Value = "DefaultEndpointsProtocol=https;AccountName=fixture;AccountKey=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=;EndpointSuffix=core.windows.net";
                Action<CapturedRequest> sharedKey = r => Check(r.Headers["Authorization"].StartsWith("SharedKey fixture:") && Uri.UnescapeDataString(new Uri(r.Uri).Query).Contains("versionid=v+1"), "shared-key authentication signs the version-qualified request");
                Download(new byte[] { 1 }, sharedKey, sharedKey);
                Check(Run("Azure Blob Download", versionWithKey).ResponseMessage.Text == "AQ==", "version-qualified shared-key read");
                TestBodyResumesAndCancellation();

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
                Check(!RunFailure("Azure Blob Download", Params("File Name", "a", "Expected ETag", " * ")).Success, "download wildcard ETag rejected before network");
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
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "Overwrite Existing Blob", "True", "Expected ETag", " * ")).Success, "upload wildcard ETag rejected before network");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "New Container Access", "Container")).Success, "unsupported access value rejected before network");
                transport.Enqueue(r => Check(r.Headers["x-ms-blob-public-access"] == "blob", "public access is explicit"), 409, "<Error><Code>PublicAccessNotPermitted</Code><Message>Account disallows public access</Message></Error>");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "Create Container If Missing", "True", "New Container Access", "Blob")).Success, "account public-access denial fails without upload or permission fallback");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "New Container Access", "Blob")).Success, "public creation requires explicit provision option");
                Check(!RunFailure("Azure Blob Upload", Params("File Name", "a", "Create Container If Missing", "yes")).Success, "invalid provisioning setting rejected before network");
                Check(transport.Pending == 0 && transport.UnexpectedRequests == 0, "all expected requests consumed; no unplanned SDK requests");
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
        private static void TestBodyResumesAndCancellation()
        {
            CancellationToken token = default;
            transport.Enqueue(r => { token = r.Cancellation; Check(token.CanBeCanceled, "HEAD has a cancellable overall budget"); }, 200, null, 4);
            transport.Enqueue(r => Check(r.Cancellation.CanBeCanceled && r.Headers["If-Match"] == "\"v1\"", "initial GET is cancellable and uses inspected ETag"), 200, new InterruptedStream(1, 2), 4);
            transport.Enqueue(r =>
            {
                Check(r.Method == "GET" && Range(r) == "bytes=2-" && r.Headers["If-Match"] == "\"v1\"", "body resumes at consumed offset with pinned ETag");
                Check(r.Cancellation.CanBeCanceled && Uri.UnescapeDataString(new Uri(r.Uri).AbsolutePath) == "/inbox//resume/" && Uri.UnescapeDataString(new Uri(r.Uri).Query).Contains("versionid=v+1"), "resume retains exact key/version and cancellable I/O");
            }, 206, new byte[] { 3, 4 }, 2, "bytes 2-3/4");
            Check(Run("Azure Blob Download", Params("File Name", "/resume/", "Version ID", "v+1")).ResponseMessage.Text == "AQIDBA==", "resumed body has exact bytes without duplication");

            transport.Enqueue(r => {}, 200, null, 4);
            transport.Enqueue(r => {}, 200, new InterruptedStream(1, 2), 4);
            transport.Enqueue(r => Check(r.Headers["If-Match"] == "\"v1\"" && Range(r) == "bytes=2-", "resume rejects changed blob instead of mixing versions"), 412, "<Error><Code>ConditionNotMet</Code></Error>");
            Check(!RunFailure("Azure Blob Download", Params("File Name", "changed-during-read")).Success, "changed body fails the entire download");

            int gets = 0;
            transport.Enqueue(r => {}, 200, null, 7);
            for (int index = 0; index < 6; index++)
            {
                int start = index;
                transport.Enqueue(r =>
                {
                    gets++;
                    Check(r.Method == "GET" && r.Headers["If-Match"] == "\"v1\"", "every reliability resume remains read-only and conditional");
                    Check(start == 0 || Range(r) == "bytes=" + start + "-", "resume budget is cumulative despite successful partial reads");
                }, index == 0 ? 200 : 206, new InterruptedStream((byte)(index + 1)), 7 - index, index == 0 ? null : "bytes " + index + "-6/7");
            }
            Check(!RunFailure("Azure Blob Download", Params("File Name", "interrupted-repeatedly")).Success && gets == 6, "at most five body resumes; exhausted budget fails without whole-activity replay");

            foreach (bool resume in new[] { false, true })
            {
                if (resume)
                {
                    transport.Enqueue(r => {}, 200, null, 4);
                    transport.Enqueue(r => {}, 200, new InterruptedStream(1, 2), 4);
                }
                transport.Enqueue(r =>
                {
                    Check(r.Cancellation.WaitHandle.WaitOne(5000), "overall deadline cancels a blocked HEAD or resume request");
                    r.Cancellation.ThrowIfCancellationRequested();
                }, 200, null);
                var blocked = RunFailure("Azure Blob Download", Params("File Name", "blocked-headers"), deadlineMilliseconds: 300);
                Check(!blocked.Success && PersistentRunnerJson.Deserialize<ExtensionFailure>(blocked.PayloadJson).Code == "DeadlineExceeded", "deadline covers HEAD and resumed GET headers without resetting");
            }

            foreach (bool resume in new[] { false, true })
            {
            var expired = new WaitingStream();
            transport.Enqueue(r => {}, 200, null, 1);
            if (resume) transport.Enqueue(r => {}, 200, new InterruptedStream(), 1);
            transport.Enqueue(r => {}, 200, expired, 1);
            var watch = Stopwatch.StartNew();
            var timeout = RunFailure("Azure Blob Download", Params("File Name", "deadline"), deadlineMilliseconds: 300);
            Check(!timeout.Success && PersistentRunnerJson.Deserialize<ExtensionFailure>(timeout.PayloadJson).Code == "DeadlineExceeded", "earlier bridge deadline cancels an active stream");
            Check(expired.Started.Task.IsCompleted && expired.Started.Task.Result.IsCancellationRequested, "deadline cancels the active stream read token");
            Check(expired.Disposed && watch.ElapsedMilliseconds < 5000, "deadline disposes response promptly without a resume");
            Check(!SourceAdapterDispatch.Cancellation.CanBeCanceled && SourceAdapterDispatch.Handler == null, "deadline clears ambient bridge state");
            }

            var interrupted = new WaitingStream();
            transport.Enqueue(r => {}, 200, null, 1);
            transport.Enqueue(r => {}, 200, interrupted, 1);
            var request = PrepareProcess("Azure Blob Download", Params("File Name", "cancel"));
            var envelope = Envelope(ExtensionBridgeProtocol.Invoke, request);
            var work = Task.Run(() =>
            {
                var response = provider.Dispatch(envelope);
                Check(!SourceAdapterDispatch.Cancellation.CanBeCanceled && SourceAdapterDispatch.Handler == null, "cancel clears ambient bridge state on dispatch thread");
                return response;
            });
            Check(interrupted.Started.Task.Wait(5000), "download reached cancellable body read");
            var acknowledgement = provider.Dispatch(Envelope(ExtensionBridgeProtocol.Cancel, new ExtensionCancelRequest { ProviderId = "popokey.azureactivities", SessionId = request.SessionId, TargetRequestId = envelope.RequestId }), true);
            Check(acknowledgement.Success && work.Wait(5000), "control cancellation completes a blocked download");
            Check(!work.Result.Success && PersistentRunnerJson.Deserialize<ExtensionFailure>(work.Result.PayloadJson).Code == "Cancelled" && interrupted.Disposed, "cancel returns failure and disposes content without a resume");
            Check(!provider.Dispatch(envelope).Success, "cancelled invocation cannot be replayed with its original identity");
            Download(new byte[] { 1 });
            Check(Run("Azure Blob Download", Params("File Name", "after-cancel")).ResponseMessage.Text == "AQ==", "later invocation has a fresh uncancelled budget");
        }
        private static string Range(CapturedRequest request) => request.Headers.TryGetValue("x-ms-range", out string range) ? range : request.Headers.TryGetValue("Range", out range) ? range : null;
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
        private static ExtensionResponseEnvelope RunFailure(string name, List<ExtensionNamedValue> values, int outputType = 14, string input = "", int inputType = 13, int deadlineMilliseconds = 120000)
        {
            var request = PrepareProcess(name, values, outputType, input, inputType, deadlineMilliseconds);
            byte[] wire = BridgeWire.DispatchJson(Encoding.UTF8.GetBytes(PersistentRunnerJson.Serialize(Envelope(ExtensionBridgeProtocol.Invoke, request))), provider, ExtensionBridgeProtocol.DefaultMaxMessageBytes);
            lastWireBytes = wire.Length;
            var response = PersistentRunnerJson.Deserialize<ExtensionResponseEnvelope>(Encoding.UTF8.GetString(wire));
            if (response.Success) { request.Phase = "Close"; Call(ExtensionBridgeProtocol.Invoke, request); }
            return response;
        }
        private static ExtensionInvokeRequest PrepareProcess(string name, List<ExtensionNamedValue> values, int outputType = 14, string input = "", int inputType = 13, int deadlineMilliseconds = 120000)
        {
            var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = "popokey.azureactivities" }).PayloadJson);
            var request = new ExtensionInvokeRequest { ProviderId = "popokey.azureactivities", TypeName = catalog.Extensions.Single(d => d.DisplayName == name).TypeName,
                Kind = "Activity", SessionId = Guid.NewGuid().ToString("N"), Phase = "Prepare", DeadlineUtc = DateTimeOffset.UtcNow.AddMilliseconds(deadlineMilliseconds).ToString("O") };
            Call(ExtensionBridgeProtocol.Invoke, request);
            request.Phase = "Process"; request.Parameters = values; request.Message = name == "Azure Blob Upload" ? new ExtensionMessage { MessageType = inputType, Text = input } : null; request.ResponseMessage = new ExtensionMessage { MessageType = outputType, Text = "" };
            return request;
        }
        private static ExtensionRequestEnvelope Envelope<T>(string operation, T value) => new ExtensionRequestEnvelope { RequestId = Guid.NewGuid().ToString("N"), Operation = operation, PayloadJson = PersistentRunnerJson.Serialize(value) };
        private static ExtensionResponseEnvelope Call<T>(string operation, T value)
        {
            var response = provider.Dispatch(Envelope(operation, value)); if (!response.Success) throw new Exception(response.ErrorMessage); return response;
        }
        private static void Check(bool success, string description) { if (!success) throw new Exception("Failed: " + description); Interlocked.Increment(ref checks); }
    }
}
