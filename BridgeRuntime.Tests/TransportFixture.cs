using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;

// Synthetic transport hosts only. This is not an externally exposed HTTP server.
internal static class TransportFixture
{
    private const int Limit = 65536;
    internal static void Run()
    {
        DeadlineAndCancel();
        if (OperatingSystem.IsWindows()) { Run("bridge-transport-fixture-" + Guid.NewGuid().ToString("N")); return; }
        // A Unix host passes the full path of a socket in a directory that only its own user can open.
        string directory = Directory.CreateTempSubdirectory("bridge-").FullName;
        try { Run(Path.Combine(directory, "pipe")); }
        finally { Directory.Delete(directory, true); }
    }

    private static void Run(string name)
    {
        using (Process child = Start("--server", "--pipe-name", name, "--parent-pid", Environment.ProcessId.ToString(), "--bridge-version", "1"))
        {
            try
            {
                using (var malformed = Connect(name)) { malformed.Write(new byte[4]); malformed.Flush(); }
                var described = Pipe(name, Envelope("extension.describe", new ExtensionDescribeRequest { ProviderId = "fixture" }));
                Check(described.Success, "runner survives invalid frame");
                // A Unix pipe is a socket that goes away with its last listener, and a caller that is
                // already waiting on it is then cut off. Callers that arrive together must all be answered.
                if (!OperatingSystem.IsWindows())
                {
                    var callers = new Task<ExtensionResponseEnvelope>[32];
                    for (int i = 0; i < callers.Length; i++)
                        callers[i] = Task.Run(() => Pipe(name, Envelope("extension.describe", new ExtensionDescribeRequest { ProviderId = "fixture" })));
                    foreach (Task<ExtensionResponseEnvelope> caller in callers)
                        Check(caller.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult().Success, "a waiting caller keeps its listener");
                    RefusedCaller(name, child);
                }
                string type = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(described.PayloadJson).Extensions[0].TypeName;
                var request = new ExtensionInvokeRequest { ProviderId = "fixture", TypeName = type, Kind = "Activity", Phase = "Prepare", SessionId = "fixture-session", DeadlineUtc = DateTime.UtcNow.AddSeconds(15).ToString("O"), ResponseMessage = new ExtensionMessage { MessageType = 13, Text = "" } };
                Check(Pipe(name, Envelope("extension.invoke", request)).Success, "pipe Prepare");
                request.Phase = "Process";
                var work = Envelope("extension.invoke", request);
                Task<ExtensionResponseEnvelope> pending = Task.Run(() => Pipe(name, work));
                Check(child.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult() == "PROBE_STARTED", "business started");
                var timer = Stopwatch.StartNew();
                var cancel = Pipe(name + ".control", Envelope("extension.cancel", new ExtensionCancelRequest { ProviderId = "fixture", TargetRequestId = work.RequestId, SessionId = request.SessionId }));
                Check(cancel.Success && timer.ElapsedMilliseconds < 700, "actual .control listener bypasses busy business pipe");
                var failure = PersistentRunnerJson.Deserialize<ExtensionFailure>(pending.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult().PayloadJson);
                Check(failure.Code == "Cancelled" && failure.ExecutionState == "Unknown", "cancel discards late pipe result");
            }
            finally { Stop(child); }
        }
        using (Process parent = Start("--fixture-parent"))
        using (Process orphan = Start("--server", "--pipe-name", name, "--parent-pid", parent.Id.ToString(), "--bridge-version", "1"))
        {
            try
            {
                using (var partial = Connect(name))
                {
                    partial.WriteByte(100); partial.Flush();
                    Stop(parent);
                    Check(orphan.WaitForExit(4000), "parent death ends runner during incomplete frame");
                }
            }
            finally { Stop(orphan); Stop(parent); }
        }
        TestHttp().GetAwaiter().GetResult();
        Console.WriteLine("Transport fixtures PASS: real .control cancellation, malformed-frame recovery, parent death during I/O, loopback HTTP auth rejection and identical JSON dispatch.");
    }

