using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

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
                FileName = GetRequiredParameter(parameters, "File Name")
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

        private static string GetRequiredParameter(Dictionary<string, string> parameters, string name)
        {
            if (parameters.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
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
                        InputBase64 = Convert.ToBase64String(inputBytes ?? Array.Empty<byte>())
                    },
                    RunnerTimeoutMilliseconds);

                return new BlobUploadResponse
                {
                    Success = true,
                    Message = response?.Message ?? "Code Executed Successfully",
                    BlobPath = response?.BlobPath ?? string.Empty
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
            internal string ConnectionString { get; set; }
            internal string ContainerName { get; set; }
            internal string FileName { get; set; }
        }

        internal sealed class BlobUploadResponse
        {
            internal bool Success { get; set; }
            internal string Message { get; set; }
            internal string BlobPath { get; set; }
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
