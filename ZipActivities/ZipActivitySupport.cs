using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.IO;
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

#if EXTENSION_BRIDGE_RUNNER
            throw new ExtensionReasonException($"The parameter '{name}' is required.");
#else
            throw new ArgumentException($"Parameter '{name}' is required.");
#endif
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
#if EXTENSION_BRIDGE_RUNNER
                    throw new ExtensionReasonException($"The parameter '{name}' must be true or false.");
#else
                    throw new ArgumentException($"Parameter '{name}' must be true or false.");
#endif
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
#if EXTENSION_BRIDGE_RUNNER
                ExplainKnownFailures(operation, request);
#endif
                return runnerClient.Invoke<ZipRequest, ZipResponse>(
                    runnerExecutablePath,
                    operation,
                    request,
                    RunnerTimeoutMilliseconds);
            }
#if EXTENSION_BRIDGE_RUNNER
            catch (ExtensionReasonException)
            {
                throw;
            }
#endif
            catch (Exception ex)
            {
#if EXTENSION_BRIDGE_RUNNER
                string reason = DescribeKnownFailure(operation, request, ex);
                if (reason != null)
                {
                    throw new ExtensionReasonException(reason);
                }
#endif
                throw new InvalidOperationException(
                    "The ZIP runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

#if EXTENSION_BRIDGE_RUNNER
        // Version 5 runs this library through the extension bridge, which shows the Host only a
        // sentence the library wrote on purpose. The failures this library knows are named here,
        // from what the person configured and never from another exception's text.
        // ZipArchiveOperations keeps every one of its own checks, so nothing is skipped.
        private static void ExplainKnownFailures(string operation, ZipRequest request)
        {
            switch (operation)
            {
                case "create-zip-file":
                    RequireSourceDirectory(request.SourceDirectory);
                    if (!request.OverwriteExistingFile && File.Exists(Path.GetFullPath(request.ZipFilePath.Trim())))
                    {
                        throw new ExtensionReasonException($"The ZIP file '{request.ZipFilePath.Trim()}' already exists. Turn on Overwrite Existing File to replace it.");
                    }

                    break;
                case "create-zip-message":
                    RequireSourceDirectory(request.SourceDirectory);
                    break;
                case "extract-zip-file":
                    if (!File.Exists(Path.GetFullPath(request.ZipFilePath.Trim())))
                    {
                        throw new ExtensionReasonException($"The ZIP file '{request.ZipFilePath.Trim()}' was not found.");
                    }

                    break;
            }
        }

        private static void RequireSourceDirectory(string sourceDirectory)
        {
            if (!Directory.Exists(Path.GetFullPath(sourceDirectory.Trim())))
            {
                throw new ExtensionReasonException($"The source directory '{sourceDirectory.Trim()}' was not found.");
            }
        }

        // Only exception types whose meaning does not depend on who threw them are named.
        private static string DescribeKnownFailure(string operation, ZipRequest request, Exception exception)
        {
            if (exception is UnauthorizedAccessException)
            {
                return operation == "extract-zip-file"
                    ? $"Access was refused while extracting '{request.ZipFilePath.Trim()}' to '{request.DestinationDirectory.Trim()}'."
                    : operation == "create-zip-file"
                        ? $"Access was refused while zipping '{request.SourceDirectory.Trim()}' to '{request.ZipFilePath.Trim()}'."
                        : $"Access was refused while zipping '{request.SourceDirectory.Trim()}'.";
            }

            if (exception is InvalidDataException && operation == "extract-zip-file")
            {
                return $"The ZIP file '{request.ZipFilePath.Trim()}' is damaged, or holds an entry that cannot be extracted safely.";
            }

            return null;
        }
#endif

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
