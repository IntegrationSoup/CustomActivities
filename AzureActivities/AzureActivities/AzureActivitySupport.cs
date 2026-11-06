using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;

namespace AzureActivities
{
    public abstract class AzureActivityBase : CustomActivity
    {
        private protected static AzureActivitySupport.BlobUploadRequest CreateRequest(Dictionary<string, string> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return new AzureActivitySupport.BlobUploadRequest
            {
                ConnectionString = GetRequiredParameter(parameters, "Connection String"),
                ContainerName = GetRequiredParameter(parameters, "Container Name"),
                FileName = GetRequiredParameter(parameters, "File Name", preserveWhitespace: true),
                CreateContainerIfMissing = parameters.TryGetValue("Create Container If Missing", out string create) ? create : null,
                NewContainerAccess = parameters.TryGetValue("New Container Access", out string access) ? access : null,
                OverwriteExistingBlob = parameters.TryGetValue("Overwrite Existing Blob", out string overwrite) ? overwrite : null,
                ExpectedETag = parameters.TryGetValue("Expected ETag", out string etag) ? etag : null
            };
        }

        private protected static void EnsureActivityInstanceReady(IActivityInstance activityInstance)
        {
            if (activityInstance == null)
            {
                throw new ArgumentNullException(nameof(activityInstance));
            }

            if (activityInstance.Message == null)
            {
                throw new InvalidOperationException("Error: Activity Message not set.");
            }

            if (activityInstance.ResponseMessage == null)
            {
                throw new InvalidOperationException("Error: Response Message not set.");
            }
        }

        private protected static byte[] GetMessageBytes(IMessage message)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            if (message is IDicomMessage dicomMessage)
            {
                string base64Dicom = dicomMessage.GetBase64EncodedDicom();
                return string.IsNullOrEmpty(base64Dicom)
                    ? Array.Empty<byte>()
                    : Convert.FromBase64String(base64Dicom);
            }

            string text = message.Text ?? string.Empty;
            if (IsBinaryMessage(message))
            {
                return Convert.FromBase64String(text);
            }

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        }

        private static bool IsBinaryMessage(IMessage message)
        {
            if (message == null)
            {
                return false;
            }

            Type messageType = message.GetType();
            if (string.Equals(messageType.Name, "BinaryMessage", StringComparison.Ordinal))
            {
                return true;
            }

            object reflectedMessageType = messageType.GetProperty("MessageType")?.GetValue(message);
            return string.Equals(reflectedMessageType?.ToString(), "Binary", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetRequiredParameter(Dictionary<string, string> parameters, string name, bool preserveWhitespace = false)
        {
            if (parameters.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return preserveWhitespace ? value : value.Trim();
            }

            throw new ArgumentException($"Parameter '{name}' is required.");
        }
    }

    internal static class AzureActivitySupport
    {
        private const string RunnerPathEnvironmentVariable = "AZUREACTIVITIES_RUNNER_PATH";
        private const string RunnerDirectoryName = "AzureActivitiesRunner";
        private const string RunnerExecutableName = "AzureActivitiesRunner.exe";
        private const int RunnerTimeoutMilliseconds = 180000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("Azure runner");

        internal static BlobDownloadResponse Download(BlobDownloadRequest request)
        {
            return runnerClient.Invoke<BlobDownloadRequest, BlobDownloadResponse>(ResolveRunnerExecutablePath(), "download-blob", request, RunnerTimeoutMilliseconds);
        }

        internal static BlobUploadResponse Upload(BlobUploadRequest request, byte[] inputBytes)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();

            try
            {
                BlobUploadPipeResponse response = runnerClient.Invoke<BlobUploadPipeRequest, BlobUploadPipeResponse>(
                    runnerExecutablePath,
                    "upload-blob",
                    new BlobUploadPipeRequest
                    {
                        ConnectionString = request.ConnectionString,
                        ContainerName = request.ContainerName,
                        FileName = request.FileName,
                        InputBase64 = Convert.ToBase64String(inputBytes ?? Array.Empty<byte>()),
                        NewContainerAccess = request.NewContainerAccess,
                        CreateContainerIfMissing = request.CreateContainerIfMissing,
                        OverwriteExistingBlob = request.OverwriteExistingBlob,
                        ExpectedETag = request.ExpectedETag
                    },
                    RunnerTimeoutMilliseconds);

                return new BlobUploadResponse
                {
                    Success = true,
                    Message = response?.Message ?? "Code Executed Successfully",
                    BlobPath = response?.BlobPath ?? string.Empty,
                    ETag = response?.ETag,
                    VersionId = response?.VersionId
                };
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The Azure runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

        private static string ResolveRunnerExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(BlobSender),
                RunnerPathEnvironmentVariable,
                RunnerDirectoryName,
                RunnerExecutableName,
                "Azure runner");
        }

        internal sealed class BlobUploadRequest
        {
            public string NewContainerAccess { get; set; }
            public string CreateContainerIfMissing { get; set; }
            public string OverwriteExistingBlob { get; set; }
            public string ExpectedETag { get; set; }
            internal string ConnectionString { get; set; }
            internal string ContainerName { get; set; }
            internal string FileName { get; set; }
        }

        internal sealed class BlobUploadResponse
        {
            public string ETag { get; set; }
            public string VersionId { get; set; }
            internal bool Success { get; set; }
            internal string Message { get; set; }
            internal string BlobPath { get; set; }
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
