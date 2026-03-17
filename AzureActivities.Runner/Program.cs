using Azure.Storage.Blobs;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AzureActivities.Runner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            BlobUploadResponse response = null;

            try
            {
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
                response = Execute(request);
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

        private static BlobUploadResponse Execute(BlobUploadRequest request)
        {
            ValidateRequest(request);

            string containerLower = request.ContainerName.ToLowerInvariant();
            using (FileStream stream = File.OpenRead(request.LocalInputPath))
            {
                BlobContainerClient container = new BlobContainerClient(request.ConnectionString, containerLower);
                var createResponse = container.CreateIfNotExists();
                if (createResponse != null && createResponse.GetRawResponse().Status == 201)
                {
                    container.SetAccessPolicy(Azure.Storage.Blobs.Models.PublicAccessType.Blob);
                }

                BlobClient blob = container.GetBlobClient(request.FileName);
                blob.DeleteIfExists(Azure.Storage.Blobs.Models.DeleteSnapshotsOption.IncludeSnapshots);
                blob.Upload(stream);

                return new BlobUploadResponse
                {
                    Success = true,
                    Message = "Code Executed Successfully",
                    BlobPath = containerLower + "/" + request.FileName
                };
            }
        }

        private static void ValidateRequest(BlobUploadRequest request)
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
    }
}
