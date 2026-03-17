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
                response = Execute(request);
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

        private static SftpCommandResponse Execute(SftpCommandRequest request)
        {
            ValidateRequest(request);

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

        private static SftpCommandResponse Upload(Session session, SftpCommandRequest request)
        {
            if (!File.Exists(request.LocalInputPath))
            {
                throw new FileNotFoundException($"Upload source file was not found: {request.LocalInputPath}", request.LocalInputPath);
            }

            if (request.CreateRemoteDirectory)
            {
                EnsureRemoteDirectoryExists(session, request.RemotePath);
            }

            TransferOptions options = CreateTransferOptions();
            TransferOperationResult result = session.PutFiles(request.LocalInputPath, request.RemotePath, remove: false, options);
            result.Check();

            long bytesTransferred = new FileInfo(request.LocalInputPath).Length;
            return new SftpCommandResponse
            {
                Success = true,
                Message = $"Uploaded {request.RemotePath}",
                RemotePath = request.RemotePath,
                BytesTransferred = bytesTransferred
            };
        }

        private static SftpCommandResponse Download(Session session, SftpCommandRequest request)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(request.LocalOutputPath) ?? AppDomain.CurrentDomain.BaseDirectory);

            TransferOptions options = CreateTransferOptions();
            TransferOperationResult result = session.GetFiles(
                request.RemotePath,
                request.LocalOutputPath,
                request.DeleteRemoteFileAfterDownload,
                options);
            result.Check();

            if (!File.Exists(request.LocalOutputPath))
            {
                throw new InvalidOperationException($"The remote file was downloaded successfully but '{request.LocalOutputPath}' was not created.");
            }

            long bytesTransferred = new FileInfo(request.LocalOutputPath).Length;
            return new SftpCommandResponse
            {
                Success = true,
                Message = $"Downloaded {request.RemotePath}",
                RemotePath = request.RemotePath,
                BytesTransferred = bytesTransferred
            };
        }

        private static SessionOptions BuildSessionOptions(SftpCommandRequest request)
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

        private static void ValidateRequest(SftpCommandRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.HostName))
            {
                throw new InvalidOperationException("Host Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.UserName))
            {
                throw new InvalidOperationException("User Name is required.");
            }

            if (string.IsNullOrWhiteSpace(request.SshHostKeyFingerprint))
            {
                throw new InvalidOperationException("SSH Host Key Fingerprint is required.");
            }

            if (string.IsNullOrWhiteSpace(request.RemotePath))
            {
                throw new InvalidOperationException("Remote Path is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Password) && string.IsNullOrWhiteSpace(request.PrivateKeyPath))
            {
                throw new InvalidOperationException("Provide either a Password or a Private Key Path.");
            }

            if (string.Equals(request.Operation, "upload", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.LocalInputPath))
                {
                    throw new InvalidOperationException("LocalInputPath is required for upload operations.");
                }
            }
            else if (string.Equals(request.Operation, "download", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(request.LocalOutputPath))
                {
                    throw new InvalidOperationException("LocalOutputPath is required for download operations.");
                }
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
    }
}
