using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HtmlToPdfActivities.Renderer
{
    internal static class Program
    {
        private const string BrowserPathEnvironmentVariable = "HTMLTOPDF_BROWSER_PATH";
        private const int BrowserTimeoutMilliseconds = 120000;

        private static int Main(string[] args)
        {
            try
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: HtmlToPdfRenderer <inputHtmlPath> <outputPdfPath>");
                    return 2;
                }

                string inputHtmlPath = Path.GetFullPath(args[0]);
                string outputPdfPath = Path.GetFullPath(args[1]);
                if (!File.Exists(inputHtmlPath))
                {
                    Console.Error.WriteLine($"Input HTML file was not found: {inputHtmlPath}");
                    return 3;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath) ?? AppDomain.CurrentDomain.BaseDirectory);

                BrowserRunResult result = ConvertHtmlToPdf(inputHtmlPath, outputPdfPath);
                if (result.Success)
                {
                    return 0;
                }

                Console.Error.WriteLine(FormatBrowserFailure(result.BrowserExecutablePath, result));
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static BrowserRunResult ConvertHtmlToPdf(string inputHtmlPath, string outputPdfPath)
        {
            string browserExecutablePath = ResolveBrowserExecutablePath();
            string tempRoot = Path.Combine(Path.GetTempPath(), "HtmlToPdfActivities.Renderer", Guid.NewGuid().ToString("N"));
            string profilePath = Path.Combine(tempRoot, "profile");
            string fileUri = new Uri(inputHtmlPath).AbsoluteUri;
            BrowserRunResult lastFailure = null;

            Directory.CreateDirectory(tempRoot);
            try
            {
                foreach (string headlessMode in new[] { "--headless=new", "--headless" })
                {
                    BrowserRunResult result = RunBrowserPrintToPdf(browserExecutablePath, profilePath, outputPdfPath, fileUri, headlessMode);
                    if (result.Success)
                    {
                        return result;
                    }

                    lastFailure = result;

                    TryDeleteFile(outputPdfPath);
                    RecreateDirectory(profilePath);
                }
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }

            return lastFailure ?? BrowserRunResult.Failed(browserExecutablePath, "--headless", null, string.Empty, string.Empty, "The browser did not produce a PDF.");
        }

        private static BrowserRunResult RunBrowserPrintToPdf(string browserExecutablePath, string profilePath, string outputPdfPath, string inputUri, string headlessMode)
        {
            RecreateDirectory(profilePath);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = browserExecutablePath,
                Arguments = BuildBrowserArguments(headlessMode, profilePath, outputPdfPath, inputUri),
                WorkingDirectory = Path.GetDirectoryName(outputPdfPath) ?? AppDomain.CurrentDomain.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return BrowserRunResult.Failed(browserExecutablePath, headlessMode, -1, string.Empty, "The browser process could not be started.");
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(BrowserTimeoutMilliseconds))
                {
                    TryKillProcess(process);
                    Task.WaitAll(stdoutTask, stderrTask);
                    return BrowserRunResult.Failed(
                        browserExecutablePath,
                        headlessMode,
                        null,
                        SafeGetTaskResult(stdoutTask),
                        SafeGetTaskResult(stderrTask),
                        $"The browser did not finish within {BrowserTimeoutMilliseconds / 1000} seconds.");
                }

                Task.WaitAll(stdoutTask, stderrTask);

                string stdout = SafeGetTaskResult(stdoutTask);
                string stderr = SafeGetTaskResult(stderrTask);
                bool pdfCreated = File.Exists(outputPdfPath) && new FileInfo(outputPdfPath).Length > 0;
                if (process.ExitCode == 0 && pdfCreated)
                {
                    return BrowserRunResult.Succeeded(browserExecutablePath, headlessMode, process.ExitCode, stdout, stderr);
                }

                string reason = pdfCreated
                    ? "The browser exited with an error."
                    : "The browser exited without creating a PDF file.";

                return BrowserRunResult.Failed(browserExecutablePath, headlessMode, process.ExitCode, stdout, stderr, reason);
            }
        }

        private static string BuildBrowserArguments(string headlessMode, string profilePath, string outputPdfPath, string inputUri)
        {
            string[] arguments =
            {
                headlessMode,
                "--disable-gpu",
                "--no-first-run",
                "--no-default-browser-check",
                "--disable-background-networking",
                "--disable-sync",
                "--metrics-recording-only",
                "--disable-crash-reporter",
                "--disable-features=OptimizationGuideModelDownloading,MediaRouter,Translate,AutofillServerCommunication",
                "--allow-file-access-from-files",
                "--no-sandbox",
                "--disable-dev-shm-usage",
                $"--user-data-dir={profilePath}",
                "--no-pdf-header-footer",
                $"--print-to-pdf={outputPdfPath}",
                "--virtual-time-budget=10000",
                inputUri
            };

            return string.Join(" ", arguments.Select(QuoteArgument));
        }

        private static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            if (!argument.Any(ch => char.IsWhiteSpace(ch) || ch == '"'))
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

        private static string SafeGetTaskResult(Task<string> task)
        {
            return task.Status == TaskStatus.RanToCompletion ? task.Result : string.Empty;
        }

        private static string ResolveBrowserExecutablePath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(BrowserPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredPath);
                if (File.Exists(fullConfiguredPath))
                {
                    return fullConfiguredPath;
                }

                throw new FileNotFoundException(
                    $"The browser configured by {BrowserPathEnvironmentVariable} was not found at '{fullConfiguredPath}'.",
                    fullConfiguredPath);
            }

            string applicationDirectory = AppDomain.CurrentDomain.BaseDirectory ?? string.Empty;
            string localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string[] candidatePaths =
            {
                Path.Combine(applicationDirectory, "chrome.exe"),
                Path.Combine(applicationDirectory, "msedge.exe"),
                Path.Combine(applicationDirectory, "chromium.exe"),
                Path.Combine(applicationDirectory, "chrome-win", "chrome.exe"),
                Path.Combine(programFiles, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFilesX86, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(programFiles, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(programFilesX86, "Microsoft", "Edge", "Application", "msedge.exe"),
                Path.Combine(localApplicationData, "Google", "Chrome", "Application", "chrome.exe"),
                Path.Combine(localApplicationData, "Microsoft", "Edge", "Application", "msedge.exe")
            };

            foreach (string candidatePath in candidatePaths)
            {
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                {
                    return candidatePath;
                }
            }

            throw new FileNotFoundException(
                $"A compatible Chrome, Edge, or Chromium executable could not be found. Install Chrome or Edge, place the browser beside the renderer executable, or set {BrowserPathEnvironmentVariable}.");
        }

        private static string FormatBrowserFailure(string browserExecutablePath, BrowserRunResult failure)
        {
            if (failure == null)
            {
                return $"Browser path: {browserExecutablePath}";
            }

            StringBuilder message = new StringBuilder();
            message.Append("Browser path: ").Append(browserExecutablePath);
            message.Append("; headless mode: ").Append(failure.HeadlessMode);

            if (failure.ExitCode.HasValue)
            {
                message.Append("; exit code: ").Append(failure.ExitCode.Value);
            }

            if (!string.IsNullOrWhiteSpace(failure.Reason))
            {
                message.Append("; reason: ").Append(failure.Reason);
            }

            if (!string.IsNullOrWhiteSpace(failure.StandardError))
            {
                message.Append("; stderr: ").Append(TrimForMessage(failure.StandardError));
            }
            else if (!string.IsNullOrWhiteSpace(failure.StandardOutput))
            {
                message.Append("; stdout: ").Append(TrimForMessage(failure.StandardOutput));
            }

            return message.ToString();
        }

        private static string TrimForMessage(string text)
        {
            string trimmed = text.Trim();
            return trimmed.Length <= 500 ? trimmed : trimmed.Substring(0, 500) + "...";
        }

        private static void RecreateDirectory(string path)
        {
            TryDeleteDirectory(path);
            Directory.CreateDirectory(path);
        }

        private static void TryDeleteFile(string path)
        {
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
                // Best-effort cleanup only.
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

        private sealed class BrowserRunResult
        {
            private BrowserRunResult(string browserExecutablePath, string headlessMode, int? exitCode, string standardOutput, string standardError, string reason, bool success)
            {
                BrowserExecutablePath = browserExecutablePath;
                HeadlessMode = headlessMode;
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
                Reason = reason ?? string.Empty;
                Success = success;
            }

            public string BrowserExecutablePath { get; }
            public string HeadlessMode { get; }
            public int? ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
            public string Reason { get; }
            public bool Success { get; }

            public static BrowserRunResult Succeeded(string browserExecutablePath, string headlessMode, int exitCode, string standardOutput, string standardError)
            {
                return new BrowserRunResult(browserExecutablePath, headlessMode, exitCode, standardOutput, standardError, string.Empty, success: true);
            }

            public static BrowserRunResult Failed(string browserExecutablePath, string headlessMode, int? exitCode, string standardOutput, string standardError, string reason = null)
            {
                return new BrowserRunResult(browserExecutablePath, headlessMode, exitCode, standardOutput, standardError, reason, success: false);
            }
        }
    }
}
