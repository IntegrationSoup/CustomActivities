using Popokey.ExtensionRunners;
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using WinSCP;

namespace SftpActivities.Runner
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            SftpCommandResponse response = null;

            try
            {
                int? serverExitCode = PersistentRunnerServer.RunIfRequested(args, HandleServerRequest);
                if (serverExitCode.HasValue)
                {
                    return serverExitCode.Value;
                }

                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: SftpActivitiesRunner <requestPath> <responsePath>");
                    return 2;
                }

                string requestPath = Path.GetFullPath(args[0]);
                string responsePath = Path.GetFullPath(args[1]);
                if (!File.Exists(requestPath))
                {
                    Console.Error.WriteLine($"Request file was not found: {requestPath}");
                    return 3;
                }

                SftpCommandRequest request = DeserializeJson<SftpCommandRequest>(requestPath);
                response = ExecuteFileRequest(request);
                WriteResponse(responsePath, response);
                return response.Success ? 0 : 1;
            }
            catch (Exception ex)
            {
                response = response ?? new SftpCommandResponse
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
            SftpPipeRequest request = PersistentRunnerJson.Deserialize<SftpPipeRequest>(payloadJson);
            SftpPipeResponse response = ExecutePipeRequest(request);
            return PersistentRunnerJson.Serialize(response);
        }

        private static SftpCommandResponse ExecuteFileRequest(SftpCommandRequest request)
        {
            ValidateFileRequest(request);

            SftpPipeRequest pipeRequest = new SftpPipeRequest
            {
                Operation = request.Operation,
                HostName = request.HostName,
                Port = request.Port,
                UserName = request.UserName,
                Password = request.Password,
                PrivateKeyPath = request.PrivateKeyPath,
                PrivateKeyPassphrase = request.PrivateKeyPassphrase,
                SshHostKeyFingerprint = request.SshHostKeyFingerprint,
                RemotePath = request.RemotePath,
                CreateRemoteDirectory = request.CreateRemoteDirectory,
                DeleteRemoteFileAfterDownload = request.DeleteRemoteFileAfterDownload,
                InputBase64 = !string.IsNullOrWhiteSpace(request.LocalInputPath) ? Convert.ToBase64String(File.ReadAllBytes(request.LocalInputPath)) : null,
                IncludeOutputBase64 = false
            };

            SftpPipeResponse pipeResponse = ExecutePipeRequest(pipeRequest);
            if (string.Equals(request.Operation, "download", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.LocalOutputPath))
                {
                    throw new InvalidOperationException("LocalOutputPath is required for download operations.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(request.LocalOutputPath) ?? AppDomain.CurrentDomain.BaseDirectory);
                File.WriteAllBytes(request.LocalOutputPath, Convert.FromBase64String(pipeResponse.OutputBase64 ?? string.Empty));
            }

            return new SftpCommandResponse
            {
                Success = true,
                Message = pipeResponse.Message,
                RemotePath = pipeResponse.RemotePath,
                BytesTransferred = pipeResponse.BytesTransferred
            };
        }

        private static SftpPipeResponse ExecutePipeRequest(SftpPipeRequest request)
        {
            ValidatePipeRequest(request);

            using (Session session = new Session())
            {
                session.ExecutablePath = ResolveWinScpExecutablePath();
                session.Open(BuildSessionOptions(request));

                switch ((request.Operation ?? string.Empty).Trim().ToLowerInvariant())
                {
                    case "upload":
                        return Upload(session, request);
                    case "download":
                        return Download(session, request);
                    default:
                        throw new InvalidOperationException($"Unsupported SFTP operation '{request.Operation}'.");
                }
            }
        }

        private static SftpPipeResponse Upload(Session session, SftpPipeRequest request)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "SftpActivities.Runner", Guid.NewGuid().ToString("N"));
            string localInputPath = Path.Combine(tempRoot, "upload.bin");

            Directory.CreateDirectory(tempRoot);
            try
            {
                byte[] inputBytes = Convert.FromBase64String(request.InputBase64 ?? string.Empty);
                File.WriteAllBytes(localInputPath, inputBytes);

                if (request.CreateRemoteDirectory)
                {
                    EnsureRemoteDirectoryExists(session, request.RemotePath);
                }

                TransferOptions options = CreateTransferOptions();
                TransferOperationResult result = session.PutFiles(localInputPath, request.RemotePath, remove: false, options);
                result.Check();

                return new SftpPipeResponse
                {
                    Message = $"Uploaded {request.RemotePath}",
                    RemotePath = request.RemotePath,
                    BytesTransferred = inputBytes.LongLength
                };
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static SftpPipeResponse Download(Session session, SftpPipeRequest request)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "SftpActivities.Runner", Guid.NewGuid().ToString("N"));
            string localOutputPath = Path.Combine(tempRoot, "download.bin");

            Directory.CreateDirectory(tempRoot);
            try
            {
                TransferOptions options = CreateTransferOptions();
                TransferOperationResult result = session.GetFiles(
                    request.RemotePath,
                    localOutputPath,
                    request.DeleteRemoteFileAfterDownload,
                    options);
                result.Check();

                if (!File.Exists(localOutputPath))
                {
                    throw new InvalidOperationException($"The remote file was downloaded successfully but '{localOutputPath}' was not created.");
                }

                byte[] outputBytes = File.ReadAllBytes(localOutputPath);
                return new SftpPipeResponse
                {
                    Message = $"Downloaded {request.RemotePath}",
                    RemotePath = request.RemotePath,
                    BytesTransferred = outputBytes.LongLength,
                    OutputBase64 = request.IncludeOutputBase64 ? Convert.ToBase64String(outputBytes) : string.Empty
                };
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static SessionOptions BuildSessionOptions(SftpPipeRequest request)
        {
            SessionOptions options = new SessionOptions
            {
                Protocol = Protocol.Sftp,
                HostName = request.HostName,
                PortNumber = request.Port > 0 ? request.Port : 22,
                UserName = request.UserName,
                Password = request.Password,
                SshHostKeyFingerprint = request.SshHostKeyFingerprint
            };

            if (!string.IsNullOrWhiteSpace(request.PrivateKeyPath))
            {
                options.SshPrivateKeyPath = request.PrivateKeyPath;
            }

            if (!string.IsNullOrWhiteSpace(request.PrivateKeyPassphrase))
            {
                options.PrivateKeyPassphrase = request.PrivateKeyPassphrase;
            }

            return options;
        }

        private static TransferOptions CreateTransferOptions()
        {
            return new TransferOptions
            {
                TransferMode = TransferMode.Binary
            };
        }

        private static void EnsureRemoteDirectoryExists(Session session, string remotePath)
        {
            string remoteDirectory = GetRemoteDirectory(remotePath);
            if (string.IsNullOrWhiteSpace(remoteDirectory) || remoteDirectory == "/")
            {
                return;
            }

            if (!session.FileExists(remoteDirectory))
            {
                session.CreateDirectory(remoteDirectory);
            }
        }

        private static string GetRemoteDirectory(string remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                return string.Empty;
            }

            string normalizedPath = remotePath.Replace('\\', '/').Trim();
            int separatorIndex = normalizedPath.LastIndexOf('/');
            return separatorIndex <= 0 ? string.Empty : normalizedPath.Substring(0, separatorIndex);
        }

        private static void ValidateFileRequest(SftpCommandRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.Equals(request.Operation, "upload", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.LocalInputPath) || !File.Exists(request.LocalInputPath))
                {
                    throw new FileNotFoundException("LocalInputPath is required for upload operations.", request?.LocalInputPath);
                }
            }
            else if (string.Equals(request.Operation, "download", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.LocalOutputPath))
                {
                    throw new InvalidOperationException("LocalOutputPath is required for download operations.");
                }
            }

            ValidateSharedRequestFields(request.HostName, request.UserName, request.SshHostKeyFingerprint, request.RemotePath, request.Password, request.PrivateKeyPath);
        }

        private static void ValidatePipeRequest(SftpPipeRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            ValidateSharedRequestFields(request.HostName, request.UserName, request.SshHostKeyFingerprint, request.RemotePath, request.Password, request.PrivateKeyPath);

            if (string.Equals(request.Operation, "upload", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.InputBase64))
                {
                    throw new InvalidOperationException("InputBase64 is required for upload operations.");
                }
            }
        }

        private static void ValidateSharedRequestFields(string hostName, string userName, string hostKeyFingerprint, string remotePath, string password, string privateKeyPath)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                throw new InvalidOperationException("Host Name is required.");
            }

            if (string.IsNullOrWhiteSpace(userName))
            {
                throw new InvalidOperationException("User Name is required.");
            }

            if (string.IsNullOrWhiteSpace(hostKeyFingerprint))
            {
                throw new InvalidOperationException("SSH Host Key Fingerprint is required.");
            }

            if (string.IsNullOrWhiteSpace(remotePath))
            {
                throw new InvalidOperationException("Remote Path is required.");
            }

            if (string.IsNullOrWhiteSpace(password) && string.IsNullOrWhiteSpace(privateKeyPath))
            {
                throw new InvalidOperationException("Provide either a Password or a Private Key Path.");
            }
        }

        private static string ResolveWinScpExecutablePath()
        {
            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string[] candidatePaths =
            {
                Path.Combine(applicationDirectory, "WinSCP.exe"),
                Path.Combine(applicationDirectory, "winscp.exe")
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new FileNotFoundException(
                "WinSCP.exe could not be found beside the SFTP runner executable. Ensure the helper folder includes WinSCP.exe.");
        }

        private static void TryDeleteDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        private static void WriteResponse(string responsePath, SftpCommandResponse response)
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
        private sealed class SftpCommandRequest
        {
            [DataMember(Order = 1)]
            public string Operation { get; set; }

            [DataMember(Order = 2)]
            public string HostName { get; set; }

            [DataMember(Order = 3)]
            public int Port { get; set; }

            [DataMember(Order = 4)]
            public string UserName { get; set; }

            [DataMember(Order = 5)]
            public string Password { get; set; }

            [DataMember(Order = 6)]
            public string PrivateKeyPath { get; set; }

            [DataMember(Order = 7)]
            public string PrivateKeyPassphrase { get; set; }

            [DataMember(Order = 8)]
            public string SshHostKeyFingerprint { get; set; }

            [DataMember(Order = 9)]
            public string RemotePath { get; set; }

            [DataMember(Order = 10)]
            public bool CreateRemoteDirectory { get; set; }

            [DataMember(Order = 11)]
            public bool DeleteRemoteFileAfterDownload { get; set; }

            [DataMember(Order = 12)]
            public string LocalInputPath { get; set; }

            [DataMember(Order = 13)]
            public string LocalOutputPath { get; set; }
        }

        [DataContract]
        private sealed class SftpCommandResponse
        {
            [DataMember(Order = 1)]
            public bool Success { get; set; }

            [DataMember(Order = 2)]
            public string Message { get; set; }

            [DataMember(Order = 3)]
            public string RemotePath { get; set; }

            [DataMember(Order = 4)]
            public long BytesTransferred { get; set; }
        }

        [DataContract]
        private sealed class SftpPipeRequest
        {
            [DataMember(Order = 1)]
            public string Operation { get; set; }

            [DataMember(Order = 2)]
            public string HostName { get; set; }

            [DataMember(Order = 3)]
            public int Port { get; set; }

            [DataMember(Order = 4)]
            public string UserName { get; set; }

            [DataMember(Order = 5)]
            public string Password { get; set; }

            [DataMember(Order = 6)]
            public string PrivateKeyPath { get; set; }

            [DataMember(Order = 7)]
            public string PrivateKeyPassphrase { get; set; }

            [DataMember(Order = 8)]
            public string SshHostKeyFingerprint { get; set; }

            [DataMember(Order = 9)]
            public string RemotePath { get; set; }

            [DataMember(Order = 10)]
            public bool CreateRemoteDirectory { get; set; }

            [DataMember(Order = 11)]
            public bool DeleteRemoteFileAfterDownload { get; set; }

            [DataMember(Order = 12)]
            public string InputBase64 { get; set; }

            [DataMember(Order = 13)]
            public bool IncludeOutputBase64 { get; set; }
        }

        [DataContract]
        private sealed class SftpPipeResponse
        {
            [DataMember(Order = 1)]
            public string Message { get; set; }

            [DataMember(Order = 2)]
            public string RemotePath { get; set; }

            [DataMember(Order = 3)]
            public long BytesTransferred { get; set; }

            [DataMember(Order = 4)]
            public string OutputBase64 { get; set; }
        }
    }
}
