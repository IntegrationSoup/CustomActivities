using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ValidateTransformer
{
    [DataContract]
    internal sealed class RawValidationOutcome
    {
        [DataMember(Name = "Profile")]
        public string Profile { get; set; }

        [DataMember(Name = "Errors")]
        public List<RawValidationError> Errors { get; set; }
    }

    [DataContract]
    internal sealed class RawValidationError
    {
        [DataMember(Name = "Path")]
        public string Path { get; set; }

        [DataMember(Name = "Reason")]
        public string Reason { get; set; }
    }

    [DataContract]
    internal sealed class ValidationOutcome
    {
        [DataMember(Name = "Profile", Order = 1)]
        public string Profile { get; set; }

        [DataMember(Name = "HasErrors", Order = 2)]
        public bool HasErrors { get; set; }

        [DataMember(Name = "AcknowledgmentCode", Order = 3)]
        public string AcknowledgmentCode { get; set; }

        [DataMember(Name = "Errors", Order = 4)]
        public List<ValidationError> Errors { get; set; } = new List<ValidationError>();
    }

    [DataContract]
    internal sealed class ValidationError
    {
        [DataMember(Name = "Path", Order = 1)]
        public string Path { get; set; }

        [DataMember(Name = "ErrorSegment", Order = 2)]
        public string ErrorSegment { get; set; }

        [DataMember(Name = "ErrorSegmentOccurrence", Order = 3)]
        public int ErrorSegmentOccurrence { get; set; }

        [DataMember(Name = "ErrorField", Order = 4)]
        public int? ErrorField { get; set; }

        [DataMember(Name = "ErrorFieldRepetition", Order = 5)]
        public int? ErrorFieldRepetition { get; set; }

        [DataMember(Name = "ErrorComponent", Order = 6)]
        public int? ErrorComponent { get; set; }

        [DataMember(Name = "ErrorSubcomponent", Order = 7)]
        public int? ErrorSubcomponent { get; set; }

        [DataMember(Name = "Reason", Order = 8)]
        public string Reason { get; set; }

        [DataMember(Name = "Severity", Order = 9)]
        public string Severity { get; set; }

        [DataMember(Name = "Hl7ErrorCode", Order = 10)]
        public string Hl7ErrorCode { get; set; }

        [DataMember(Name = "Hl7ErrorText", Order = 11)]
        public string Hl7ErrorText { get; set; }

        [DataMember(Name = "Hl7ErrorCodingSystem", Order = 12)]
        public string Hl7ErrorCodingSystem { get; set; }

        [DataMember(Name = "ApplicationErrorCode", Order = 13)]
        public string ApplicationErrorCode { get; set; }

        [DataMember(Name = "ApplicationErrorText", Order = 14)]
        public string ApplicationErrorText { get; set; }

        [DataMember(Name = "ApplicationErrorCodingSystem", Order = 15)]
        public string ApplicationErrorCodingSystem { get; set; }
    }
}
