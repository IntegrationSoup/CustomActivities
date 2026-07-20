namespace ValidateTransformer
{
    internal static class ValidationActivitySamples
    {
        internal const string InputMessage = @"MSH|^~\&|HL7Soup|Instance1|HL7Soup|Instance2|20010520173800||ADT^A01^ADT_A01|93710600|P|2.5.1||||AL
PID|1||001000||Test^Patient||19670808|M";

        internal const string JsonResponse = @"{
  ""Profile"": ""ADT A01 Validation"",
  ""HasErrors"": true,
  ""AcknowledgmentCode"": ""AE"",
  ""Errors"": [
    {
      ""Path"": ""MSH-15"",
      ""ErrorSegment"": ""MSH"",
      ""ErrorSegmentOccurrence"": 1,
      ""ErrorField"": 15,
      ""ErrorFieldRepetition"": null,
      ""ErrorComponent"": null,
      ""ErrorSubcomponent"": null,
      ""Reason"": ""is not in message"",
      ""Severity"": ""E"",
      ""Hl7ErrorCode"": ""101"",
      ""Hl7ErrorText"": ""Required field missing"",
      ""Hl7ErrorCodingSystem"": ""HL70357"",
      ""ApplicationErrorCode"": ""PROFILE_REQUIRED"",
      ""ApplicationErrorText"": ""Required by validation profile"",
      ""ApplicationErrorCodingSystem"": ""L""
    }
  ]
}";

        internal const string Hl7AckResponse = @"MSH|^~\&|HL7Soup|Instance2|HL7Soup|Instance1|20010520173800||ACK^A01^ACK|93710600|P|2.5.1||||AL
MSA|AE|93710600|MSH-15: is not in message
ERR||MSH^1^15|101^Required field missing^HL70357|E|PROFILE_REQUIRED^Required by validation profile^L||Validation profile: ADT A01 Validation; Path: MSH-15; is not in message|MSH-15: is not in message";
    }
}
