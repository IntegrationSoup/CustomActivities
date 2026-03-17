using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace AmazonActivities
{
    internal abstract class AwsActivityBase : CustomActivity
    {
        protected static AwsActivitySupport.S3UploadRequest CreateRequest(Dictionary<string, string> parameters)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            return new AwsActivitySupport.S3UploadRequest
            {
                BucketName = GetRequiredParameter(parameters, "Bucket Name"),
                FileName = GetRequiredParameter(parameters, "File Name"),
                Region = GetRequiredParameter(parameters, "Region"),
                AccessKeyId = GetRequiredParameter(parameters, "Access Key ID"),
                SecretAccessKey = GetRequiredParameter(parameters, "Secret Access Key")
            };
        }

        protected static void EnsureActivityInstanceReady(IActivityInstance activityInstance)
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

    internal static class AwsActivitySupport
    {
        private const string RunnerPathEnvironmentVariable = "AWSACTIVITIES_RUNNER_PATH";
        private const string RunnerDirectoryName = "AwsActivitiesRunner";
        private const string RunnerExecutableName = "AwsActivitiesRunner.exe";
        private const int RunnerTimeoutMilliseconds = 180000;

        internal static S3UploadResponse Upload(S3UploadRequest request, byte[] inputBytes)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();
            string tempRoot = Path.Combine(Path.GetTempPath(), "AwsActivities", Guid.NewGuid().ToString("N"));
            string requestPath = Path.Combine(tempRoot, "request.json");
            string responsePath = Path.Combine(tempRoot, "response.json");
            string inputPath = Path.Combine(tempRoot, "input.bin");

            Directory.CreateDirectory(tempRoot);
            try
            {
                request.LocalInputPath = inputPath;
                File.WriteAllBytes(inputPath, inputBytes ?? Array.Empty<byte>());
                SerializeJson(requestPath, request);

                RunnerRunResult result = RunRunner(runnerExecutablePath, requestPath, responsePath);
                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        "The AWS runner process failed. " + FormatRunnerFailure(runnerExecutablePath, result));
                }

                if (!File.Exists(responsePath))
                {
                    throw new InvalidOperationException(
                        $"The AWS runner completed without creating '{responsePath}'.");
                }

                S3UploadResponse response = DeserializeJson<S3UploadResponse>(responsePath);
                if (!response.Success)
                {
                    throw new InvalidOperationException(response.Message ?? "The AWS runner reported a failure.");
                }

                return response;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static RunnerRunResult RunRunner(string runnerExecutablePath, string requestPath, string responsePath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = runnerExecutablePath,
                Arguments = BuildRunnerArguments(requestPath, responsePath),
                WorkingDirectory = Path.GetDirectoryName(runnerExecutablePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return RunnerRunResult.Failed(-1, string.Empty, "The runner process could not be started.");
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(RunnerTimeoutMilliseconds))
                {
                    TryKillProcess(process);
                    Task.WaitAll(stdoutTask, stderrTask);
                    return RunnerRunResult.Failed(
                        null,
                        SafeGetTaskResult(stdoutTask),
                        SafeGetTaskResult(stderrTask),
                        $"The runner did not finish within {RunnerTimeoutMilliseconds / 1000} seconds.");
                }

                Task.WaitAll(stdoutTask, stderrTask);

                string stdout = SafeGetTaskResult(stdoutTask);
                string stderr = SafeGetTaskResult(stderrTask);
                if (process.ExitCode == 0)
                {
                    return RunnerRunResult.Succeeded(process.ExitCode, stdout, stderr);
                }

                return RunnerRunResult.Failed(process.ExitCode, stdout, stderr, "The runner exited with an error.");
            }
        }

        private static string ResolveRunnerExecutablePath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(RunnerPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredPath);
                if (File.Exists(fullConfiguredPath))
                {
                    return fullConfiguredPath;
                }

                throw new FileNotFoundException(
                    $"The runner configured by {RunnerPathEnvironmentVariable} was not found at '{fullConfiguredPath}'.",
                    fullConfiguredPath);
            }

            string activityDirectory = Path.GetDirectoryName(typeof(S3Sender).Assembly.Location) ?? string.Empty;
            string hostDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string[] candidatePaths =
            {
                Path.Combine(activityDirectory, RunnerDirectoryName, RunnerExecutableName),
                Path.Combine(hostDirectory, RunnerDirectoryName, RunnerExecutableName)
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new FileNotFoundException(
                $"The AWS runner executable could not be found. Place '{RunnerExecutableName}' under a '{RunnerDirectoryName}' folder beside the activity DLL, or set {RunnerPathEnvironmentVariable}.");
        }

        private static string BuildRunnerArguments(string requestPath, string responsePath)
        {
            return QuoteArgument(requestPath) + " " + QuoteArgument(responsePath);
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (!RequiresQuoting(argument))
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');

            int backslashCount = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append(character);
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    builder.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                builder.Append(character);
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount * 2);
            }

            builder.Append('"');
            return builder.ToString();
        }

        private static bool RequiresQuoting(string argument)
        {
            foreach (char character in argument)
            {
                if (char.IsWhiteSpace(character) || character == '"')
                {
                    return true;
                }
            }

            return false;
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

        private static string SafeGetTaskResult(Task<string> task)
        {
            return task.Status == TaskStatus.RanToCompletion ? task.Result : string.Empty;
        }

        private static string FormatRunnerFailure(string runnerExecutablePath, RunnerRunResult result)
        {
            StringBuilder message = new StringBuilder();
            message.Append("Runner path: ").Append(runnerExecutablePath);

            if (result.ExitCode.HasValue)
            {
                message.Append("; exit code: ").Append(result.ExitCode.Value);
            }

            if (!string.IsNullOrWhiteSpace(result.Reason))
            {
                message.Append("; reason: ").Append(result.Reason);
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError))
            {
                message.Append("; stderr: ").Append(TrimForMessage(result.StandardError));
            }
            else if (!string.IsNullOrWhiteSpace(result.StandardOutput))
            {
                message.Append("; stdout: ").Append(TrimForMessage(result.StandardOutput));
            }

            return message.ToString();
        }

        private static string TrimForMessage(string text)
        {
            string trimmed = text.Trim();
            return trimmed.Length <= 500 ? trimmed : trimmed.Substring(0, 500) + "...";
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

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Best-effort cleanup only.
            }
        }

        [DataContract]
        internal sealed class S3UploadRequest
        {
            [DataMember(Order = 1)]
            public string BucketName { get; set; }

            [DataMember(Order = 2)]
            public string FileName { get; set; }

            [DataMember(Order = 3)]
            public string Region { get; set; }

            [DataMember(Order = 4)]
            public string AccessKeyId { get; set; }

            [DataMember(Order = 5)]
            public string SecretAccessKey { get; set; }

            [DataMember(Order = 6)]
            public string LocalInputPath { get; set; }
        }

        [DataContract]
        internal sealed class S3UploadResponse
        {
            [DataMember(Order = 1)]
            public bool Success { get; set; }

            [DataMember(Order = 2)]
            public string Message { get; set; }

            [DataMember(Order = 3)]
            public string ObjectPath { get; set; }
        }

        private sealed class RunnerRunResult
        {
            private RunnerRunResult(int? exitCode, string standardOutput, string standardError, string reason, bool success)
            {
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
                Reason = reason ?? string.Empty;
                Success = success;
            }

            public int? ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
            public string Reason { get; }
            public bool Success { get; }

            public static RunnerRunResult Succeeded(int exitCode, string standardOutput, string standardError)
            {
                return new RunnerRunResult(exitCode, standardOutput, standardError, string.Empty, success: true);
            }

            public static RunnerRunResult Failed(int? exitCode, string standardOutput, string standardError, string reason = null)
            {
                return new RunnerRunResult(exitCode, standardOutput, standardError, reason, success: false);
            }
        }
    }
}
