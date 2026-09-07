using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;

// Synthetic transport hosts only. This is not an externally exposed HTTP server.
internal static class TransportFixture
{
    private const int Limit = 65536;
    internal static void Run()
    {
        string name = "bridge-transport-fixture-" + Guid.NewGuid().ToString("N");
        using (Process child = Start("--server", "--pipe-name", name, "--parent-pid", Environment.ProcessId.ToString(), "--bridge-version", "1"))
        {
            try
            {
                using (var malformed = Connect(name)) { malformed.Write(new byte[4]); malformed.Flush(); }
                var described = Pipe(name, Envelope("extension.describe", new ExtensionDescribeRequest { ProviderId = "fixture" }));
                Check(described.Success, "runner survives invalid frame");
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
    private static Process Start(params string[] args) { var info = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true }; foreach (string arg in args) info.ArgumentList.Add(arg); return Process.Start(info); }
    private static void Stop(Process process) { if (!process.HasExited) { process.Kill(); process.WaitForExit(5000); } }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); }
}
