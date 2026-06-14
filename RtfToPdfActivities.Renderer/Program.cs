using Popokey.ExtensionRunners;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace RtfToPdfActivities.Renderer
{
    internal static class Program
    {
        private const string LibreOfficePathEnvironmentVariable = "RTFTOPDF_LIBREOFFICE_PATH";
        private const int LibreOfficeTimeoutMilliseconds = 120000;

        private static readonly object libreOfficePathGate = new object();
        private static string cachedLibreOfficeExecutablePath;

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
                Console.Error.WriteLine("Usage: RtfToPdfRenderer <inputRtfPath> <outputPdfPath>");
                return 2;
            }

            string inputRtfPath = Path.GetFullPath(args[0]);
            string outputPdfPath = Path.GetFullPath(args[1]);
            if (!File.Exists(inputRtfPath))
            {
                Console.Error.WriteLine($"Input RTF file was not found: {inputRtfPath}");
                return 3;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPdfPath) ?? AppDomain.CurrentDomain.BaseDirectory);

            ConvertRtfToPdf(File.ReadAllText(inputRtfPath), includePdfBase64: false, outputPdfPath);
            if (!File.Exists(outputPdfPath))
            {
                Console.Error.WriteLine($"The RTF-to-PDF renderer completed without creating '{outputPdfPath}'.");
                return 1;
            }

            return 0;
        }

        private static string HandleServerRequest(string operation, string payloadJson)
        {
            if (!string.Equals(operation, "convert-rtf-to-pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Unsupported RTF-to-PDF operation '{operation}'.");
            }

            RtfToPdfPipeRequest request = PersistentRunnerJson.Deserialize<RtfToPdfPipeRequest>(payloadJson);
            RtfToPdfPipeResponse response = ConvertRtfToPdf(request?.Rtf ?? string.Empty, includePdfBase64: true, outputPdfPath: null);
            return PersistentRunnerJson.Serialize(response);
        }

        private static RtfToPdfPipeResponse ConvertRtfToPdf(string rtf, bool includePdfBase64, string outputPdfPath)
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "RtfToPdfActivities.Renderer", Guid.NewGuid().ToString("N"));
            string inputRtfPath = Path.Combine(tempRoot, "input.rtf");
            string resolvedOutputPdfPath = outputPdfPath ?? Path.Combine(tempRoot, "output.pdf");

            Directory.CreateDirectory(tempRoot);
            try
            {
                File.WriteAllText(
                    inputRtfPath,
                    string.IsNullOrWhiteSpace(rtf)
                        ? @"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 \par}"
                        : rtf,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                ConversionRunResult result = ConvertRtfToPdfInternal(inputRtfPath, resolvedOutputPdfPath);
                if (!result.Success)
                {
                    throw new InvalidOperationException(FormatLibreOfficeFailure(result.LibreOfficeExecutablePath, result));
                }

                if (!File.Exists(resolvedOutputPdfPath))
                {
                    throw new InvalidOperationException($"LibreOffice completed without creating '{resolvedOutputPdfPath}'.");
                }

                return new RtfToPdfPipeResponse
                {
                    LibreOfficePath = result.LibreOfficeExecutablePath,
                    PdfBase64 = includePdfBase64 ? Convert.ToBase64String(File.ReadAllBytes(resolvedOutputPdfPath)) : string.Empty
                };
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static ConversionRunResult ConvertRtfToPdfInternal(string inputRtfPath, string outputPdfPath)
        {
            string libreOfficeExecutablePath = ResolveLibreOfficeExecutablePath();
            string tempRoot = Path.Combine(Path.GetTempPath(), "RtfToPdfActivities.Renderer.Convert", Guid.NewGuid().ToString("N"));
            string profilePath = Path.Combine(tempRoot, "profile");
            string outputDirectory = Path.Combine(tempRoot, "output");
            string expectedGeneratedPdfPath = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(inputRtfPath) + ".pdf");

            Directory.CreateDirectory(tempRoot);
            Directory.CreateDirectory(outputDirectory);
            try
            {
                ConversionRunResult result = RunLibreOfficeConvert(
                    libreOfficeExecutablePath,
                    profilePath,
                    inputRtfPath,
                    outputDirectory);

                if (!result.Success)
                {
                    return result;
                }

                if (!File.Exists(expectedGeneratedPdfPath))
                {
                    return ConversionRunResult.Failed(
                        libreOfficeExecutablePath,
                        result.ExitCode,
                        result.StandardOutput,
                        result.StandardError,
                        "LibreOffice exited without creating a PDF file.");
                }

                File.Copy(expectedGeneratedPdfPath, outputPdfPath, overwrite: true);
                return result;
            }
            finally
            {
                TryDeleteDirectory(tempRoot);
            }
        }

        private static ConversionRunResult RunLibreOfficeConvert(
            string libreOfficeExecutablePath,
            string profilePath,
            string inputRtfPath,
            string outputDirectory)
        {
            Directory.CreateDirectory(profilePath);

            ProcessStartInfo startInfo = new ProcessStartInfo
            {
                FileName = libreOfficeExecutablePath,
                Arguments = BuildLibreOfficeArguments(profilePath, inputRtfPath, outputDirectory),
                WorkingDirectory = outputDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (Process process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return ConversionRunResult.Failed(
                        libreOfficeExecutablePath,
                        -1,
                        string.Empty,
                        "The LibreOffice process could not be started.",
                        "LibreOffice process start failed.");
                }

                Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
                Task<string> stderrTask = process.StandardError.ReadToEndAsync();

                if (!process.WaitForExit(LibreOfficeTimeoutMilliseconds))
                {
                    TryKillProcess(process);
                    Task.WaitAll(stdoutTask, stderrTask);
                    return ConversionRunResult.Failed(
                        libreOfficeExecutablePath,
                        null,
                        SafeGetTaskResult(stdoutTask),
                        SafeGetTaskResult(stderrTask),
                        $"LibreOffice did not finish within {LibreOfficeTimeoutMilliseconds / 1000} seconds.");
                }

                Task.WaitAll(stdoutTask, stderrTask);

                string stdout = SafeGetTaskResult(stdoutTask);
                string stderr = SafeGetTaskResult(stderrTask);
                if (process.ExitCode == 0)
                {
                    return ConversionRunResult.Succeeded(libreOfficeExecutablePath, process.ExitCode, stdout, stderr);
                }

                return ConversionRunResult.Failed(
                    libreOfficeExecutablePath,
                    process.ExitCode,
                    stdout,
                    stderr,
                    "LibreOffice exited with an error.");
            }
        }

        private static string BuildLibreOfficeArguments(string profilePath, string inputRtfPath, string outputDirectory)
        {
            string[] arguments =
            {
                "--headless",
                "--nologo",
                "--nodefault",
                "--norestore",
                "--nolockcheck",
                "-env:UserInstallation=" + new Uri(profilePath + Path.DirectorySeparatorChar).AbsoluteUri,
                "--convert-to",
                "pdf:writer_pdf_Export",
                "--outdir",
                outputDirectory,
                inputRtfPath
            };

            return string.Join(" ", Array.ConvertAll(arguments, PersistentRunnerCommandLine.QuoteArgument));
        }

        private static string SafeGetTaskResult(Task<string> task)
        {
            return task.Status == TaskStatus.RanToCompletion ? task.Result : string.Empty;
        }

        private static string ResolveLibreOfficeExecutablePath()
        {
            string configuredPath = Environment.GetEnvironmentVariable(LibreOfficePathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                string fullConfiguredPath = Path.GetFullPath(configuredPath);
                if (File.Exists(fullConfiguredPath))
                {
                    return fullConfiguredPath;
                }

                throw new FileNotFoundException(
                    $"LibreOffice configured by {LibreOfficePathEnvironmentVariable} was not found at '{fullConfiguredPath}'.",
                    fullConfiguredPath);
            }

            lock (libreOfficePathGate)
            {
                if (!string.IsNullOrWhiteSpace(cachedLibreOfficeExecutablePath) && File.Exists(cachedLibreOfficeExecutablePath))
                {
                    return cachedLibreOfficeExecutablePath;
                }

                string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                string[] candidatePaths =
                {
                    Path.Combine(programFiles, "LibreOffice", "program", "soffice.com"),
                    Path.Combine(programFiles, "LibreOffice", "program", "soffice.exe"),
                    Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.com"),
                    Path.Combine(programFilesX86, "LibreOffice", "program", "soffice.exe")
                };

                foreach (string candidatePath in candidatePaths)
                {
                    if (!string.IsNullOrEmpty(candidatePath) && File.Exists(candidatePath))
                    {
                        cachedLibreOfficeExecutablePath = candidatePath;
                        return candidatePath;
                    }
                }
            }

            throw new FileNotFoundException(
                $"A compatible LibreOffice executable could not be found. Install LibreOffice, or set {LibreOfficePathEnvironmentVariable} to soffice.com or soffice.exe.");
        }

        private static string FormatLibreOfficeFailure(string libreOfficeExecutablePath, ConversionRunResult result)
        {
            StringBuilder message = new StringBuilder();
            message.Append("LibreOffice path: ").Append(libreOfficeExecutablePath);

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
        private sealed class RtfToPdfPipeRequest
        {
            [DataMember(Order = 1)]
            public string Rtf { get; set; }
        }

        [DataContract]
        private sealed class RtfToPdfPipeResponse
        {
            [DataMember(Order = 1)]
            public string PdfBase64 { get; set; }

            [DataMember(Order = 2)]
            public string LibreOfficePath { get; set; }
        }

        private sealed class ConversionRunResult
        {
            private ConversionRunResult(string libreOfficeExecutablePath, int? exitCode, string standardOutput, string standardError, string reason, bool success)
            {
                LibreOfficeExecutablePath = libreOfficeExecutablePath;
                ExitCode = exitCode;
                StandardOutput = standardOutput ?? string.Empty;
                StandardError = standardError ?? string.Empty;
                Reason = reason ?? string.Empty;
                Success = success;
            }

            public string LibreOfficeExecutablePath { get; }
            public int? ExitCode { get; }
            public string StandardOutput { get; }
            public string StandardError { get; }
            public string Reason { get; }
            public bool Success { get; }

            public static ConversionRunResult Succeeded(string libreOfficeExecutablePath, int exitCode, string standardOutput, string standardError)
            {
                return new ConversionRunResult(libreOfficeExecutablePath, exitCode, standardOutput, standardError, string.Empty, success: true);
            }

            public static ConversionRunResult Failed(string libreOfficeExecutablePath, int? exitCode, string standardOutput, string standardError, string reason = null)
            {
                return new ConversionRunResult(libreOfficeExecutablePath, exitCode, standardOutput, standardError, reason, success: false);
            }
        }
    }
}
