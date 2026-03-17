using Amazon;
using Amazon.S3;
using Amazon.S3.Transfer;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace AmazonActivities.Runner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            S3UploadResponse response = null;

            try
            {
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
                response = Execute(request);
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

        private static S3UploadResponse Execute(S3UploadRequest request)
        {
            ValidateRequest(request);

            RegionEndpoint bucketRegion = RegionEndpoint.GetBySystemName(request.Region);
            var credentials = new Amazon.Runtime.BasicAWSCredentials(request.AccessKeyId, request.SecretAccessKey);
            using (IAmazonS3 s3Client = new AmazonS3Client(credentials, bucketRegion))
            using (FileStream stream = File.OpenRead(request.LocalInputPath))
            {
                TransferUtility fileTransferUtility = new TransferUtility(s3Client);
                fileTransferUtility.Upload(stream, request.BucketName, request.FileName);

                return new S3UploadResponse
                {
                    Success = true,
                    Message = "Code Execute Successfully",
                    ObjectPath = request.BucketName + "/" + request.FileName
                };
            }
        }

        private static void ValidateRequest(S3UploadRequest request)
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
    }
}
