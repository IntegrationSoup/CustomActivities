using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AmazonActivities.Runner
{
    internal static class Program
    {
        private static readonly Dictionary<string, IAmazonS3> s3ClientCache = new Dictionary<string, IAmazonS3>(StringComparer.Ordinal);

        private static int Main(string[] args)
        {
            S3UploadResponse response = null;

            try
            {
                int? serverExitCode = PersistentRunnerServer.RunIfRequested(args, HandleServerRequest);
                if (serverExitCode.HasValue)
                {
                    return serverExitCode.Value;
                }

                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: AwsActivitiesRunner <requestPath> <responsePath>");
                    return 2;
                }

                string requestPath = Path.GetFullPath(args[0]);
                string responsePath = Path.GetFullPath(args[1]);
                if (!File.Exists(requestPath))
                {
                    Console.Error.WriteLine($"Request file was not found: {requestPath}");
                    return 3;
                }

                S3UploadRequest request = DeserializeJson<S3UploadRequest>(requestPath);
                response = ExecuteFileRequest(request);
                WriteResponse(responsePath, response);
                return response.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                response = response ?? new S3UploadResponse
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
            if (!string.Equals(operation, "upload-s3", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported AWS runner operation '{operation}'.");
            }

            S3UploadPipeRequest request = PersistentRunnerJson.Deserialize<S3UploadPipeRequest>(payloadJson);
            S3UploadPipeResponse response = ExecutePipeRequest(request);
            return PersistentRunnerJson.Serialize(response);
        }

        private static S3UploadResponse ExecuteFileRequest(S3UploadRequest request)
        {
            ValidateFileRequest(request);

            S3UploadPipeRequest pipeRequest = new S3UploadPipeRequest
            {
                BucketName = request.BucketName,
                FileName = request.FileName,
                Region = request.Region,
                AccessKeyId = request.AccessKeyId,
                SecretAccessKey = request.SecretAccessKey,
                InputBase64 = Convert.ToBase64String(File.ReadAllBytes(request.LocalInputPath))
            };

            S3UploadPipeResponse pipeResponse = ExecutePipeRequest(pipeRequest);
            return new S3UploadResponse
            {
                Success = true,
                Message = pipeResponse.Message,
                ObjectPath = pipeResponse.ObjectPath
            };
        }

        private static S3UploadPipeResponse ExecutePipeRequest(S3UploadPipeRequest request)
        {
            ValidatePipeRequest(request);

            IAmazonS3 s3Client = GetS3Client(request.Region, request.AccessKeyId, request.SecretAccessKey);
            byte[] inputBytes = Convert.FromBase64String(request.InputBase64 ?? string.Empty);

            using (MemoryStream stream = new MemoryStream(inputBytes, writable: false))
            {
                TransferUtility fileTransferUtility = new TransferUtility(s3Client);
                fileTransferUtility.Upload(stream, request.BucketName, request.FileName);

                return new S3UploadPipeResponse
                {
                    Message = "Code Execute Successfully",
                    ObjectPath = request.BucketName + "/" + request.FileName
                };
            }
        }

        private static IAmazonS3 GetS3Client(string region, string accessKeyId, string secretAccessKey)
        {
            string cacheKey = region + "\n" + accessKeyId + "\n" + secretAccessKey;
            lock (s3ClientCache)
            {
                if (!s3ClientCache.TryGetValue(cacheKey, out IAmazonS3 client))
                {
                    RegionEndpoint bucketRegion = RegionEndpoint.GetBySystemName(region);
                    var credentials = new Amazon.Runtime.BasicAWSCredentials(accessKeyId, secretAccessKey);
                    client = new AmazonS3Client(credentials, bucketRegion);
                    s3ClientCache[cacheKey] = client;
                }

                return client;
            }
        }

        private static void ValidateFileRequest(S3UploadRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.BucketName))
            {
                throw new InvalidOperationException("Bucket Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                throw new InvalidOperationException("File Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Region))
            {
                throw new InvalidOperationException("Region is required.");
            }

            if (string.IsNullOrWhiteSpace(request.AccessKeyId))
            {
                throw new InvalidOperationException("Access Key ID is required.");
            }

            if (string.IsNullOrWhiteSpace(request.SecretAccessKey))
            {
                throw new InvalidOperationException("Secret Access Key is required.");
            }

            if (string.IsNullOrWhiteSpace(request.LocalInputPath) || !File.Exists(request.LocalInputPath))
            {
                throw new FileNotFoundException("The local input file could not be found.", request?.LocalInputPath);
            }
        }

        private static void ValidatePipeRequest(S3UploadPipeRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.BucketName))
            {
                throw new InvalidOperationException("Bucket Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.FileName))
            {
                throw new InvalidOperationException("File Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Region))
            {
                throw new InvalidOperationException("Region is required.");
            }

            if (string.IsNullOrWhiteSpace(request.AccessKeyId))
            {
                throw new InvalidOperationException("Access Key ID is required.");
            }

            if (string.IsNullOrWhiteSpace(request.SecretAccessKey))
            {
                throw new InvalidOperationException("Secret Access Key is required.");
            }
        }

        private static void WriteResponse(string responsePath, S3UploadResponse response)
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
        private sealed class S3UploadRequest
        {
            [DataMember(Order = 1)]
            public string BucketName { get; set; }

            [DataMember(Order = 2)]
            public string FileName { get; set; }

            [DataMember(Order = 3)]
            public string Region { get; set; }

            [DataMember(Order = 4)]
            public string AccessKeyId { get; set; }

            [DataMember(Order = 5)]
            public string SecretAccessKey { get; set; }

            [DataMember(Order = 6)]
            public string LocalInputPath { get; set; }
        }

        [DataContract]
        private sealed class S3UploadResponse
        {
            [DataMember(Order = 1)]
            public bool Success { get; set; }

            [DataMember(Order = 2)]
            public string Message { get; set; }

            [DataMember(Order = 3)]
            public string ObjectPath { get; set; }
        }

        [DataContract]
        private sealed class S3UploadPipeRequest
        {
            [DataMember(Order = 1)]
            public string BucketName { get; set; }

            [DataMember(Order = 2)]
            public string FileName { get; set; }

            [DataMember(Order = 3)]
            public string Region { get; set; }

            [DataMember(Order = 4)]
            public string AccessKeyId { get; set; }

            [DataMember(Order = 5)]
            public string SecretAccessKey { get; set; }

            [DataMember(Order = 6)]
            public string InputBase64 { get; set; }
        }

        [DataContract]
        private sealed class S3UploadPipeResponse
        {
            [DataMember(Order = 1)]
            public string Message { get; set; }

            [DataMember(Order = 2)]
            public string ObjectPath { get; set; }
        }
    }
}
