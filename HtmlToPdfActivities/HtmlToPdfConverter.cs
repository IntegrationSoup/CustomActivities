using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace HtmlToPdfActivities
{
    [DisplayName("Convert HTML to PDF")]
    [InMessage(@"<html><body><h1>Sample PDF</h1><p>This HTML will be rendered to PDF.</p></body></html>", TypeOfMessages.Text)]
    [OutMessage(@"JVBERi0xLjQKJ...", TypeOfMessages.Binary)]
    public class HtmlToPdfConverter : CustomActivity
    {
        private const string RendererPathEnvironmentVariable = "HTMLTOPDF_RENDERER_PATH";
        private const string RendererDirectoryName = "HtmlToPdfRenderer";
        private const string RendererExecutableName = "HtmlToPdfRenderer.exe";
        private const int RendererTimeoutMilliseconds = 180000;

        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
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

            string html = activityInstance.Message.Text ?? string.Empty;
            byte[] pdfBytes = ConvertHtmlToPdf(html);

            // HL7Soup binary messages are stored as base64 in the Text payload.
            activityInstance.ResponseMessage.SetText(Convert.ToBase64String(pdfBytes));
        }

        private static byte[] ConvertHtmlToPdf(string html)
        {
            string rendererExecutablePath = ResolveRendererExecutablePath();
            string tempRoot = Path.Combine(Path.GetTempPath(), "HtmlToPdfActivities", Guid.NewGuid().ToString("N"));
            string htmlPath = Path.Combine(tempRoot, "input.html");
            string pdfPath = Path.Combine(tempRoot, "output.pdf");

            Directory.CreateDirectory(tempRoot);
            try
            {
                File.WriteAllText(
                    htmlPath,
                    string.IsNullOrWhiteSpace(html) ? "<html><body></body></html>" : html,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                RendererRunResult result = RunRenderer(rendererExecutablePath, htmlPath, pdfPath);
                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        "The HTML-to-PDF renderer process failed. " + FormatRendererFailure(rendererExecutablePath, result));
                }

                if (!File.Exists(pdfPath))
                {
                    throw new InvalidOperationException(
                        $"The HTML-to-PDF renderer completed without creating '{pdfPath}'.");
                }

                return File.ReadAllBytes(pdfPath);
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static RendererRunResult RunRenderer(string rendererExecutablePath, string htmlPath, string pdfPath)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = rendererExecutablePath,
                Arguments = BuildRendererArguments(htmlPath, pdfPath),
                WorkingDirectory = Path.GetDirectoryName(rendererExecutablePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo))
            {
                if (process == null)
                {
                    return RendererRunResult.Failed(-1, string.Empty, "The renderer process could not be started.");
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(RendererTimeoutMilliseconds))
                {
                    TryKillProcess(process);
                    Task.WaitAll(stdoutTask, stderrTask);
                    return RendererRunResult.Failed(
                        null,
                        SafeGetTaskResult(stdoutTask),
                        SafeGetTaskResult(stderrTask),
                        $"The renderer did not finish within {RendererTimeoutMilliseconds / 1000} seconds.");
                }

                Task.WaitAll(stdoutTask, stderrTask);

                string stdout = SafeGetTaskResult(stdoutTask);
                string stderr = SafeGetTaskResult(stderrTask);
                if (process.ExitCode == 0)
                {
                    return RendererRunResult.Succeeded(process.ExitCode, stdout, stderr);
                }

                return RendererRunResult.Failed(
                    process.ExitCode,
                    stdout,
                    stderr,
                    "The renderer exited with an error.");
            }
        }

        private static string BuildRendererArguments(string htmlPath, string pdfPath)
        {
            return QuoteArgument(htmlPath) + " " + QuoteArgument(pdfPath);
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

        private static string SafeGetTaskResult(Task<string> task)
        {
            return task.Status == TaskStatus.RanToCompletion ? task.Result : string.Empty;
        }

        private static string ResolveRendererExecutablePath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(RendererPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredPath);
                if (File.Exists(fullConfiguredPath))
                {
                    return fullConfiguredPath;
                }

                throw new FileNotFoundException(
                    $"The renderer configured by {RendererPathEnvironmentVariable} was not found at '{fullConfiguredPath}'.",
                    fullConfiguredPath);
            }

            string activityDirectory = Path.GetDirectoryName(typeof(HtmlToPdfConverter).Assembly.Location) ?? string.Empty;
            string hostDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string[] candidatePaths =
            {
                Path.Combine(activityDirectory, RendererDirectoryName, RendererExecutableName),
                Path.Combine(hostDirectory, RendererDirectoryName, RendererExecutableName)
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new FileNotFoundException(
                $"The HTML-to-PDF renderer executable could not be found. Place '{RendererExecutableName}' under a '{RendererDirectoryName}' folder beside the activity DLL, or set {RendererPathEnvironmentVariable}.");
        }

        private static string FormatRendererFailure(string rendererExecutablePath, RendererRunResult result)
        {
            StringBuilder message = new StringBuilder();
            message.Append("Renderer path: ").Append(rendererExecutablePath);

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

        private static void TryKillProcess(System.Diagnostics.Process process)
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

        private sealed class RendererRunResult
        {
            private RendererRunResult(int? exitCode, string standardOutput, string standardError, string reason, bool success)
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

            public static RendererRunResult Succeeded(int exitCode, string standardOutput, string standardError)
            {
                return new RendererRunResult(exitCode, standardOutput, standardError, string.Empty, success: true);
            }

            public static RendererRunResult Failed(int? exitCode, string standardOutput, string standardError, string reason = null)
            {
                return new RendererRunResult(exitCode, standardOutput, standardError, reason, success: false);
            }
        }
    }
}
