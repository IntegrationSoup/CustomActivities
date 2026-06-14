using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Popokey.ExtensionRunners
{
    internal sealed class PersistentRunnerClient
    {
        private const int StartupTimeoutMilliseconds = 15000;

        private readonly object processGate = new object();
        // Each extension runner handles one request at a time. Callers block here,
        // including while queued behind earlier work, until their own response arrives.
        private readonly SemaphoreSlim requestGate = new SemaphoreSlim(1, 1);
        private readonly string runnerDescription;

        private Process process;
        private string executablePath;
        private string pipeName;

        internal PersistentRunnerClient(string runnerDescription)
        {
            this.runnerDescription = runnerDescription ?? "runner";
        }

        internal TResponse Invoke<TRequest, TResponse>(string runnerExecutablePath, string operation, TRequest request, int timeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(runnerExecutablePath))
            {
                throw new ArgumentException("A runner executable path is required.", nameof(runnerExecutablePath));
            }

            if (string.IsNullOrWhiteSpace(operation))
            {
                throw new ArgumentException("An operation name is required.", nameof(operation));
            }

            requestGate.Wait();
            try
            {
                bool canRetryBeforeDispatch = true;
                while (true)
                {
                    EnsureRunnerStarted(runnerExecutablePath);

                    bool dispatched = false;
                    try
                    {
                        using (NamedPipeClientStream pipe = ConnectToRunner(timeoutMilliseconds))
                        {
                            PersistentRunnerRequestEnvelope requestEnvelope = new PersistentRunnerRequestEnvelope
                            {
                                RequestId = Guid.NewGuid().ToString("N"),
                                Operation = operation,
                                PayloadJson = PersistentRunnerJson.Serialize(request)
                            };

                            dispatched = true;
                            PersistentRunnerPipeIO.WriteMessage(pipe, requestEnvelope);

                            PersistentRunnerResponseEnvelope responseEnvelope = ReadResponse(pipe, timeoutMilliseconds);
                            ValidateResponse(responseEnvelope, requestEnvelope.RequestId);
                            return DeserializePayload<TResponse>(responseEnvelope.PayloadJson);
                        }
                    }
                    catch (Exception ex) when (ShouldRetryBeforeDispatch(ex, dispatched, canRetryBeforeDispatch))
                    {
                        InvalidateProcess(killIfRunning: true);
                        canRetryBeforeDispatch = false;
                    }
                    catch
                    {
                        if (dispatched)
                        {
                            InvalidateProcess(killIfRunning: true);
                        }

                        throw;
                    }
                }
            }
            finally
            {
                requestGate.Release();
            }
        }

        private void EnsureRunnerStarted(string requestedExecutablePath)
        {
            lock (processGate)
            {
                if (!string.Equals(executablePath, requestedExecutablePath, StringComparison.OrdinalIgnoreCase))
                {
                    InvalidateProcessNoLock(killIfRunning: true);
                    executablePath = requestedExecutablePath;
                    pipeName = BuildPipeName(requestedExecutablePath);
                }
                else if (process != null && !SafeHasExited(process))
                {
                    return;
                }
                else
                {
                    InvalidateProcessNoLock(killIfRunning: false);
                }

                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = requestedExecutablePath,
                    Arguments = PersistentRunnerCommandLine.BuildServerArguments(pipeName, Process.GetCurrentProcess().Id),
                    WorkingDirectory = Path.GetDirectoryName(requestedExecutablePath) ?? AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                process = Process.Start(startInfo);
                if (process == null)
                {
                    throw new InvalidOperationException($"The {runnerDescription} process could not be started.");
                }
            }
        }

        private NamedPipeClientStream ConnectToRunner(int timeoutMilliseconds)
        {
            string currentPipeName;
            lock (processGate)
            {
                currentPipeName = pipeName;
            }

            NamedPipeClientStream pipe = new NamedPipeClientStream(
                ".",
                currentPipeName,
                PipeDirection.InOut,
                PipeOptions.None);

            try
            {
                int connectTimeout = Math.Min(Math.Max(timeoutMilliseconds, 1), StartupTimeoutMilliseconds);
                pipe.Connect(connectTimeout);
                return pipe;
            }
            catch
            {
                pipe.Dispose();
                throw;
            }
        }

        private PersistentRunnerResponseEnvelope ReadResponse(NamedPipeClientStream pipe, int timeoutMilliseconds)
        {
            Task<PersistentRunnerResponseEnvelope> responseTask = Task.Run(() => PersistentRunnerPipeIO.ReadMessage<PersistentRunnerResponseEnvelope>(pipe));
            if (!responseTask.Wait(timeoutMilliseconds))
            {
                throw new TimeoutException($"The {runnerDescription} did not respond within {timeoutMilliseconds / 1000} seconds.");
            }

            return responseTask.Result;
        }

        private static void ValidateResponse(PersistentRunnerResponseEnvelope responseEnvelope, string requestId)
        {
            if (responseEnvelope == null)
            {
                throw new InvalidOperationException("The runner returned an empty response.");
            }

            if (responseEnvelope.ProtocolVersion != PersistentRunnerProtocol.CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"The runner responded with protocol version '{responseEnvelope.ProtocolVersion}', but version '{PersistentRunnerProtocol.CurrentVersion}' was expected.");
            }

            if (!string.Equals(responseEnvelope.RequestId, requestId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The runner response did not match the active request.");
            }

            if (!responseEnvelope.Success)
            {
                throw new InvalidOperationException(responseEnvelope.ErrorMessage ?? "The runner reported a failure.");
            }
        }

        private static T DeserializePayload<T>(string payloadJson)
        {
            if (typeof(T) == typeof(string))
            {
                object value = payloadJson ?? string.Empty;
                return (T)value;
            }

            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return default(T);
            }

            return PersistentRunnerJson.Deserialize<T>(payloadJson);
        }

        private static bool ShouldRetryBeforeDispatch(Exception ex, bool dispatched, bool canRetryBeforeDispatch)
        {
            if (dispatched || !canRetryBeforeDispatch)
            {
                return false;
            }

            return ex is IOException
                || ex is EndOfStreamException
                || ex is TimeoutException;
        }

        private void InvalidateProcess(bool killIfRunning)
        {
            lock (processGate)
            {
                InvalidateProcessNoLock(killIfRunning);
            }
        }

        private void InvalidateProcessNoLock(bool killIfRunning)
        {
            if (process != null)
            {
                try
                {
                    if (killIfRunning && !SafeHasExited(process))
                    {
                        process.Kill();
                    }
                }
                catch
                {
                    // Best-effort cleanup only.
                }
                finally
                {
                    process.Dispose();
                    process = null;
                }
            }
        }

        private static bool SafeHasExited(Process candidate)
        {
            try
            {
                return candidate.HasExited;
            }
            catch
            {
                return true;
            }
        }

        private static string BuildPipeName(string runnerExecutablePath)
        {
            string fullPath = Path.GetFullPath(runnerExecutablePath ?? string.Empty);
            string hash;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(fullPath);
                byte[] digest = sha.ComputeHash(bytes);
                StringBuilder builder = new StringBuilder(24);
                for (int index = 0; index < 12; index++)
                {
                    builder.Append(digest[index].ToString("x2"));
                }

                hash = builder.ToString();
            }

            return "IntegrationSoup."
                + Process.GetCurrentProcess().Id
                + "."
                + hash;
        }
    }
}
