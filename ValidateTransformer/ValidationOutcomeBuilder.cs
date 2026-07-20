using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ValidateTransformer
{
    internal static class ValidationOutcomeBuilder
    {
        private static readonly Regex PathPattern = new Regex(
            @"^(?<segment>[A-Za-z0-9]{3})(?:\[(?<segmentOccurrence>\d+)\])?(?:[-.](?<field>\d+)(?:\[(?<fieldRepetition>\d+)\])?(?:\.(?<component>\d+))?(?:\.(?<subcomponent>\d+))?)?$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        internal static ValidationOutcome Build(string rawJson, string requestedProfile)
        {
            RawValidationOutcome rawOutcome = ValidationJson.Deserialize<RawValidationOutcome>(rawJson);
            if (rawOutcome == null || rawOutcome.Errors == null)
            {
                throw new Exception("The HL7 validation service returned a result without an Errors list.");
            }

            List<RawValidationError> rawErrors = rawOutcome.Errors;

            ValidationOutcome outcome = new ValidationOutcome
            {
                Profile = requestedProfile,
                HasErrors = rawErrors.Count > 0,
                AcknowledgmentCode = rawErrors.Count > 0 ? "AE" : "AA"
            };

            foreach (RawValidationError rawError in rawErrors)
            {
                outcome.Errors.Add(BuildError(rawError));
            }

            return outcome;
        }

        private static ValidationError BuildError(RawValidationError rawError)
        {
            string path = rawError == null ? string.Empty : rawError.Path ?? string.Empty;
            string reason = rawError == null ? string.Empty : rawError.Reason ?? string.Empty;
            reason = reason.Replace("messsage", "message");
            ValidationError error = new ValidationError
            {
                Path = path,
                Reason = reason,
                Severity = "E",
                ErrorSegmentOccurrence = 1
            };

            Match match = PathPattern.Match(path.Trim());
            if (match.Success)
            {
                error.ErrorSegment = match.Groups["segment"].Value.ToUpperInvariant();
                error.ErrorSegmentOccurrence = ParsePositiveInteger(match.Groups["segmentOccurrence"].Value, 1);
                error.ErrorField = ParseNullablePositiveInteger(match.Groups["field"].Value);
                error.ErrorFieldRepetition = ParseNullablePositiveInteger(match.Groups["fieldRepetition"].Value);
                error.ErrorComponent = ParseNullablePositiveInteger(match.Groups["component"].Value);
                error.ErrorSubcomponent = ParseNullablePositiveInteger(match.Groups["subcomponent"].Value);
            }
            else if (path.Length >= 3)
            {
                error.ErrorSegment = path.Substring(0, 3).ToUpperInvariant();
            }

            ApplyClassification(error);
            return error;
        }

        private static void ApplyClassification(ValidationError error)
        {
            string normalizedReason = (error.Reason ?? string.Empty).ToLowerInvariant();
            string normalizedPath = (error.Path ?? string.Empty).ToUpperInvariant();

            if (normalizedReason.Contains("not in message") ||
                normalizedReason.Contains("not in messsage") ||
                normalizedReason.Contains("required") ||
                normalizedReason == "is empty" ||
                normalizedReason.EndsWith(" is empty", StringComparison.Ordinal))
            {
                SetClassification(error, "101", "Required field missing", "PROFILE_REQUIRED", "Required by validation profile");
                return;
            }

            if (normalizedPath == "MSH-9.1")
            {
                SetClassification(error, "200", "Unsupported message type", "PROFILE_MESSAGE_TYPE", "Message type not permitted by validation profile");
                return;
            }

            if (normalizedPath == "MSH-9.2")
            {
                SetClassification(error, "201", "Unsupported event code", "PROFILE_EVENT_CODE", "Trigger event not permitted by validation profile");
                return;
            }

            if (normalizedPath == "MSH-11" || normalizedPath.StartsWith("MSH-11.", StringComparison.Ordinal))
            {
                SetClassification(error, "202", "Unsupported processing id", "PROFILE_PROCESSING_ID", "Processing ID not permitted by validation profile");
                return;
            }

            if (normalizedPath == "MSH-12" || normalizedPath.StartsWith("MSH-12.", StringComparison.Ordinal))
            {
                SetClassification(error, "203", "Unsupported version id", "PROFILE_VERSION_ID", "Version ID not permitted by validation profile");
                return;
            }

            if (normalizedReason.Contains("data table") || normalizedReason.Contains("in list"))
            {
                SetClassification(error, "103", "Table value not found", "PROFILE_TABLE_VALUE", "Value not permitted by validation profile");
                return;
            }

            SetClassification(error, "102", "Data type error", "PROFILE_RULE", "Validation profile rule failed");
        }

        private static void SetClassification(
            ValidationError error,
            string hl7Code,
            string hl7Text,
            string applicationCode,
            string applicationText)
        {
            error.Hl7ErrorCode = hl7Code;
            error.Hl7ErrorText = hl7Text;
            error.Hl7ErrorCodingSystem = "HL70357";
            error.ApplicationErrorCode = applicationCode;
            error.ApplicationErrorText = applicationText;
            error.ApplicationErrorCodingSystem = "L";
        }

        private static int ParsePositiveInteger(string value, int fallback)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0
                ? parsed
                : fallback;
        }

        private static int? ParseNullablePositiveInteger(string value)
        {
            int parsed;
            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed) && parsed > 0
                ? (int?)parsed
                : null;
        }
    }
}
