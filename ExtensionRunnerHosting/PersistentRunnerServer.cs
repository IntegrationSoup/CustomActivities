using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace Popokey.ExtensionRunners
{
    internal delegate string PersistentRunnerRequestHandler(string operation, string payloadJson);

    internal static class PersistentRunnerServer
    {
        internal static int? RunIfRequested(string[] args, PersistentRunnerRequestHandler handler)
        {
            if (!TryParseServerArguments(args, out ServerArguments serverArguments))
            {
                return null;
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return RunServer(serverArguments, handler);
        }

        private static int RunServer(ServerArguments serverArguments, PersistentRunnerRequestHandler handler)
        {
            while (IsParentProcessAlive(serverArguments.ParentProcessId))
            {
                using (NamedPipeServerStream pipe = new NamedPipeServerStream(
                    serverArguments.PipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None))
                {
                    if (!WaitForConnection(pipe, serverArguments.ParentProcessId))
                    {
                        return 0;
                    }

                    PersistentRunnerRequestEnvelope requestEnvelope = null;
                    PersistentRunnerResponseEnvelope responseEnvelope;
                    try
                    {
                        requestEnvelope = PersistentRunnerPipeIO.ReadMessage<PersistentRunnerRequestEnvelope>(pipe);
                        ValidateRequest(requestEnvelope);

                        string responsePayload = handler(requestEnvelope.Operation, requestEnvelope.PayloadJson ?? string.Empty);
                        responseEnvelope = PersistentRunnerResponseEnvelope.CreateSuccess(requestEnvelope.RequestId, responsePayload);
                    }
                    catch (Exception ex)
                    {
                        responseEnvelope = PersistentRunnerResponseEnvelope.CreateFailure(
                            requestEnvelope?.RequestId,
                            ex.Message);
                    }

                    try
                    {
                        PersistentRunnerPipeIO.WriteMessage(pipe, responseEnvelope);
                    }
                    catch
                    {
                        // Best-effort only. A broken pipe here simply means the caller has already gone away.
                    }
                }
            }

            return 0;
        }

        private static bool WaitForConnection(NamedPipeServerStream pipe, int parentProcessId)
        {
            Task waitTask = pipe.WaitForConnectionAsync();
            while (true)
            {
                if (waitTask.Wait(500))
                {
                    return true;
                }

                if (!IsParentProcessAlive(parentProcessId))
                {
                    return false;
                }
            }
        }

        private static void ValidateRequest(PersistentRunnerRequestEnvelope requestEnvelope)
        {
            if (requestEnvelope == null)
            {
                throw new InvalidOperationException("The request envelope was empty.");
            }

            if (requestEnvelope.ProtocolVersion != PersistentRunnerProtocol.CurrentVersion)
            {
                throw new InvalidOperationException(
                    $"The request used protocol version '{requestEnvelope.ProtocolVersion}', but version '{PersistentRunnerProtocol.CurrentVersion}' is required.");
            }

            if (string.IsNullOrWhiteSpace(requestEnvelope.RequestId))
            {
                throw new InvalidOperationException("The request did not include a request identifier.");
            }

            if (string.IsNullOrWhiteSpace(requestEnvelope.Operation))
            {
                throw new InvalidOperationException("The request did not include an operation.");
            }
        }

        private static bool TryParseServerArguments(string[] args, out ServerArguments serverArguments)
        {
            serverArguments = null;
            if (args == null || args.Length == 0 || !string.Equals(args[0], "--server", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string pipeName = null;
            int parentProcessId = 0;

            for (int index = 1; index < args.Length; index++)
            {
                string argument = args[index];
                if (string.Equals(argument, "--pipe-name", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    pipeName = args[++index];
                    continue;
                }

                if (string.Equals(argument, "--parent-pid", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    if (!int.TryParse(args[++index], out parentProcessId))
                    {
                        throw new InvalidOperationException("The --parent-pid value must be an integer.");
                    }

                    continue;
                }
            }

            if (string.IsNullOrWhiteSpace(pipeName))
            {
                throw new InvalidOperationException("The --pipe-name argument is required in server mode.");
            }

            if (parentProcessId <= 0)
            {
                throw new InvalidOperationException("The --parent-pid argument is required in server mode.");
            }

            serverArguments = new ServerArguments(pipeName, parentProcessId);
            return true;
        }

        private static bool IsParentProcessAlive(int parentProcessId)
        {
            try
            {
                using (Process parentProcess = Process.GetProcessById(parentProcessId))
                {
                    return !parentProcess.HasExited;
                }
            }
            catch
            {
                return false;
            }
        }

        private sealed class ServerArguments
        {
            internal ServerArguments(string pipeName, int parentProcessId)
            {
                PipeName = pipeName;
                ParentProcessId = parentProcessId;
            }

            internal string PipeName { get; }
            internal int ParentProcessId { get; }
        }
    }
}
