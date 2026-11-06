using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using AzureActivities;
using Newtonsoft.Json;
using System;
using System.IO;

namespace AzureActivities.Runner
{
    internal static class BlobDownloadOperation
    {
        internal static Func<string, string, BlobContainerClient> ContainerClientFactory { get; set; }

        internal static BlobDownloadResponse Execute(BlobDownloadRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.ConnectionString) || string.IsNullOrWhiteSpace(request.ContainerName) || string.IsNullOrEmpty(request.FileName))
                throw new ArgumentException("Connection String, Container Name and File Name are required.");
            if (request.FileName.Length > 1024 || request.ContainerName.Length > 63 || request.ConnectionString.Length > 16384 || request.VersionId?.Length > 256 || request.ExpectedETag?.Length > 256)
                throw new ArgumentException("A blob identity or connection setting exceeds its supported length.");
            // Worst-case control-character text expands in both nested JSON strings.
            // 8 MiB leaves headroom inside the default 64 MiB bridge envelope.
            if (request.MaximumBytes < 1 || request.MaximumBytes > 8 * 1024 * 1024)
                throw new ArgumentException("The maximum download size must be between 1 byte and 8 MiB.");
            try
            {
                var options = new BlobClientOptions();
                options.Retry.MaxRetries = 0;
                options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
                var container = ContainerClientFactory != null ? ContainerClientFactory(request.ConnectionString, request.ContainerName)
                    : new BlobContainerClient(request.ConnectionString, request.ContainerName, options);
                BlobClient blob = container.GetBlobClient(request.FileName);
                if (!string.IsNullOrWhiteSpace(request.VersionId)) blob = blob.WithVersion(request.VersionId);
                var conditions = string.IsNullOrWhiteSpace(request.ExpectedETag) ? null : new BlobRequestConditions { IfMatch = new ETag(request.ExpectedETag) };
                // Read-only path: no container creation, listing, upload, delete or access-policy calls.
                BlobProperties properties = blob.GetProperties(conditions).Value;
                if (properties.ContentLength > request.MaximumBytes) throw new InvalidOperationException("The blob exceeds Maximum Download MB.");
                var download = blob.DownloadStreaming(new BlobDownloadOptions { Conditions = new BlobRequestConditions { IfMatch = properties.ETag } }).Value;
                using (download.Content)
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[81920];
                    int count;
                    while ((count = download.Content.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + count > request.MaximumBytes) throw new InvalidOperationException("The blob exceeds Maximum Download MB.");
                        output.Write(buffer, 0, count);
                    }
                    if (output.Length != properties.ContentLength) throw new InvalidOperationException("The blob download was incomplete.");
                    return new BlobDownloadResponse
                    {
                        Base64 = Convert.ToBase64String(output.ToArray()),
                        MetadataJson = JsonConvert.SerializeObject(new { name = request.FileName, size = output.Length, etag = properties.ETag.ToString(),
                            contentType = properties.ContentType, versionId = properties.VersionId, lastModifiedUtc = properties.LastModified.UtcDateTime.ToString("o") })
                    };
                }
            }
            catch (RequestFailedException exception)
            {
                string reason = exception.Status == 404 ? "Blob or container was not found." : exception.Status == 403 ? "Read access was denied. Check the blob read permission and credential expiry."
                    : exception.Status == 412 ? "The blob changed or Expected ETag did not match." : "Azure Blob download failed.";
                // SDK error bodies may contain signed URLs or resource details.
                throw new InvalidOperationException(reason + " Azure HTTP status: " + exception.Status + ".");
            }
        }
    }
}
