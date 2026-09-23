using System;
using System.IO;
using System.Text;
using HL7Soup.Integrations.ExtensionBridge;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Popokey.ExtensionRunners
{
    // The same unframed outer JSON can be hosted behind an authenticated HTTP
    // endpoint. Authentication belongs to that host and precedes DispatchJson.
    internal static class BridgeWire
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false, true);
        internal static byte[] DispatchJson(byte[] bytes, BridgeProvider provider, int limit, bool controlOnly = false)
        {
            ValidateLimit(limit);
            if (bytes == null || bytes.Length == 0 || bytes.Length > limit) throw new InvalidDataException("Invalid envelope size.");
            ExtensionRequestEnvelope request = null;
            ExtensionResponseEnvelope response;
            try
            {
                string json = Utf8.GetString(bytes);
                using (var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None })
                {
                    var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                    if (!(token is JObject) || reader.Read()) throw new InvalidDataException("Expected a single JSON object.");
                    request = PersistentRunnerJson.Deserialize<ExtensionRequestEnvelope>(json);
                }
                if (request?.Operation == ExtensionBridgeProtocol.Describe && bytes.Length > ExtensionBridgeProtocol.MaximumDescribeBytes)
                    throw new InvalidDataException("Describe exceeds its envelope limit.");
                response = provider.Dispatch(request, controlOnly);
            }
            catch
            {
                response = new ExtensionResponseEnvelope { RequestId = request?.RequestId, Success = false, ErrorMessage = "Malformed or oversized bridge envelope.",
                    PayloadJson = PersistentRunnerJson.Serialize(new ExtensionFailure { Code = "InvalidRequest", ExecutionState = "NotStarted", Message = "Malformed or oversized bridge envelope." }) };
            }
            byte[] output = Utf8.GetBytes(PersistentRunnerJson.Serialize(response));
            int responseLimit = request?.Operation == ExtensionBridgeProtocol.Describe ? Math.Min(limit, ExtensionBridgeProtocol.MaximumDescribeBytes) : limit;
            if (output.Length > responseLimit)
            {
                output = Utf8.GetBytes(PersistentRunnerJson.Serialize(new ExtensionResponseEnvelope { RequestId = request?.RequestId, Success = false,
                    ErrorMessage = "Response exceeds envelope limit.", PayloadJson = PersistentRunnerJson.Serialize(new ExtensionFailure { Code = "ExecutionFailed", ExecutionState = "Completed", Message = "Response exceeds envelope limit; operation is not replayable." }) }));
            }
            if (output.Length > responseLimit) throw new InvalidDataException("Response exceeds envelope limit.");
            return output;
        }

        internal static byte[] ReadFrame(Stream stream, int limit)
        {
            ValidateLimit(limit);
            byte[] prefix = ReadExact(stream, 4);
            int length = prefix[0] | prefix[1] << 8 | prefix[2] << 16 | prefix[3] << 24;
            if (length <= 0 || length > limit) throw new InvalidDataException("Invalid bridge frame length.");
            return ReadExact(stream, length);
        }

        internal static void WriteFrame(Stream stream, byte[] bytes, int limit)
        {
            ValidateLimit(limit);
            if (bytes == null || bytes.Length == 0 || bytes.Length > limit) throw new InvalidDataException("Invalid bridge frame length.");
            int n = bytes.Length;
            stream.Write(new[] { (byte)n, (byte)(n >> 8), (byte)(n >> 16), (byte)(n >> 24) }, 0, 4);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush();
        }

        private static byte[] ReadExact(Stream stream, int count)
        {
            byte[] bytes = new byte[count];
            for (int offset = 0; offset < count;)
            {
                int read = stream.Read(bytes, offset, count - offset);
                if (read <= 0) throw new EndOfStreamException("Truncated bridge frame.");
                offset += read;
            }
            return bytes;
        }

        private static void ValidateLimit(int limit)
        {
            if (limit <= 0 || limit > ExtensionBridgeProtocol.MaximumMessageBytes) throw new ArgumentOutOfRangeException(nameof(limit));
        }
    }
}
