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
        private static readonly Dictionary<string, BlobContainerClient> containerClientCache = new Dictionary<string, BlobContainerClient>(StringComparer.Ordinal);

        private static int Main(string[] args)
        {
            BlobUploadResponse response = null;

            try
            {
                int? serverExitCode = PersistentRunnerServer.RunIfRequested(args, HandleServerRequest);
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
                InputBase64 = Convert.ToBase64String(File.ReadAllBytes(request.LocalInputPath))
            };

            BlobUploadPipeResponse pipeResponse = ExecutePipeRequest(pipeRequest);
            return new BlobUploadResponse
            {
                Success = true,
                Message = pipeResponse.Message,
                BlobPath = pipeResponse.BlobPath
            };
        }

        private static BlobUploadPipeResponse ExecutePipeRequest(BlobUploadPipeRequest request)
        {
            ValidatePipeRequest(request);

            string containerLower = request.ContainerName.ToLowerInvariant();
            BlobContainerClient container = GetContainerClient(request.ConnectionString, containerLower);
            byte[] inputBytes = Convert.FromBase64String(request.InputBase64 ?? string.Empty);

            using (MemoryStream stream = new MemoryStream(inputBytes, writable: false))
            {
                var createResponse = container.CreateIfNotExists();
                if (createResponse != null && createResponse.GetRawResponse().Status == 201)
                {
                    container.SetAccessPolicy(Azure.Storage.Blobs.Models.PublicAccessType.Blob);
                }

                BlobClient blob = container.GetBlobClient(request.FileName);
                blob.DeleteIfExists(Azure.Storage.Blobs.Models.DeleteSnapshotsOption.IncludeSnapshots);
                blob.Upload(stream);

                return new BlobUploadPipeResponse
                {
                    Message = "Code Executed Successfully",
                    BlobPath = containerLower + "/" + request.FileName
                };
            }
        }

        private static BlobContainerClient GetContainerClient(string connectionString, string containerName)
        {
            string cacheKey = connectionString + "\n" + containerName;
            lock (containerClientCache)
            {
                if (!containerClientCache.TryGetValue(cacheKey, out BlobContainerClient client))
                {
                    client = new BlobContainerClient(connectionString, containerName);
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

            if (string.IsNullOrWhiteSpace(request.FileName))
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

            if (string.IsNullOrWhiteSpace(request.FileName))
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
            [DataMember(Order = 1)]
            public string Message { get; set; }

            [DataMember(Order = 2)]
            public string BlobPath { get; set; }
        }
    }
}
