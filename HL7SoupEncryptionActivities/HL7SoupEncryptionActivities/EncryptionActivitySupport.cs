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
#if EXTENSION_BRIDGE_RUNNER
                throw new ExtensionReasonException("The parameter 'Encryption Key' is required.");
#else
                throw new ArgumentException("Parameter 'Encryption Key' is required.");
#endif
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
#if EXTENSION_BRIDGE_RUNNER
                string reason = DescribeKnownFailure(request, inputText, ex);
                if (reason != null)
                {
                    throw new ExtensionReasonException(reason);
                }
#endif
                throw new InvalidOperationException(
                    "The encryption runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

#if EXTENSION_BRIDGE_RUNNER
        // Version 5 runs this library through the extension bridge, which shows the Host only a sentence
        // the library wrote on purpose. The failures named here are decided by the library's own checks,
        // never from another exception's text, and a sentence never holds the key or the message.
        internal const string NotEncryptedText = "The message cannot be decrypted: it is not encrypted text (it is not Base64).";
        internal const string WrongKeyOrChangedText = "The message could not be decrypted with this Encryption Key. Either the key differs from the one it was encrypted with, or the text is not the unchanged output of an Encrypt Message step.";

        private static string DescribeKnownFailure(EncryptionRequest request, string inputText, Exception failure)
        {
            if (!string.Equals(request?.Operation, "decrypt", StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                Convert.FromBase64String(inputText ?? string.Empty);
            }
            catch (FormatException)
            {
                return NotEncryptedText;
            }

            // A cryptographic failure is blamed on the key or the text only when encryption itself is shown
            // to work here: the same key must encrypt and decrypt a sample. Otherwise it stays masked.
            if (failure is System.Security.Cryptography.CryptographicException && RoundTripWorks(request.EncryptionKey))
            {
                return WrongKeyOrChangedText;
            }

            return null;
        }

        private static bool RoundTripWorks(string encryptionKey)
        {
            const string sample = "round trip check";
            try
            {
                string runner = ResolveRunnerExecutablePath();
                EncryptionPipeResponse encrypted = runnerClient.Invoke<EncryptionPipeRequest, EncryptionPipeResponse>(
                    runner, "transform-text", new EncryptionPipeRequest { Operation = "encrypt", EncryptionKey = encryptionKey, InputText = sample }, RunnerTimeoutMilliseconds);
                EncryptionPipeResponse decrypted = runnerClient.Invoke<EncryptionPipeRequest, EncryptionPipeResponse>(
                    runner, "transform-text", new EncryptionPipeRequest { Operation = "decrypt", EncryptionKey = encryptionKey, InputText = encrypted?.OutputText }, RunnerTimeoutMilliseconds);
                return string.Equals(decrypted?.OutputText, sample, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

#endif
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
