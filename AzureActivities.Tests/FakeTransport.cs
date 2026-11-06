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
        internal CancellationToken Cancellation;
    }
    internal sealed class FakeTransport : HttpPipelineTransport
    {
        private readonly Queue<Func<CapturedRequest, Response>> pending = new Queue<Func<CapturedRequest, Response>>();
        internal int Pending => pending.Count;
        internal int UnexpectedRequests { get; private set; }
        internal void Enqueue(Action<CapturedRequest> check, int status, object content, int? length = null, string contentRange = null)
        {
            pending.Enqueue(r => { check(r); return content is Stream stream ? new FakeResponse(status, Array.Empty<byte>(), length, contentRange, stream)
                : new FakeResponse(status, content is byte[] b ? b : Encoding.UTF8.GetBytes(content as string ?? string.Empty), length, contentRange); });
        }
        public override Request CreateRequest() => HttpClientTransport.Shared.CreateRequest();
        public override void Process(HttpMessage message)
        {
            message.CancellationToken.ThrowIfCancellationRequested();
            if (pending.Count == 0) { UnexpectedRequests++; throw new InvalidOperationException("Unexpected request/retry: " + message.Request.Method); }
            using (var body = new MemoryStream())
            {
                message.Request.Content?.WriteTo(body, CancellationToken.None);
                var captured = new CapturedRequest { Method = message.Request.Method.ToString(), Uri = message.Request.Uri.ToUri().AbsoluteUri, Body = body.ToArray(), Cancellation = message.CancellationToken };
                foreach (var header in message.Request.Headers) captured.Headers[header.Name] = header.Value;
                try { message.Response = pending.Dequeue()(captured); }
                catch (OperationCanceledException) when (message.CancellationToken.IsCancellationRequested) { throw; }
                catch (Exception ex) { Console.Error.WriteLine(ex.Message + " | " + captured.Method + " " + captured.Uri); throw; }
            }
        }
        public override ValueTask ProcessAsync(HttpMessage message) { Process(message); return default(ValueTask); }
    }
    internal sealed class FakeResponse : Response
    {
        private readonly Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly int status;
        private Stream contentStream;
        internal FakeResponse(int status, byte[] content, int? length, string contentRange = null, Stream body = null)
        {
            if (status >= 400 && !Encoding.UTF8.GetString(content).Contains("<Message>"))
                content = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content).Replace("</Error>", "<Message>Synthetic failure</Message></Error>"));
            this.status = status; ContentStream = body ?? new MemoryStream(content);
            headers["Content-Length"] = (length ?? content.Length).ToString(); headers["Content-Type"] = status >= 400 ? "application/xml" : "application/octet-stream";
            if (contentRange != null) headers["Content-Range"] = contentRange;
            headers["ETag"] = "\"v1\""; headers["Last-Modified"] = "Thu, 01 Jan 2026 00:00:00 GMT";
            headers["x-ms-blob-type"] = "BlockBlob"; headers["x-ms-request-id"] = "synthetic";
            if (status == 404) headers["x-ms-error-code"] = "BlobNotFound";
            if (status == 409) headers["x-ms-error-code"] = content != null && Encoding.UTF8.GetString(content).Contains("PublicAccessNotPermitted") ? "PublicAccessNotPermitted" : "ContainerAlreadyExists";
            if (status == 412) headers["x-ms-error-code"] = "ConditionNotMet";
        }
        public override int Status => status;
        public override string ReasonPhrase => "Synthetic";
        // Match the transport contract: the SDK can replace this with a
        // non-disposing wrapper when transferring ownership to the caller.
        public override Stream ContentStream
        {
            get => contentStream;
            set { contentStream = value; }
        }
        public override string ClientRequestId { get; set; }
        public override void Dispose() => ContentStream?.Dispose();
        protected override bool ContainsHeader(string name) => headers.ContainsKey(name);
        protected override bool TryGetHeader(string name, out string value) => headers.TryGetValue(name, out value);
        protected override bool TryGetHeaderValues(string name, out IEnumerable<string> values) { bool found = headers.TryGetValue(name, out string value); values = found ? new[] { value } : null; return found; }
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => headers.Select(h => new HttpHeader(h.Key, h.Value));
    }
}
