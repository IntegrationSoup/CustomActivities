using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

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

        internal static SftpCommandResponse Upload(SftpCommandRequest request, byte[] inputBytes)
        {
            SftpCommandResult result = Execute(request, inputBytes, expectOutputFile: false);
            return result.Response;
        }

        internal static SftpDownloadResult Download(SftpCommandRequest request)
        {
            SftpCommandResult result = Execute(request, null, expectOutputFile: true);
            return new SftpDownloadResult(result.Response, result.OutputBytes ?? Array.Empty<byte>());
        }

        private static SftpCommandResult Execute(SftpCommandRequest request, byte[] inputBytes, bool expectOutputFile)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();
            string tempRoot = Path.Combine(Path.GetTempPath(), "SftpActivities", Guid.NewGuid().ToString("N"));
            string requestPath = Path.Combine(tempRoot, "request.json");
            string responsePath = Path.Combine(tempRoot, "response.json");
            string inputPath = Path.Combine(tempRoot, "input.bin");
            string outputPath = Path.Combine(tempRoot, "output.bin");

            Directory.CreateDirectory(tempRoot);
            try
            {
                request.LocalInputPath = inputBytes != null ? inputPath : null;
                request.LocalOutputPath = expectOutputFile ? outputPath : null;

                if (inputBytes != null)
                {
                    File.WriteAllBytes(inputPath, inputBytes);
                }

                SerializeJson(requestPath, request);

                RunnerRunResult result = RunRunner(runnerExecutablePath, requestPath, responsePath);
                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        "The SFTP runner process failed. " + FormatRunnerFailure(runnerExecutablePath, result));
                }

                if (!File.Exists(responsePath))
                {
                    throw new InvalidOperationException(
                        $"The SFTP runner completed without creating '{responsePath}'.");
                }

                SftpCommandResponse response = DeserializeJson<SftpCommandResponse>(responsePath);
                if (!response.Success)
                {
                    throw new InvalidOperationException(response.Message ?? "The SFTP runner reported a failure.");
                }

                byte[] outputBytes = null;
                if (expectOutputFile)
                {
                    if (!File.Exists(outputPath))
                    {
                        throw new InvalidOperationException(
                            $"The SFTP runner reported success but did not create '{outputPath}'.");
                    }

                    outputBytes = File.ReadAllBytes(outputPath);
                }

                return new SftpCommandResult(response, outputBytes);
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

                return RunnerRunResult.Failed(
                    process.ExitCode,
                    stdout,
                    stderr,
                    "The runner exited with an error.");
            }
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

            string activityDirectory = Path.GetDirectoryName(typeof(SftpUpload).Assembly.Location) ?? string.Empty;
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
                $"The SFTP runner executable could not be found. Place '{RunnerExecutableName}' under a '{RunnerDirectoryName}' folder beside the activity DLL, or set {RunnerPathEnvironmentVariable}.");
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

        private static string SafeGetTaskResult(Task<string> task)
        {
            return task.Status == TaskStatus.RanToCompletion ? task.Result : string.Empty;
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

            [DataMember(Order = 12)]
            public string LocalInputPath { get; set; }

            [DataMember(Order = 13)]
            public string LocalOutputPath { get; set; }
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

        private sealed class SftpCommandResult
        {
            public SftpCommandResult(SftpCommandResponse response, byte[] outputBytes)
            {
                Response = response;
                OutputBytes = outputBytes;
            }

            public SftpCommandResponse Response { get; }
            public byte[] OutputBytes { get; }
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
