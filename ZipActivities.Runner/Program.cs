using Popokey.ExtensionRunners;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace ZipActivities.Runner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            ZipResponse response = null;

            try
            {
                int? serverExitCode = PersistentRunnerServer.RunIfRequested(args, HandleServerRequest);
                if (serverExitCode.HasValue)
                {
                    return serverExitCode.Value;
                }

                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: ZipActivitiesRunner <requestPath> <responsePath>");
                    return 2;
                }

                string requestPath = Path.GetFullPath(args[0]);
                string responsePath = Path.GetFullPath(args[1]);
                if (!File.Exists(requestPath))
                {
                    Console.Error.WriteLine($"Request file was not found: {requestPath}");
                    return 3;
                }

                ZipFileRequest request = DeserializeJson<ZipFileRequest>(requestPath);
                response = Execute(request.Operation, request);
                WriteResponse(responsePath, response);
                return 0;
            }
            catch (Exception ex)
            {
                response = response ?? new ZipResponse
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
            ZipRequest request = PersistentRunnerJson.Deserialize<ZipRequest>(payloadJson);
            ZipResponse response = Execute(operation, request);
            return PersistentRunnerJson.Serialize(response);
        }

        private static ZipResponse Execute(string operation, ZipRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            switch ((operation ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "create-file":
                case "create-zip-file":
                {
                    ZipOperationResult result = ZipArchiveOperations.CreateFile(
                        request.SourceDirectory,
                        request.ZipFilePath,
                        request.IncludeBaseDirectory,
                        request.OverwriteExistingFile);
                    return CreateResponse(
                        result,
                        $"Created ZIP file '{Path.GetFullPath(request.ZipFilePath)}' containing {DescribeContents(result)}.",
                        null);
                }
                case "create-message":
                case "create-zip-message":
                {
                    ZipBytesResult bytesResult = ZipArchiveOperations.CreateBytes(request.SourceDirectory, request.IncludeBaseDirectory);
                    return CreateResponse(
                        bytesResult.Result,
                        $"Created ZIP message containing {DescribeContents(bytesResult.Result)}.",
                        Convert.ToBase64String(bytesResult.Bytes));
                }
                case "extract":
                case "extract-zip-file":
                {
                    ZipOperationResult result = ZipArchiveOperations.Extract(
                        request.ZipFilePath,
                        request.DestinationDirectory,
                        request.OverwriteExistingFiles);
                    return CreateResponse(
                        result,
                        $"Extracted ZIP file '{Path.GetFullPath(request.ZipFilePath)}' to '{Path.GetFullPath(request.DestinationDirectory)}' ({DescribeContents(result)}).",
                        null);
                }
                default:
                    throw new InvalidOperationException($"Unsupported ZIP operation '{operation}'.");
            }
        }

        private static ZipResponse CreateResponse(ZipOperationResult result, string message, string outputBase64)
        {
            return new ZipResponse
            {
                Success = true,
                Message = message,
                FileCount = result.FileCount,
                DirectoryCount = result.DirectoryCount,
                ZipLength = result.ZipLength,
                OutputBase64 = outputBase64
            };
        }

        private static string DescribeContents(ZipOperationResult result)
        {
            return result.FileCount
                + (result.FileCount == 1 ? " file" : " files")
                + " and "
                + result.DirectoryCount
                + (result.DirectoryCount == 1 ? " directory" : " directories");
        }

        private static void WriteResponse(string responsePath, ZipResponse response)
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
        private class ZipRequest
        {
            [DataMember(Order = 1)]
            public string SourceDirectory { get; set; }

            [DataMember(Order = 2)]
            public string ZipFilePath { get; set; }

            [DataMember(Order = 3)]
            public string DestinationDirectory { get; set; }

            [DataMember(Order = 4)]
            public bool IncludeBaseDirectory { get; set; }

            [DataMember(Order = 5)]
            public bool OverwriteExistingFile { get; set; }

            [DataMember(Order = 6)]
            public bool OverwriteExistingFiles { get; set; }
        }

        [DataContract]
        private sealed class ZipFileRequest : ZipRequest
        {
            [DataMember(Order = 7)]
            public string Operation { get; set; }
        }

        [DataContract]
        private sealed class ZipResponse
        {
            [DataMember(Order = 1)]
            public bool Success { get; set; }

            [DataMember(Order = 2)]
            public string Message { get; set; }

            [DataMember(Order = 3)]
            public int FileCount { get; set; }

            [DataMember(Order = 4)]
            public int DirectoryCount { get; set; }

            [DataMember(Order = 5)]
            public long ZipLength { get; set; }

            [DataMember(Order = 6)]
            public string OutputBase64 { get; set; }
        }
    }
}
