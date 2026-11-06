using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureActivities;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AzureActivities.Runner
{
    internal static class BlobDownloadOperation
    {
        internal static Func<string, string, BlobClientOptions, BlobContainerClient> ContainerClientFactory { get; set; }

        internal static BlobDownloadResponse Execute(BlobDownloadRequest request, CancellationToken cancellation = default)
        {
            // The public/DLL call remains synchronous; every I/O operation shares one budget.
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(120));
                return ExecuteAsync(request, deadline.Token).GetAwaiter().GetResult();
            }
        }

        private static async Task<BlobDownloadResponse> ExecuteAsync(BlobDownloadRequest request, CancellationToken cancellation)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.ContainerName) || string.IsNullOrEmpty(request.FileName))
                throw new ArgumentException("Connection String, Container Name and File Name are required.");
            if (request.FileName.Length > 1024 || request.ContainerName.Length > 63 || request.ConnectionString.Length > 16384 || request.VersionId?.Length > 256 || request.ExpectedETag?.Length > 256)
                throw new ArgumentException("A blob identity or connection setting exceeds its supported length.");
            // Worst-case control-character text expands in both nested JSON strings.
            // 8 MiB leaves headroom inside the default 64 MiB bridge envelope.
            if (request.MaximumBytes < 1 || request.MaximumBytes > 8 * 1024 * 1024)
                throw new ArgumentException("The maximum download size must be between 1 byte and 8 MiB.");
            if (request.ExpectedETag?.Trim() == "*") throw new ArgumentException("Expected ETag must identify a specific blob; wildcard * is not supported.");
            cancellation.ThrowIfCancellationRequested();
            try
            {
                using (var policy = new DownloadRequestPolicy(request.VersionId))
                {
                    var options = new BlobClientOptions { TrimBlobNameSlashes = false };
                    options.Retry.MaxRetries = 0;
                    options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
                    options.AddPolicy(policy, HttpPipelinePosition.PerCall);
                    var container = ContainerClientFactory != null ? ContainerClientFactory(request.ConnectionString, request.ContainerName, options)
                        : new BlobContainerClient(request.ConnectionString, request.ContainerName, options);
                    BlobClient blob = AzureBlobIdentity.ExactClient(container, request.FileName);
                    var conditions = string.IsNullOrWhiteSpace(request.ExpectedETag) ? null : new BlobRequestConditions { IfMatch = new ETag(request.ExpectedETag) };
                    // Read-only path: no container creation, listing, upload, delete or access-policy calls.
                    BlobProperties properties = (await blob.GetPropertiesAsync(conditions, cancellationToken: cancellation).ConfigureAwait(false)).Value;
                    if (properties.ContentLength > request.MaximumBytes) throw new InvalidOperationException("The blob exceeds Maximum Download MB.");
                    var response = await blob.DownloadStreamingAsync(new BlobDownloadOptions { Conditions = new BlobRequestConditions { IfMatch = properties.ETag } }, cancellation).ConfigureAwait(false);
                    using (response.GetRawResponse())
                    using (var download = response.Value)
                    using (var output = new MemoryStream())
                    {
                        byte[] buffer = new byte[81920];
                        int count;
                        // This SDK may resume an interrupted body up to five times. Resumes
                        // retain the version, inspected ETag and original cancellation budget.
                        while ((count = await download.Content.ReadAsync(buffer, 0, buffer.Length, cancellation).ConfigureAwait(false)) > 0)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            if (output.Length + count > request.MaximumBytes) throw new InvalidOperationException("The blob exceeds Maximum Download MB.");
                            output.Write(buffer, 0, count);
                        }
                        cancellation.ThrowIfCancellationRequested();
                        if (output.Length != properties.ContentLength) throw new InvalidOperationException("The blob download was incomplete.");
                        return new BlobDownloadResponse
                        {
                            Base64 = Convert.ToBase64String(output.ToArray()),
                            MetadataJson = JsonConvert.SerializeObject(new { name = request.FileName, size = output.Length, etag = properties.ETag.ToString(),
                                contentType = properties.ContentType, versionId = properties.VersionId, lastModifiedUtc = properties.LastModified.UtcDateTime.ToString("o") })
                        };
                    }
                }
            }
            catch (RequestFailedException exception)
            {
                string reason = exception.Status == 404 ? "Blob or container was not found." : exception.Status == 403 ? "Read access was denied. Check the blob read permission and credential expiry."
                    : exception.Status == 412 ? "The blob changed or Expected ETag did not match." : "Azure Blob download failed.";
                // SDK error bodies may contain signed URLs or resource details.
                throw new InvalidOperationException(reason + " Azure HTTP status: " + exception.Status + ".");
            }
            catch (ArgumentException) { throw new InvalidOperationException("Azure Blob download failed. Check the connection string and resource names."); }
            catch (FormatException) { throw new InvalidOperationException("Azure Blob download failed. Check the connection string and resource names."); }
            catch (System.Net.Http.HttpRequestException) { throw new InvalidOperationException("Azure Blob download failed. Check the storage connection and network."); }
            catch (IOException) { throw new InvalidOperationException("Azure Blob download failed. Check the storage connection and network."); }
            catch (AggregateException) { throw new InvalidOperationException("Azure Blob download failed after interrupted body reads. Check the storage connection and network."); }
        }

        // 12.14.0's WithVersion rebuilds the path with default slash trimming.
        // Add only the version query before authentication, preserving the exact path
        // and all other SAS query fields. The policy also runs for body-resume GETs.
        private sealed class DownloadRequestPolicy : HttpPipelineSynchronousPolicy, IDisposable
        {
            private readonly string version;
            private readonly List<Response> responses = new List<Response>();
            private readonly List<Stream> bodies = new List<Stream>();
            internal DownloadRequestPolicy(string version) { this.version = version; }
            public override void OnSendingRequest(HttpMessage message)
            {
                if (string.IsNullOrWhiteSpace(version)) return;
                string query = message.Request.Uri.Query?.TrimStart('?') ?? string.Empty;
                message.Request.Uri.Query = string.Join("&", query.Split('&').Where(part => !string.Equals(Uri.UnescapeDataString(part.Split('=')[0]), "versionid", StringComparison.OrdinalIgnoreCase)));
                message.Request.Uri.AppendQuery("versionid", version, true);
            }
            public override void OnReceivedResponse(HttpMessage message)
            {
                lock (responses)
                {
                    responses.Add(message.Response);
                    if (message.Response.ContentStream != null) bodies.Add(message.Response.ContentStream);
                }
            }
            public void Dispose()
            {
                // Own every HTTP response, including resumed bodies. The SDK's
                // outer content stream is non-disposing in this pinned version.
                Response[] owned;
                Stream[] ownedBodies;
                lock (responses) { owned = responses.ToArray(); ownedBodies = bodies.ToArray(); responses.Clear(); bodies.Clear(); }
                foreach (var response in owned) response.Dispose();
                foreach (var body in ownedBodies) body.Dispose();
            }
        }
    }
}