    // What an ended call reports must not depend on how fast it unwinds. The probe stops the moment
    // its call is cancelled, which is when the clock is closest to the deadline. No pipe is involved.
    private static void DeadlineAndCancel()
    {
        var provider = new BridgeProvider("fixture", "Fixture", (operation, payload) => throw new NotSupportedException(), typeof(StopsWhenCancelled));
        var described = provider.Dispatch(Envelope("extension.describe", new ExtensionDescribeRequest { ProviderId = "fixture" }));
        string type = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(described.PayloadJson).Extensions[0].TypeName;
        // First with the provider's clock held still. The clock then never reaches a deadline, as when a
        // timer fires early, so only the timer can say that the deadline ended a call.
        long held = DateTimeOffset.UtcNow.UtcTicks;
        provider.Clock = () => new DateTimeOffset(Volatile.Read(ref held), TimeSpan.Zero);
        for (int i = 0; i < 3; i++)
        {
            ExtensionFailure early = Ended(provider, type, "held-deadline-" + i, TimeSpan.FromMilliseconds(150), false, 0);
            Check(early.Code == "DeadlineExceeded" && early.ExecutionState == "Unknown", "a call its timer ended before the clock reached the deadline reports DeadlineExceeded, not " + early.Code);
        }
        // A cancel comes first and the timer fires while the call unwinds. The cancel ended the call.
        for (int i = 0; ; i++)
        {
            Check(i < 5, "a cancel lands before the timer fires");
            ExtensionFailure first = Ended(provider, type, "held-cancel-" + i, TimeSpan.FromMilliseconds(150), true, 600);
            // A machine that stalled until the timer had fired before the cancel landed has shown nothing.
            if (cancelLanded >= TimeSpan.FromMilliseconds(140)) continue;
            Check(first.Code == "Cancelled" && first.ExecutionState == "Unknown", "a call a cancel ended before the clock reached the deadline reports Cancelled, not " + first.Code);
            break;
        }
        // A cancel comes first with the clock already past the deadline. The clock decides.
        ExtensionFailure outlived = Ended(provider, type, "held-both", TimeSpan.FromSeconds(20), true, 0,
            () => Volatile.Write(ref held, Volatile.Read(ref held) + TimeSpan.FromSeconds(21).Ticks));
        Check(outlived.Code == "DeadlineExceeded" && outlived.ExecutionState == "Unknown", "a cancelled call that outlived its deadline on the clock reports DeadlineExceeded, not " + outlived.Code);
        // Then with the real clock.
        provider.Clock = () => DateTimeOffset.UtcNow;
        // The deadline ends the call. A timer cuts the fraction of a millisecond off its wait.
        for (int i = 0, ended = 0; ended < 40; i++)
        {
            Check(i < 80, "calls begin before their deadline");
            ExtensionFailure late = Ended(provider, type, "deadline-" + i, TimeSpan.FromMilliseconds(150.9), false, 0);
            // A machine that stalled past the deadline before the call began has shown nothing.
            if (late.ExecutionState == "NotStarted") continue;
            Check(late.Code == "DeadlineExceeded" && late.ExecutionState == "Unknown", "a call ended by its deadline reports DeadlineExceeded, not " + late.Code);
            ended++;
        }
        // A cancel ends the call long before its deadline.
        ExtensionFailure cancelled = Ended(provider, type, "cancel", TimeSpan.FromSeconds(20), true, 0);
        Check(cancelled.Code == "Cancelled" && cancelled.ExecutionState == "Unknown", "a cancelled call reports Cancelled, not " + cancelled.Code);
        // A cancel comes first, and the call is still unwinding when its deadline passes.
        ExtensionFailure both = Ended(provider, type, "both", TimeSpan.FromSeconds(1), true, 1600);
        Check(both.Code == "DeadlineExceeded" && both.ExecutionState == "Unknown", "a call that outlives its deadline reports DeadlineExceeded, not " + both.Code);
        Console.WriteLine("Deadline and cancel fixture PASS: with the clock held still, 3 calls ended by their timer, one by a cancel, one by a cancel past its deadline. With the real clock, 40 calls ended by their deadline, one by a cancel, one by a cancel and then its deadline.");
    }

    // How long after a call was handed over its cancel was acknowledged.
    private static TimeSpan cancelLanded;

