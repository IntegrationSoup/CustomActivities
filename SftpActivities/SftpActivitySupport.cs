using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace SftpActivities
{
    public abstract class SftpActivityBase : CustomActivity
    {
        protected static SftpActivitySupport.SftpCommandRequest CreateRequest(Dictionary<string, string> parameters, string operation)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return new SftpActivitySupport.SftpCommandRequest
            {
                Operation = operation,
                HostName = GetRequiredParameter(parameters, "Host Name"),
                Port = GetOptionalInteger(parameters, "Port", 22),
                UserName = GetRequiredParameter(parameters, "User Name"),
                Password = GetOptionalParameter(parameters, "Password"),
                PrivateKeyPath = GetOptionalParameter(parameters, "Private Key Path"),
                PrivateKeyPassphrase = GetOptionalParameter(parameters, "Private Key Passphrase"),
                SshHostKeyFingerprint = GetRequiredParameter(parameters, "SSH Host Key Fingerprint"),
                RemotePath = GetRequiredParameter(parameters, "Remote Path")
            };
        }

        protected static bool GetOptionalBoolean(Dictionary<string, string> parameters, string name, bool defaultValue = false)
        {
            string value = GetOptionalParameter(parameters, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "y":
                case "on":
                    return true;
                case "0":
                case "false":
                case "no":
                case "n":
                case "off":
                    return false;
                default:
                    throw new ArgumentException($"Parameter '{name}' must be true or false.");
            }
        }

        protected static void EnsureActivityInstanceReady(IActivityInstance activityInstance, bool requireMessage = true)
        {
            if (activityInstance == null)
            {
                throw new ArgumentNullException(nameof(activityInstance));
            }

            if (requireMessage && activityInstance.Message == null)
            {
                throw new InvalidOperationException("Error: Activity Message not set.");
            }

            if (activityInstance.ResponseMessage == null)
            {
                throw new InvalidOperationException("Error: Response Message not set.");
            }
        }

        private static string GetRequiredParameter(Dictionary<string, string> parameters, string name)
        {
            string value = GetOptionalParameter(parameters, name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            throw new ArgumentException($"Parameter '{name}' is required.");
        }

        private static string GetOptionalParameter(Dictionary<string, string> parameters, string name)
        {
            return parameters.TryGetValue(name, out string value) ? value : null;
        }

        private static int GetOptionalInteger(Dictionary<string, string> parameters, string name, int defaultValue)
        {
            string value = GetOptionalParameter(parameters, name);
            if (string.IsNullOrWhiteSpace(value))
            {
                return defaultValue;
            }

            if (int.TryParse(value.Trim(), out int parsedValue) && parsedValue > 0)
            {
                return parsedValue;
            }

            throw new ArgumentException($"Parameter '{name}' must be a positive integer.");
        }
    }

    public static class SftpActivitySupport
    {
        private const string RunnerPathEnvironmentVariable = "SFTPACTIVITIES_RUNNER_PATH";
        private const string RunnerDirectoryName = "SftpActivitiesRunner";
        private const string RunnerExecutableName = "SftpActivitiesRunner.exe";
        private const int RunnerTimeoutMilliseconds = 180000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("SFTP runner");

        internal static SftpCommandResponse Upload(SftpCommandRequest request, byte[] inputBytes)
        {
            SftpPipeResponse response = Execute(
                "upload-sftp",
                request,
                inputBytes,
                includeOutputBytes: false);

            return CreateCommandResponse(response);
        }

        internal static SftpDownloadResult Download(SftpCommandRequest request)
        {
            SftpPipeResponse response = Execute(
                "download-sftp",
                request,
                inputBytes: null,
                includeOutputBytes: true);

            return new SftpDownloadResult(
                CreateCommandResponse(response),
                string.IsNullOrWhiteSpace(response.OutputBase64)
                    ? Array.Empty<byte>()
                    : Convert.FromBase64String(response.OutputBase64));
        }

        private static SftpPipeResponse Execute(string operation, SftpCommandRequest request, byte[] inputBytes, bool includeOutputBytes)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();

            try
            {
                return runnerClient.Invoke<SftpPipeRequest, SftpPipeResponse>(
                    runnerExecutablePath,
                    operation,
                    new SftpPipeRequest
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
                        InputBase64 = inputBytes != null ? Convert.ToBase64String(inputBytes) : null,
                        IncludeOutputBase64 = includeOutputBytes
                    },
                    RunnerTimeoutMilliseconds);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The SFTP runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

        private static string ResolveRunnerExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(SftpUpload),
                RunnerPathEnvironmentVariable,
                RunnerDirectoryName,
                RunnerExecutableName,
                "SFTP runner");
        }

        private static SftpCommandResponse CreateCommandResponse(SftpPipeResponse response)
        {
            return new SftpCommandResponse
            {
                Success = true,
                Message = response?.Message,
                RemotePath = response?.RemotePath,
                BytesTransferred = response?.BytesTransferred ?? 0
            };
        }

        [DataContract]
        public sealed class SftpCommandRequest
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
        }

        [DataContract]
        internal sealed class SftpCommandResponse
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

        internal sealed class SftpDownloadResult
        {
            public SftpDownloadResult(SftpCommandResponse response, byte[] bytes)
            {
                Response = response;
                Bytes = bytes;
            }

            public SftpCommandResponse Response { get; }
            public byte[] Bytes { get; }
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
