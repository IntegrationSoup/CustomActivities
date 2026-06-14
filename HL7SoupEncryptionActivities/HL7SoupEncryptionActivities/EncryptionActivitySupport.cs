using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace HL7SoupEncryptionActivities
{
    public abstract class EncryptionActivityBase : CustomActivity
    {
        private protected static EncryptionActivitySupport.EncryptionRequest CreateRequest(Dictionary<string, string> parameters, string operation)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (!parameters.TryGetValue("Encryption Key", out string encryptionKey) || string.IsNullOrWhiteSpace(encryptionKey))
            {
                throw new ArgumentException("Parameter 'Encryption Key' is required.");
            }

            return new EncryptionActivitySupport.EncryptionRequest
            {
                Operation = operation,
                EncryptionKey = encryptionKey
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
    }

    internal static class EncryptionActivitySupport
    {
        private const string RunnerPathEnvironmentVariable = "ENCRYPTIONACTIVITIES_RUNNER_PATH";
        private const string RunnerDirectoryName = "EncryptionActivitiesRunner";
        private const string RunnerExecutableName = "EncryptionActivitiesRunner.exe";
        private const int RunnerTimeoutMilliseconds = 180000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("encryption runner");

        internal static string Execute(EncryptionRequest request, string inputText)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();

            try
            {
                EncryptionPipeResponse response = runnerClient.Invoke<EncryptionPipeRequest, EncryptionPipeResponse>(
                    runnerExecutablePath,
                    "transform-text",
                    new EncryptionPipeRequest
                    {
                        Operation = request.Operation,
                        EncryptionKey = request.EncryptionKey,
                        InputText = inputText ?? string.Empty
                    },
                    RunnerTimeoutMilliseconds);

                return response?.OutputText ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The encryption runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

        private static string ResolveRunnerExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(EncryptMessage),
                RunnerPathEnvironmentVariable,
                RunnerDirectoryName,
                RunnerExecutableName,
                "encryption runner");
        }

        internal sealed class EncryptionRequest
        {
            internal string Operation { get; set; }
            internal string EncryptionKey { get; set; }
        }

        [DataContract]
        private sealed class EncryptionPipeRequest
        {
            [DataMember(Order = 1)]
            public string Operation { get; set; }

            [DataMember(Order = 2)]
            public string EncryptionKey { get; set; }

            [DataMember(Order = 3)]
            public string InputText { get; set; }
        }

        [DataContract]
        private sealed class EncryptionPipeResponse
        {
            [DataMember(Order = 1)]
            public string OutputText { get; set; }
        }
    }
}