    // Deadlines are measured on the provider's clock. whenStarted runs once the probe is waiting, before the cancel.
    private static ExtensionFailure Ended(BridgeProvider provider, string type, string session, TimeSpan deadline, bool cancel, int unwindMilliseconds, Action whenStarted = null)
    {
        var request = new ExtensionInvokeRequest { ProviderId = "fixture", TypeName = type, Kind = "Activity", Phase = "Prepare", SessionId = session, DeadlineUtc = provider.Clock().AddSeconds(15).UtcDateTime.ToString("O"), ResponseMessage = new ExtensionMessage { MessageType = 13, Text = "" } };
        Check(provider.Dispatch(Envelope("extension.invoke", request)).Success, "in-process Prepare");
        request.Phase = "Process";
        StopsWhenCancelled.Started.Reset();
        StopsWhenCancelled.UnwindMilliseconds = unwindMilliseconds;
        request.DeadlineUtc = provider.Clock().Add(deadline).UtcDateTime.ToString("O");
        var work = Envelope("extension.invoke", request);
        var handedOver = Stopwatch.StartNew();
        Task<ExtensionResponseEnvelope> pending = Task.Run(() => provider.Dispatch(work));
        if (cancel)
        {
            Check(StopsWhenCancelled.Started.Wait(5000), "the probe started");
            if (whenStarted != null) whenStarted();
            Check(provider.Dispatch(Envelope("extension.cancel", new ExtensionCancelRequest { ProviderId = "fixture", TargetRequestId = work.RequestId, SessionId = session }), true).Success, "cancel acknowledged");
            cancelLanded = handedOver.Elapsed;
        }
        return PersistentRunnerJson.Deserialize<ExtensionFailure>(pending.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult().PayloadJson);
    }

    public sealed class StopsWhenCancelled : CustomActivity
    {
        internal static readonly ManualResetEventSlim Started = new ManualResetEventSlim();
        internal static int UnwindMilliseconds;
        public override void Process(IWorkflowInstance workflow, IActivityInstance activity, Dictionary<string, string> parameters)
        {
            CancellationToken ended = SourceAdapterDispatch.Cancellation;
            Started.Set();
            ended.WaitHandle.WaitOne();
            Thread.Sleep(UnwindMilliseconds);
            throw new OperationCanceledException(ended);
        }
    }

