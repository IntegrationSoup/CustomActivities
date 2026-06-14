using System.Runtime.Serialization;

namespace Popokey.ExtensionRunners
{
    internal static class PersistentRunnerProtocol
    {
        internal const int CurrentVersion = 1;
    }

    [DataContract]
    internal sealed class PersistentRunnerRequestEnvelope
    {
        [DataMember(Order = 1)]
        public int ProtocolVersion { get; set; } = PersistentRunnerProtocol.CurrentVersion;

        [DataMember(Order = 2)]
        public string RequestId { get; set; }

        [DataMember(Order = 3)]
        public string Operation { get; set; }

        [DataMember(Order = 4)]
        public string PayloadJson { get; set; }
    }

    [DataContract]
    internal sealed class PersistentRunnerResponseEnvelope
    {
        [DataMember(Order = 1)]
        public int ProtocolVersion { get; set; } = PersistentRunnerProtocol.CurrentVersion;

        [DataMember(Order = 2)]
        public string RequestId { get; set; }

        [DataMember(Order = 3)]
        public bool Success { get; set; }

        [DataMember(Order = 4)]
        public string PayloadJson { get; set; }

        [DataMember(Order = 5)]
        public string ErrorMessage { get; set; }

        internal static PersistentRunnerResponseEnvelope CreateSuccess(string requestId, string payloadJson)
        {
            return new PersistentRunnerResponseEnvelope
            {
                RequestId = requestId,
                Success = true,
                PayloadJson = payloadJson ?? string.Empty,
                ErrorMessage = string.Empty
            };
        }

        internal static PersistentRunnerResponseEnvelope CreateFailure(string requestId, string errorMessage)
        {
            return new PersistentRunnerResponseEnvelope
            {
                RequestId = requestId,
                Success = false,
                PayloadJson = string.Empty,
                ErrorMessage = errorMessage ?? "The runner reported a failure."
            };
        }
    }
}
