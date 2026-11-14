using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using HL7Soup.Integrations.ExtensionBridge;

namespace Popokey.ExtensionRunners
{
    internal static class BridgeRunner
    {
        internal static int? RunIfRequested(string[] args, PersistentRunnerRequestHandler legacy, Func<BridgeProvider> createProvider)
        {
            if (!args.Any(a => string.Equals(a, "--bridge-version", StringComparison.OrdinalIgnoreCase)))
                return PersistentRunnerServer.RunIfRequested(args, legacy);
            if (args.Length == 0 || args[0] != "--server") throw new ArgumentException("Bridge mode requires --server.");
            string pipeName = null;
            int parentPid = 0, version = 0, limit = ExtensionBridgeProtocol.DefaultMaxMessageBytes;
            var seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length || !seen.Add(args[i])) throw new ArgumentException("Missing or duplicate bridge argument.");
                switch (args[i])
                {
                    case "--pipe-name": pipeName = args[i + 1]; break;
                    case "--parent-pid": parentPid = int.Parse(args[i + 1]); break;
                    case "--bridge-version": version = int.Parse(args[i + 1]); break;
                    case "--max-message-bytes": limit = int.Parse(args[i + 1]); break;
                    default: throw new ArgumentException("Unknown bridge launch argument.");
                }
            }
            if (version != 1 || parentPid <= 0 || limit <= 0 || limit > ExtensionBridgeProtocol.MaximumMessageBytes)
                throw new ArgumentException("Invalid bridge version, parent or limit.");
            if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 200 || (!IsUnixSocketPath(pipeName) && pipeName.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.'))))
                throw new ArgumentException("A local pipe name without path components is required.");
            using (Process parent = Process.GetProcessById(parentPid))
            using (var monitor = new Timer(_ => { try { if (parent.HasExited) Environment.Exit(0); } catch { Environment.Exit(0); } }, null, 0, 500))
            {
                BridgeProvider provider = createProvider();
                // On Unix a pipe is a socket that goes away with its last listener, and a caller that is
                // already waiting on it is then cut off. One listener for each pipe is held open and never
                // used, the control pipe first, so that both are there before the first caller is answered.
                using (HoldOpen(pipeName + ".control"))
                using (HoldOpen(pipeName))
                {
                    var control = Task.Run(() => Listen(pipeName + ".control", provider, Math.Min(limit, ExtensionBridgeProtocol.MaximumDescribeBytes), true));
                    control.ContinueWith(_ => Environment.Exit(1), TaskContinuationOptions.OnlyOnFaulted);
                    Listen(pipeName, provider, limit, false);
                    return 0;
                }
            }
        }

        // A Unix host supplies the full path of a socket inside a private directory that it has just made.
        private static bool IsUnixSocketPath(string pipeName)
        {
#if NETFRAMEWORK
            return false;
#else
            return !OperatingSystem.IsWindows() && pipeName.IndexOf('\0') < 0 && Path.IsPathFullyQualified(pipeName)
                && string.Equals(Path.GetFullPath(pipeName), pipeName, StringComparison.Ordinal) && Path.GetFileName(pipeName).Length > 0;
#endif
        }

        private static NamedPipeServerStream HoldOpen(string pipeName)
        {
#if NETFRAMEWORK
            return null;
#else
            return OperatingSystem.IsWindows() ? null : CreatePipe(pipeName);
#endif
        }

#if !NETFRAMEWORK
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
#endif
        internal static PipeSecurity CreateSecurity()
        {
            var security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            // Network logons must not turn this local endpoint into a remote pipe.
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }

        private static void Listen(string pipeName, BridgeProvider provider, int limit, bool control)
        {
            while (true)
            {
                using (var pipe = CreatePipe(pipeName))
                {
                    try
                    {
                        // A client can disconnect between opening this pipe and
                        // WaitForConnection completing. Recreate the listener then too.
                        pipe.WaitForConnection();
                        byte[] input = Bounded(() => BridgeWire.ReadFrame(pipe, limit), pipe);
                        byte[] output = BridgeWire.DispatchJson(input, provider, limit, control);
                        Bounded(() => { BridgeWire.WriteFrame(pipe, output, limit); return true; }, pipe);
                    }
                    catch (IOException) { }
                    catch (TimeoutException) { }
                    catch (AggregateException) { }
#if !NETFRAMEWORK
                    // On Unix a caller that is another user is refused when it connects. That must not end the runner.
                    catch (UnauthorizedAccessException) when (!OperatingSystem.IsWindows()) { }
#endif
                }
            }
        }

        private static NamedPipeServerStream CreatePipe(string pipeName)
        {
#if NETFRAMEWORK
            return new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, CreateSecurity());
#else
            // Unix has no pipe access rules. The pipe is a socket that only this user may use.
            if (!OperatingSystem.IsWindows())
                return new NamedPipeServerStream(pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 4096, 4096);
            return NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, CreateSecurity());
#endif
        }

        private static T Bounded<T>(Func<T> action, Stream stream)
        {
            var task = Task.Run(action);
            if (!task.Wait(30000))
            {
                stream.Dispose();
                task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                throw new TimeoutException("Bridge frame I/O deadline elapsed.");
            }
            return task.GetAwaiter().GetResult();
        }
    }
}
