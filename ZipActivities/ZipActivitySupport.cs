using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ZipActivities
{
    public abstract class ZipActivityBase : CustomActivity
    {
        private protected static ZipActivitySupport.ZipRequest CreateDirectoryRequest(Dictionary<string, string> parameters)
        {
            return new ZipActivitySupport.ZipRequest
            {
                SourceDirectory = GetRequiredParameter(parameters, "Source Directory"),
                IncludeBaseDirectory = GetOptionalBoolean(parameters, "Include Base Directory")
            };
        }

        private protected static string GetRequiredParameter(Dictionary<string, string> parameters, string name)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (parameters.TryGetValue(name, out string value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            throw new ArgumentException($"Parameter '{name}' is required.");
        }

        private protected static bool GetOptionalBoolean(Dictionary<string, string> parameters, string name, bool defaultValue = false)
        {
            if (parameters == null || !parameters.TryGetValue(name, out string value) || string.IsNullOrWhiteSpace(value))
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

        private protected static void EnsureActivityInstanceReady(IActivityInstance activityInstance)
        {
            if (activityInstance == null)
            {
                throw new ArgumentNullException(nameof(activityInstance));
            }

            if (activityInstance.ResponseMessage == null)
            {
                throw new InvalidOperationException("Error: Response Message not set.");
            }
        }
    }

    internal static class ZipActivitySupport
    {
        private const string RunnerPathEnvironmentVariable = "ZIPACTIVITIES_RUNNER_PATH";
        private const string RunnerDirectoryName = "ZipActivitiesRunner";
        private const string RunnerExecutableName = "ZipActivitiesRunner.exe";
        private const int RunnerTimeoutMilliseconds = 600000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("ZIP runner");

        internal static ZipResponse CreateZipFile(ZipRequest request)
        {
            return Execute("create-zip-file", request);
        }

        internal static ZipResponse CreateZipMessage(ZipRequest request)
        {
            return Execute("create-zip-message", request);
        }

        internal static ZipResponse ExtractZipFile(ZipRequest request)
        {
            return Execute("extract-zip-file", request);
        }

        private static ZipResponse Execute(string operation, ZipRequest request)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();

            try
            {
                return runnerClient.Invoke<ZipRequest, ZipResponse>(
                    runnerExecutablePath,
                    operation,
                    request,
                    RunnerTimeoutMilliseconds);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The ZIP runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

        private static string ResolveRunnerExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(CreateZipFile),
                RunnerPathEnvironmentVariable,
                RunnerDirectoryName,
                RunnerExecutableName,
                "ZIP runner");
        }

        [DataContract]
        internal sealed class ZipRequest
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
        internal sealed class ZipResponse
        {
            [DataMember(Order = 1)]
            public string Message { get; set; }

            [DataMember(Order = 2)]
            public int FileCount { get; set; }

            [DataMember(Order = 3)]
            public int DirectoryCount { get; set; }

            [DataMember(Order = 4)]
            public long ZipLength { get; set; }

            [DataMember(Order = 5)]
            public string OutputBase64 { get; set; }
        }
    }
}
