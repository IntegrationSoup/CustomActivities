using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace HtmlToPdfActivities.Renderer
{
    internal static class Program
    {
        private const string BrowserPathEnvironmentVariable = "HTMLTOPDF_BROWSER_PATH";
        private const int BrowserTimeoutMilliseconds = 120000;

        private static readonly object browserPreferenceGate = new object();
        private static string preferredBrowserPath;
        private static string preferredHeadlessMode;

        private static int Main(string[] args)
        {
            try
            {
                int? serverExitCode = PersistentRunnerServer.RunIfRequested(args, HandleServerRequest);
                if (serverExitCode.HasValue)
                {
                    return serverExitCode.Value;
                }

                return RunOneShot(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                return 1;
            }
        }

        private static int RunOneShot(string[] args)
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

            HtmlToPdfPipeResponse response = ConvertHtmlToPdf(File.ReadAllText(inputHtmlPath), includePdfBase64: false, outputPdfPath);
            if (!File.Exists(outputPdfPath))
            {
                Console.Error.WriteLine($"The HTML-to-PDF renderer completed without creating '{outputPdfPath}'.");
                return 1;
            }

            return response != null ? 0 : 1;
        }

        private static string HandleServerRequest(string operation, string payloadJson)
        {
            if (!string.Equals(operation, "convert-html-to-pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported HTML-to-PDF operation '{operation}'.");
            }

            HtmlToPdfPipeRequest request = PersistentRunnerJson.Deserialize<HtmlToPdfPipeRequest>(payloadJson);
            HtmlToPdfPipeResponse response = ConvertHtmlToPdf(request?.Html ?? string.Empty, includePdfBase64: true, outputPdfPath: null);
            return PersistentRunnerJson.Serialize(response);
        }

        private static HtmlToPdfPipeResponse ConvertHtmlToPdf(string html, bool includePdfBase64, string outputPdfPath)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "HtmlToPdfActivities.Renderer", Guid.NewGuid().ToString("N"));
            string htmlPath = Path.Combine(tempRoot, "input.html");
            string pdfPath = outputPdfPath ?? Path.Combine(tempRoot, "output.pdf");

            Directory.CreateDirectory(tempRoot);
            try
            {
                File.WriteAllText(
                    htmlPath,
                    string.IsNullOrWhiteSpace(html) ? "<html><body></body></html>" : html,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                BrowserRunResult result = PrintToPdf(htmlPath, pdfPath);
                if (!result.Success)
                {
                    throw new InvalidOperationException(FormatBrowserFailure(result.BrowserExecutablePath, result));
                }

                if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length == 0)
                {
                    throw new InvalidOperationException($"The browser completed without creating '{pdfPath}'.");
                }

                return new HtmlToPdfPipeResponse
                {
                    BrowserPath = result.BrowserExecutablePath,
                    HeadlessMode = result.HeadlessMode,
                    PdfBase64 = includePdfBase64 ? Convert.ToBase64String(File.ReadAllBytes(pdfPath)) : string.Empty
                };
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static BrowserRunResult PrintToPdf(string inputHtmlPath, string outputPdfPath)
        {
            string fileUri = new Uri(inputHtmlPath).AbsoluteUri;
            string tempProfileRoot = Path.Combine(Path.GetTempPath(), "HtmlToPdfActivities.Renderer.Profile", Guid.NewGuid().ToString("N"));
            BrowserRunResult lastFailure = null;

            Directory.CreateDirectory(tempProfileRoot);
            try
            {
                foreach (BrowserAttempt attempt in GetOrderedBrowserAttempts())
                {
                    string profilePath = Path.Combine(tempProfileRoot, Guid.NewGuid().ToString("N"));
                    BrowserRunResult result = RunBrowserPrintToPdf(
                        attempt.BrowserPath,
                        profilePath,
                        outputPdfPath,
                        fileUri,
                        attempt.HeadlessMode);

                    if (result.Success)
                    {
                        RememberSuccessfulAttempt(result.BrowserExecutablePath, result.HeadlessMode);
                        return result;
                    }

                    lastFailure = result;
                    TryDeleteFile(outputPdfPath);
                }
            }
            finally
            {
                TryDeleteDirectory(tempProfileRoot);
            }

            return lastFailure ?? BrowserRunResult.Failed(string.Empty, "--headless", null, string.Empty, string.Empty, "The browser did not produce a PDF.");
        }

        private static IEnumerable<BrowserAttempt> GetOrderedBrowserAttempts()
        {
            List<string> browserCandidates = ResolveBrowserExecutablePaths();
            string preferredPathSnapshot;
            string preferredHeadlessSnapshot;

            lock (browserPreferenceGate)
            {
                preferredPathSnapshot = preferredBrowserPath;
                preferredHeadlessSnapshot = preferredHeadlessMode;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(preferredPathSnapshot) && browserCandidates.Contains(preferredPathSnapshot, StringComparer.OrdinalIgnoreCase))
            {
                foreach (BrowserAttempt preferredAttempt in BuildAttemptsForBrowser(preferredPathSnapshot, preferredHeadlessSnapshot))
                {
                    string key = preferredAttempt.BrowserPath + "|" + preferredAttempt.HeadlessMode;
                    if (seen.Add(key))
                    {
                        yield return preferredAttempt;
                    }
                }
            }

            foreach (string browserPath in browserCandidates)
            {
                foreach (BrowserAttempt attempt in BuildAttemptsForBrowser(browserPath, null))
                {
                    string key = attempt.BrowserPath + "|" + attempt.HeadlessMode;
                    if (seen.Add(key))
                    {
                        yield return attempt;
                    }
                }
            }
        }

        private static IEnumerable<BrowserAttempt> BuildAttemptsForBrowser(string browserPath, string preferredMode)
        {
            if (!string.IsNullOrWhiteSpace(preferredMode))
            {
                yield return new BrowserAttempt(browserPath, preferredMode);
            }

            foreach (string headlessMode in new[] { "--headless=new", "--headless=old", "--headless" })
            {
                if (!string.Equals(headlessMode, preferredMode, StringComparison.OrdinalIgnoreCase))
                {
                    yield return new BrowserAttempt(browserPath, headlessMode);
                }
            }
        }

        private static void RememberSuccessfulAttempt(string browserPath, string headlessMode)
        {
            lock (browserPreferenceGate)
            {
                preferredBrowserPath = browserPath;
                preferredHeadlessMode = headlessMode;
            }
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
                "--print-to-pdf-no-header",
                $"--print-to-pdf={outputPdfPath}",
                "--virtual-time-budget=10000",
                inputUri
            };

            return string.Join(" ", arguments.Select(PersistentRunnerCommandLine.QuoteArgument));
        }

        private static string SafeGetTaskResult(Task<string> task)
        {
            return task.Status == TaskStatus.RanToCompletion ? task.Result : string.Empty;
        }

        private static List<string> ResolveBrowserExecutablePaths()
        {
            string configuredPath = Environment.GetEnvironmentVariable(BrowserPathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredPath);
                if (File.Exists(fullConfiguredPath))
                {
                    return new List<string> { fullConfiguredPath };
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

            List<string> resolvedPaths = new List<string>();
            foreach (string candidatePath in candidatePaths)
            {
                if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath) && !resolvedPaths.Contains(candidatePath, StringComparer.OrdinalIgnoreCase))
                {
                    resolvedPaths.Add(candidatePath);
                }
            }

            if (resolvedPaths.Count == 0)
            {
                throw new FileNotFoundException(
                    $"A compatible Chrome, Edge, or Chromium executable could not be found. Install Chrome or Edge, place the browser beside the renderer executable, or set {BrowserPathEnvironmentVariable}.");
            }

            return resolvedPaths;
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

            if (LooksLikeEdgeLocalSystemIssue(browserExecutablePath, failure))
            {
                message.Append("; note: Edge headless PDF rendering can fail when Integration Soup runs under the LocalSystem service account. Running the service as a dedicated user account or switching HTMLTOPDF_BROWSER_PATH to Chrome is recommended.");
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

        private static bool LooksLikeEdgeLocalSystemIssue(string browserExecutablePath, BrowserRunResult failure)
        {
            if (failure == null)
            {
                return false;
            }

            string browserPath = browserExecutablePath ?? string.Empty;
            if (browserPath.IndexOf("msedge", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            string userName = Environment.UserName ?? string.Empty;
            string domainName = Environment.UserDomainName ?? string.Empty;
            bool isLocalSystem = userName.Equals("SYSTEM", StringComparison.OrdinalIgnoreCase)
                || domainName.Equals("NT AUTHORITY", StringComparison.OrdinalIgnoreCase);

            if (!isLocalSystem)
            {
                return false;
            }

            return !failure.Success
                && string.Equals(failure.Reason, "The browser exited without creating a PDF file.", StringComparison.OrdinalIgnoreCase);
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

        private sealed class BrowserAttempt
        {
            internal BrowserAttempt(string browserPath, string headlessMode)
            {
                BrowserPath = browserPath;
                HeadlessMode = headlessMode;
            }

            internal string BrowserPath { get; }
            internal string HeadlessMode { get; }
        }

        [DataContract]
        private sealed class HtmlToPdfPipeRequest
        {
            [DataMember(Order = 1)]
            public string Html { get; set; }
        }

        [DataContract]
        private sealed class HtmlToPdfPipeResponse
        {
            [DataMember(Order = 1)]
            public string PdfBase64 { get; set; }

            [DataMember(Order = 2)]
            public string BrowserPath { get; set; }

            [DataMember(Order = 3)]
            public string HeadlessMode { get; set; }
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
