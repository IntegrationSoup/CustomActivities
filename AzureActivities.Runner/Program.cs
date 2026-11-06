using Azure.Storage.Blobs;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AzureActivities.Runner
{
    internal static class Program
    {
        internal static Func<string, string, BlobContainerClient> ContainerClientFactory { get; set; }
        private static readonly Dictionary<string, BlobContainerClient> containerClientCache = new Dictionary<string, BlobContainerClient>(StringComparer.Ordinal);

        private static int Main(string[] args)
        {
            BlobUploadResponse response = null;

            try
            {
                int? serverExitCode = BridgeRunner.RunIfRequested(args, HandleServerRequest, () => new BridgeProvider("popokey.azureactivities", "AzureActivities", HandleServerRequest, typeof(AzureActivities.BlobSender), typeof(AzureActivities.BlobDownload)));
                if (serverExitCode.HasValue)
                {
                    return serverExitCode.Value;
                }

                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: AzureActivitiesRunner <requestPath> <responsePath>");
                    return 2;
                }

                string requestPath = Path.GetFullPath(args[0]);
                string responsePath = Path.GetFullPath(args[1]);
                if (!File.Exists(requestPath))
                {
                    Console.Error.WriteLine($"Request file was not found: {requestPath}");
                    return 3;
                }

                BlobUploadRequest request = DeserializeJson<BlobUploadRequest>(requestPath);
                response = ExecuteFileRequest(request);
                WriteResponse(responsePath, response);
                return response.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                response = response ?? new BlobUploadResponse
                {
                    Success = false,
                    Message = ex.Message
                };

                if (args.Length >= 2)
                {
                    try
                    {
                        WriteResponse(Path.GetFullPath(args[1]), response);
                    }
                    catch
                    {
                        // Best-effort only.
                    }
                }

                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static string HandleServerRequest(string operation, string payloadJson)
        {
            if (string.Equals(operation, "download-blob", StringComparison.OrdinalIgnoreCase))
                return PersistentRunnerJson.Serialize(BlobDownloadOperation.Execute(PersistentRunnerJson.Deserialize<AzureActivities.BlobDownloadRequest>(payloadJson)));
            if (!string.Equals(operation, "upload-blob", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported Azure runner operation '{operation}'.");
            }

            BlobUploadPipeRequest request = PersistentRunnerJson.Deserialize<BlobUploadPipeRequest>(payloadJson);
            BlobUploadPipeResponse response = ExecutePipeRequest(request);
            return PersistentRunnerJson.Serialize(response);
        }

        private static BlobUploadResponse ExecuteFileRequest(BlobUploadRequest request)
        {
            ValidateFileRequest(request);

            BlobUploadPipeRequest pipeRequest = new BlobUploadPipeRequest
            {
                ConnectionString = request.ConnectionString,
                ContainerName = request.ContainerName,
                FileName = request.FileName,
                InputBase64 = Convert.ToBase64String(File.ReadAllBytes(request.LocalInputPath)),
                NewContainerAccess = request.NewContainerAccess,
                CreateContainerIfMissing = request.CreateContainerIfMissing,
                OverwriteExistingBlob = request.OverwriteExistingBlob,
                ExpectedETag = request.ExpectedETag
            };

            BlobUploadPipeResponse pipeResponse = ExecutePipeRequest(pipeRequest);
            return new BlobUploadResponse
            {
                Success = true,
                Message = pipeResponse.Message,
                BlobPath = pipeResponse.BlobPath,
                ETag = pipeResponse.ETag,
                VersionId = pipeResponse.VersionId
            };
        }

        private static BlobUploadPipeResponse ExecutePipeRequest(BlobUploadPipeRequest request)
        {
            ValidatePipeRequest(request);
            bool createContainer = false;
            if (!string.IsNullOrWhiteSpace(request.CreateContainerIfMissing) && !bool.TryParse(request.CreateContainerIfMissing.Trim(), out createContainer))
                throw new ArgumentException("Create Container If Missing must be False or True.");
            bool overwrite = false;
            if (!string.IsNullOrWhiteSpace(request.OverwriteExistingBlob) && !bool.TryParse(request.OverwriteExistingBlob.Trim(), out overwrite))
                throw new ArgumentException("Overwrite Existing Blob must be False or True.");
            if (!overwrite && !string.IsNullOrWhiteSpace(request.ExpectedETag))
                throw new ArgumentException("Expected ETag requires Overwrite Existing Blob=True.");
            var access = Azure.Storage.Blobs.Models.PublicAccessType.None;
            if (!string.IsNullOrWhiteSpace(request.NewContainerAccess) && !string.Equals(request.NewContainerAccess.Trim(), "Private", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(request.NewContainerAccess.Trim(), "Blob", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("New Container Access must be Private or Blob.");
                access = Azure.Storage.Blobs.Models.PublicAccessType.Blob;
            }
            if (!createContainer && access != Azure.Storage.Blobs.Models.PublicAccessType.None)
                throw new ArgumentException("Public new-container access requires Create Container If Missing=True.");

            string containerLower = request.ContainerName.ToLowerInvariant();
            BlobContainerClient container = GetContainerClient(request.ConnectionString, containerLower);
            byte[] inputBytes = Convert.FromBase64String(request.InputBase64 ?? string.Empty);
            if (inputBytes.Length > 8 * 1024 * 1024) throw new InvalidOperationException("Azure Blob Upload supports at most 8 MiB per invocation.");

            using (MemoryStream stream = new MemoryStream(inputBytes, writable: false))
            {
                // Existing containers retain their permissions; do not call SetAccessPolicy.
                if (createContainer) container.CreateIfNotExists(access);

                BlobClient blob = container.GetBlobClient(request.FileName);
                var conditions = !overwrite ? new Azure.Storage.Blobs.Models.BlobRequestConditions { IfNoneMatch = Azure.ETag.All }
                    : string.IsNullOrWhiteSpace(request.ExpectedETag) ? null : new Azure.Storage.Blobs.Models.BlobRequestConditions { IfMatch = new Azure.ETag(request.ExpectedETag) };
                // Known-length, bounded messages use one PUT, never OpenWrite or a pre-delete.
                var uploaded = blob.Upload(stream, new Azure.Storage.Blobs.Models.BlobUploadOptions { Conditions = conditions,
                    TransferOptions = new Azure.Storage.StorageTransferOptions { InitialTransferSize = 16 * 1024 * 1024, MaximumTransferSize = 16 * 1024 * 1024, MaximumConcurrency = 1 } }).Value;

                return new BlobUploadPipeResponse
                {
                    Message = "Code Executed Successfully",
                    BlobPath = containerLower + "/" + request.FileName,
                    ETag = uploaded.ETag.ToString(),
                    VersionId = uploaded.VersionId
                };
            }
        }

        private static BlobContainerClient GetContainerClient(string connectionString, string containerName)
        {
            if (ContainerClientFactory != null) return ContainerClientFactory(connectionString, containerName);
            string cacheKey = connectionString + "\n" + containerName;
            lock (containerClientCache)
            {
                if (!containerClientCache.TryGetValue(cacheKey, out BlobContainerClient client))
                {
                    var options = new BlobClientOptions();
                    options.Retry.MaxRetries = 0;
                    options.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
                    client = new BlobContainerClient(connectionString, containerName, options);
                    containerClientCache[cacheKey] = client;
                }

                return client;
            }
        }

        private static void ValidateFileRequest(BlobUploadRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ConnectionString))
            {
                throw new InvalidOperationException("Connection String is required.");
            }

            if (string.IsNullOrWhiteSpace(request.ContainerName))
            {
                throw new InvalidOperationException("Container Name is required.");
            }

            if (string.IsNullOrEmpty(request.FileName))
            {
                throw new InvalidOperationException("File Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.LocalInputPath) || !File.Exists(request.LocalInputPath))
            {
                throw new FileNotFoundException("The local input file could not be found.", request?.LocalInputPath);
            }
        }

        private static void ValidatePipeRequest(BlobUploadPipeRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.ConnectionString))
            {
                throw new InvalidOperationException("Connection String is required.");
            }

            if (string.IsNullOrWhiteSpace(request.ContainerName))
            {
                throw new InvalidOperationException("Container Name is required.");
            }

            if (string.IsNullOrEmpty(request.FileName))
            {
                throw new InvalidOperationException("File Name is required.");
            }
        }

        private static void WriteResponse(string responsePath, BlobUploadResponse response)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(responsePath) ?? AppDomain.CurrentDomain.BaseDirectory);
            SerializeJson(responsePath, response);
        }

        private static void SerializeJson<T>(string path, T value)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = File.Create(path))
            {
                serializer.WriteObject(stream, value);
            }
        }

        private static T DeserializeJson<T>(string path)
        {
            DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
            using (FileStream stream = File.OpenRead(path))
            {
                return (T)serializer.ReadObject(stream);
            }
        }

        [DataContract]
        private sealed class BlobUploadRequest
        {
            [DataMember(Order = 5)]
            public string NewContainerAccess { get; set; }
            [DataMember(Order = 6)]
            public string OverwriteExistingBlob { get; set; }
            [DataMember(Order = 7)]
            public string ExpectedETag { get; set; }
            [DataMember(Order = 8)]
            public string CreateContainerIfMissing { get; set; }
            [DataMember(Order = 1)]
            public string ConnectionString { get; set; }

            [DataMember(Order = 2)]
            public string ContainerName { get; set; }

            [DataMember(Order = 3)]
            public string FileName { get; set; }

            [DataMember(Order = 4)]
            public string LocalInputPath { get; set; }
        }

        [DataContract]
        private sealed class BlobUploadResponse
        {
            [DataMember(Order = 5)] public string ETag { get; set; }
            [DataMember(Order = 6)] public string VersionId { get; set; }
            [DataMember(Order = 1)]
            public bool Success { get; set; }

            [DataMember(Order = 2)]
            public string Message { get; set; }

            [DataMember(Order = 3)]
            public string BlobPath { get; set; }
        }

        [DataContract]
        private sealed class BlobUploadPipeRequest
        {
            [DataMember(Order = 5)]
            public string NewContainerAccess { get; set; }
            [DataMember(Order = 6)]
            public string OverwriteExistingBlob { get; set; }
            [DataMember(Order = 7)]
            public string ExpectedETag { get; set; }
            [DataMember(Order = 8)]
            public string CreateContainerIfMissing { get; set; }
            [DataMember(Order = 1)]
            public string ConnectionString { get; set; }

            [DataMember(Order = 2)]
            public string ContainerName { get; set; }

            [DataMember(Order = 3)]
            public string FileName { get; set; }

            [DataMember(Order = 4)]
            public string InputBase64 { get; set; }
        }

        [DataContract]
        private sealed class BlobUploadPipeResponse
        {
            [DataMember(Order = 3)] public string ETag { get; set; }
            [DataMember(Order = 4)] public string VersionId { get; set; }
            [DataMember(Order = 1)]
            public string Message { get; set; }

            [DataMember(Order = 2)]
            public string BlobPath { get; set; }
        }
    }
}
