using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;
using HL7Soup.Integrations.ExtensionBridge;

namespace Popokey.ExtensionRunners
{
    internal delegate string PersistentRunnerRequestHandler(string operation, string payloadJson);
    // Bridge handlers deliberately receive the unchanged outer-envelope fields.  This
    // lets a runner add schema-versioned v5 operations without changing any v4
    // operation payload or serializer.
    internal delegate string ExtensionBridgeRequestHandler(string operation, string payloadJson, string requestId);

    internal static class PersistentRunnerServer
    {
        internal static int? RunIfRequested(string[] args, PersistentRunnerRequestHandler handler)
        {
            return RunIfRequested(args, handler, null);
        }

        internal static int? RunIfRequested(
            string[] args,
            PersistentRunnerRequestHandler handler,
            ExtensionBridgeRequestHandler bridgeHandler)
        {
            if (!TryParseServerArguments(args, out ServerArguments serverArguments))
            {
                return null;
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return RunServer(serverArguments, handler, bridgeHandler);
        }

        private static int RunServer(
            ServerArguments serverArguments,
            PersistentRunnerRequestHandler handler,
            ExtensionBridgeRequestHandler bridgeHandler)
        {
            // Cancellation must not wait behind an Invoke on the business listener.
            // It is deliberately best-effort: acknowledgement says only that the
            // runner received the request, never that externally-visible work was
            // rolled back or prevented.
            if (serverArguments.BridgeVersion == 1 && bridgeHandler != null)
            {
                Task.Run(() => RunControlServer(serverArguments, bridgeHandler));
            }

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

                        string responsePayload = serverArguments.BridgeVersion == 1 && IsBridgeOperation(requestEnvelope.Operation)
                            ? DispatchBridgeRequest(bridgeHandler, requestEnvelope)
                            : handler(requestEnvelope.Operation, requestEnvelope.PayloadJson ?? string.Empty);
                        responseEnvelope = PersistentRunnerResponseEnvelope.CreateSuccess(requestEnvelope.RequestId, responsePayload);
                    }
                    catch (Exception ex)
                    {
                        responseEnvelope = CreateFailureResponse(requestEnvelope, ex);
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

        private static PersistentRunnerResponseEnvelope CreateFailureResponse(
            PersistentRunnerRequestEnvelope requestEnvelope,
            Exception exception)
        {
            PersistentRunnerResponseEnvelope response = PersistentRunnerResponseEnvelope.CreateFailure(
                requestEnvelope?.RequestId,
                exception.Message);
            if (requestEnvelope != null && IsBridgeOperation(requestEnvelope.Operation))
            {
                response.PayloadJson = PersistentRunnerJson.Serialize(new ExtensionFailure
                {
                    Code = "ExecutionFailed",
                    ExecutionState = "Unknown",
                    Message = exception.Message
                });
            }

            return response;
        }

        private static void RunControlServer(ServerArguments serverArguments, ExtensionBridgeRequestHandler bridgeHandler)
        {
            string controlPipeName = serverArguments.PipeName + ".control";
            while (IsParentProcessAlive(serverArguments.ParentProcessId))
            {
                using (NamedPipeServerStream pipe = new NamedPipeServerStream(
                    controlPipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.None))
                {
                    if (!WaitForConnection(pipe, serverArguments.ParentProcessId))
                    {
                        return;
                    }

                    PersistentRunnerRequestEnvelope requestEnvelope = null;
                    PersistentRunnerResponseEnvelope responseEnvelope;
                    try
                    {
                        requestEnvelope = PersistentRunnerPipeIO.ReadMessage<PersistentRunnerRequestEnvelope>(pipe);
                        ValidateRequest(requestEnvelope);
                        if (!string.Equals(requestEnvelope.Operation, "extension.cancel", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidOperationException("Only extension.cancel is accepted on the control pipe.");
                        }

                        responseEnvelope = PersistentRunnerResponseEnvelope.CreateSuccess(
                            requestEnvelope.RequestId,
                            bridgeHandler(requestEnvelope.Operation, requestEnvelope.PayloadJson ?? string.Empty, requestEnvelope.RequestId));
                    }
                    catch (Exception ex)
                    {
                        responseEnvelope = CreateFailureResponse(requestEnvelope, ex);
                    }

                    try
                    {
                        PersistentRunnerPipeIO.WriteMessage(pipe, responseEnvelope);
                    }
                    catch
                    {
                        // A disconnected control client is not a reason to stop the runner.
                    }
                }
            }
        }

        private static bool IsBridgeOperation(string operation)
        {
            return string.Equals(operation, "extension.describe", StringComparison.OrdinalIgnoreCase)
                || string.Equals(operation, "extension.invoke", StringComparison.OrdinalIgnoreCase)
                || string.Equals(operation, "extension.cancel", StringComparison.OrdinalIgnoreCase);
        }

        private static string DispatchBridgeRequest(
            ExtensionBridgeRequestHandler bridgeHandler,
            PersistentRunnerRequestEnvelope requestEnvelope)
        {
            if (bridgeHandler == null)
            {
                throw new InvalidOperationException(
                    "This runner does not support the version 5 extension bridge.");
            }

            return bridgeHandler(
                requestEnvelope.Operation,
                requestEnvelope.PayloadJson ?? string.Empty,
                requestEnvelope.RequestId);
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
            int bridgeVersion = 0;

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

                if (string.Equals(argument, "--bridge-version", StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
                {
                    if (!int.TryParse(args[++index], out bridgeVersion) || bridgeVersion != 1)
                    {
                        throw new InvalidOperationException("Only --bridge-version 1 is supported.");
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

            serverArguments = new ServerArguments(pipeName, parentProcessId, bridgeVersion);
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
            internal ServerArguments(string pipeName, int parentProcessId, int bridgeVersion)
            {
                PipeName = pipeName;
                ParentProcessId = parentProcessId;
                BridgeVersion = bridgeVersion;
            }

            internal string PipeName { get; }
            internal int ParentProcessId { get; }
            internal int BridgeVersion { get; }
        }
    }
}