    // On Unix a caller that is another user is refused when it connects, and the runner must go on
    // answering on both pipes. Another user means sudo here, so this part runs for an ordinary user
    // whom sudo asks nothing. BRIDGE_FIXTURE_OTHER_USER=required turns a skip into a failure.
    private static void RefusedCaller(string name, Process child)
    {
        if (Environment.IsPrivilegedProcess || Sudo("true") != 0)
        {
            Check(Environment.GetEnvironmentVariable("BRIDGE_FIXTURE_OTHER_USER") != "required", "another user needs an ordinary user who may use sudo without a password");
            Console.WriteLine("Refused-caller fixture SKIPPED: it needs an ordinary user who may use sudo without a password.");
            return;
        }
        var probe = new System.Collections.Generic.List<string>();
        // sudo drops the environment; a runtime outside the default place is found through DOTNET_ROOT.
        string root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(root)) { probe.Add("env"); probe.Add("DOTNET_ROOT=" + root); }
        probe.Add(Environment.ProcessPath);
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            probe.Add(System.Reflection.Assembly.GetEntryAssembly().Location);
        probe.Add("--fixture-refused-probe");
        foreach (string pipe in new[] { name, name + ".control" })
        {
            string label = Path.GetFileName(pipe);
            probe.Add(pipe);
            int exit = Sudo(probe.ToArray());
            probe.RemoveAt(probe.Count - 1);
            Check(exit == 0, "another user gets no answer on " + label + " (probe exit " + exit + ")");
            Check(!child.HasExited, "runner survives refusing another user on " + label);
            // The control pipe takes cancellations only; one that names nothing is still acknowledged.
            var own = pipe == name ? Envelope("extension.describe", new ExtensionDescribeRequest { ProviderId = "fixture" })
                : Envelope("extension.cancel", new ExtensionCancelRequest { ProviderId = "fixture", TargetRequestId = "none", SessionId = "none" });
            Check(Pipe(pipe, own).Success, "runner still answers its own user on " + label);
        }
        Console.WriteLine("Refused-caller fixture PASS: another user got no answer on either pipe, and the runner went on answering its own user.");
    }

    // Started through sudo by RefusedCaller. 0: the runner gave this user no answer. 3: it answered. 4: the socket was not reached.
    internal static int RefusedProbe(string path)
    {
        using (var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified) { ReceiveTimeout = 5000, SendTimeout = 5000 })
        {
            try { socket.Connect(new UnixDomainSocketEndPoint(path)); }
            catch (SocketException) { return 4; }
            try
            {
                using (var stream = new NetworkStream(socket))
                {
                    BridgeWire.WriteFrame(stream, Encoding.UTF8.GetBytes(PersistentRunnerJson.Serialize(Envelope("extension.describe", new ExtensionDescribeRequest { ProviderId = "fixture" }))), Limit);
                    BridgeWire.ReadFrame(stream, Limit);
                }
                return 3;
            }
            catch (IOException) { return 0; }
        }
    }

    // The exit code of "sudo -n <command>", or -1 when there is no sudo or it did not finish.
    private static int Sudo(params string[] command)
    {
        var info = new ProcessStartInfo("sudo") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        info.ArgumentList.Add("-n");
        foreach (string part in command) info.ArgumentList.Add(part);
        try
        {
            using (Process process = Process.Start(info))
            {
                process.StandardError.ReadToEndAsync();
                if (process.WaitForExit(20000)) return process.ExitCode;
                process.Kill();
                return -1;
            }
        }
        catch (System.ComponentModel.Win32Exception) { return -1; }
    }

    private static async Task TestHttp()
    {
        var provider = new BridgeProvider("fixture", "Fixture", (op,json)=>throw new NotSupportedException(), typeof(RuntimeTests.Probe));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        string token = Guid.NewGuid().ToString("N");
        var address = new Uri("http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/");
        try
        {
            Task server = Task.Run(async () =>
            {
                for (int i = 0; i < 2; i++)
                using (TcpClient connection = await listener.AcceptTcpClientAsync())
                {
                    connection.ReceiveTimeout = 5000; connection.SendTimeout = 5000;
                    using NetworkStream stream = connection.GetStream();
                    var header = new StringBuilder();
                    while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    {
                        int next = stream.ReadByte();
                        if (next < 0 || header.Length > 16384) throw new IOException("Invalid fixture HTTP header");
                        header.Append((char)next);
                    }
                    bool authorized = false; int length = -1;
                    foreach (string line in header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None))
                    {
                        if (line.Equals("Authorization: Bearer " + token, StringComparison.Ordinal)) authorized = true;
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) length = int.Parse(line.Substring(15).Trim());
                    }
                    if (length < 1 || length > Limit) throw new IOException("Invalid fixture HTTP body length");
                    byte[] bytes = new byte[length]; stream.ReadExactly(bytes);
                    byte[] result = authorized ? BridgeWire.DispatchJson(bytes, provider, Limit) : Encoding.UTF8.GetBytes("Unauthorized");
                    byte[] prefix = Encoding.ASCII.GetBytes("HTTP/1.1 " + (authorized ? "200 OK" : "401 Unauthorized") + "\r\nContent-Type: application/json; charset=utf-8\r\nConnection: close\r\nContent-Length: " + result.Length + "\r\n\r\n");
                    await stream.WriteAsync(prefix); await stream.WriteAsync(result);
                }
            });
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };
            string json = PersistentRunnerJson.Serialize(Envelope("extension.describe", new ExtensionDescribeRequest { ProviderId = "fixture" }));
            using var denied = await client.PostAsync(address, new StringContent(json, Encoding.UTF8, "application/json"));
            Check(denied.StatusCode == HttpStatusCode.Unauthorized, "HTTP auth happens before dispatch");
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
            using var accepted = await client.PostAsync(address, new StringContent(json, Encoding.UTF8, "application/json"));
            var response = PersistentRunnerJson.Deserialize<ExtensionResponseEnvelope>(await accepted.Content.ReadAsStringAsync());
            Check(accepted.IsSuccessStatusCode && response.Success, "HTTP uses identical unframed DTO");
            await server.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { listener.Stop(); }
    }
    private static ExtensionRequestEnvelope Envelope<T>(string operation, T payload) => new ExtensionRequestEnvelope { Operation = operation, RequestId = Guid.NewGuid().ToString("N"), PayloadJson = PersistentRunnerJson.Serialize(payload) };
    private static NamedPipeClientStream Connect(string name) { var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut); try { pipe.Connect(5000); return pipe; } catch { pipe.Dispose(); throw; } }
    private static ExtensionResponseEnvelope Pipe(string name, ExtensionRequestEnvelope request)
    {
        using var pipe = Connect(name);
        using var timeout = new Timer(_ => pipe.Dispose(), null, 5000, Timeout.Infinite);
        BridgeWire.WriteFrame(pipe, Encoding.UTF8.GetBytes(PersistentRunnerJson.Serialize(request)), Limit);
        return PersistentRunnerJson.Deserialize<ExtensionResponseEnvelope>(Encoding.UTF8.GetString(BridgeWire.ReadFrame(pipe, Limit)));
    }
    private static Process Start(params string[] args)
    {
        var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        // Started as "dotnet Fixture.dll", the child needs the fixture's own path first.
        if (string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(System.Reflection.Assembly.GetEntryAssembly().Location);
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return Process.Start(info);
    }
    private static void Stop(Process process) { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
