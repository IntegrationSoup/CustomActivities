using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AzureActivities.Tests
{
    internal sealed class CapturedRequest
    {
        internal string Method, Uri;
        internal byte[] Body;
        internal Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }
    internal sealed class FakeTransport : HttpPipelineTransport
    {
        private readonly Queue<Func<CapturedRequest, Response>> pending = new Queue<Func<CapturedRequest, Response>>();
        internal int Pending => pending.Count;
        internal void Enqueue(Action<CapturedRequest> check, int status, object content, int? length = null)
        {
            pending.Enqueue(r => { check(r); return new FakeResponse(status, content is byte[] b ? b : Encoding.UTF8.GetBytes(content as string ?? string.Empty), length); });
        }
        public override Request CreateRequest() => HttpClientTransport.Shared.CreateRequest();
        public override void Process(HttpMessage message)
        {
            if (pending.Count == 0) throw new InvalidOperationException("Unexpected request/retry: " + message.Request.Method);
            using (var body = new MemoryStream())
            {
                message.Request.Content?.WriteTo(body, CancellationToken.None);
                var captured = new CapturedRequest { Method = message.Request.Method.ToString(), Uri = message.Request.Uri.ToString(), Body = body.ToArray() };
                foreach (var header in message.Request.Headers) captured.Headers[header.Name] = header.Value;
                try { message.Response = pending.Dequeue()(captured); }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message + " | " + captured.Method + " " + captured.Uri); throw; }
            }
        }
        public override ValueTask ProcessAsync(HttpMessage message) { Process(message); return default(ValueTask); }
    }
    internal sealed class FakeResponse : Response
    {
        private readonly Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly int status;
        internal FakeResponse(int status, byte[] content, int? length)
        {
            if (status >= 400 && !Encoding.UTF8.GetString(content).Contains("<Message>"))
                content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content).Replace("</Error>", "<Message>Synthetic failure</Message></Error>"));
            this.status = status; ContentStream = new MemoryStream(content);
            headers["Content-Length"] = (length ?? content.Length).ToString(); headers["Content-Type"] = status >= 400 ? "application/xml" : "application/octet-stream";
            headers["ETag"] = "\"v1\""; headers["Last-Modified"] = "Thu, 01 Jan 2026 00:00:00 GMT";
            headers["x-ms-blob-type"] = "BlockBlob"; headers["x-ms-request-id"] = "synthetic";
            if (status == 404) headers["x-ms-error-code"] = "BlobNotFound";
            if (status == 409) headers["x-ms-error-code"] = content != null && Encoding.UTF8.GetString(content).Contains("PublicAccessNotPermitted") ? "PublicAccessNotPermitted" : "ContainerAlreadyExists";
            if (status == 412) headers["x-ms-error-code"] = "ConditionNotMet";
        }
        public override int Status => status;
        public override string ReasonPhrase => "Synthetic";
        public override Stream ContentStream { get; set; }
        public override string ClientRequestId { get; set; }
        public override void Dispose() => ContentStream?.Dispose();
        protected override bool ContainsHeader(string name) => headers.ContainsKey(name);
        protected override bool TryGetHeader(string name, out string value) => headers.TryGetValue(name, out value);
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { bool found = headers.TryGetValue(name, out string value); values = found ? new[] { value } : null; return found; }
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => headers.Select(h => new HttpHeader(h.Key, h.Value));
    }
}
