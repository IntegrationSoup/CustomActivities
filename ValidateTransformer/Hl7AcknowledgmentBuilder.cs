using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ValidateTransformer
{
    internal static class Hl7AcknowledgmentBuilder
    {
        internal static string Build(IHL7Message inboundMessage, ValidationOutcome outcome)
        {
            IHL7Segment msh = inboundMessage == null ? null : inboundMessage.GetSegment("MSH");
            if (msh == null || string.IsNullOrWhiteSpace(msh.Text))
            {
                throw new Exception("Cannot generate an HL7 ACK because the inbound message does not have an MSH segment.");
            }

            MshFieldValues mshFields = new MshFieldValues
            {
                EncodingCharacters = msh.GetFieldValue(2),
                SendingApplication = msh.GetFieldValue(3),
                SendingFacility = msh.GetFieldValue(4),
                ReceivingApplication = msh.GetFieldValue(5),
                ReceivingFacility = msh.GetFieldValue(6),
                TriggerEvent = msh.GetComponentValue(9, 2),
                MessageControlId = msh.GetFieldValue(10),
                Version = msh.GetFieldValue(12)
            };

            return Build(msh.Text, outcome, mshFields);
        }

        internal static string Build(string inboundMsh, ValidationOutcome outcome)
        {
            return Build(inboundMsh, outcome, null);
        }

        private static string Build(string inboundMsh, ValidationOutcome outcome, MshFieldValues mshFields)
        {
            if (string.IsNullOrWhiteSpace(inboundMsh) || inboundMsh.Length < 4 || !inboundMsh.StartsWith("MSH", StringComparison.Ordinal))
            {
                throw new Exception("Cannot generate an HL7 ACK because the inbound message does not have a valid MSH segment.");
            }

            outcome = outcome ?? new ValidationOutcome { AcknowledgmentCode = "AA" };
            char fieldSeparator = inboundMsh[3];
            string[] fields = inboundMsh.TrimEnd('\r', '\n').Split(new[] { fieldSeparator }, StringSplitOptions.None);
            string encodingCharacters = mshFields == null ? GetField(fields, 1) : mshFields.EncodingCharacters;
            if (string.IsNullOrEmpty(encodingCharacters) || encodingCharacters.Length < 4)
            {
                encodingCharacters = "^~\\&";
            }
            char componentSeparator = encodingCharacters[0];
            char repetitionSeparator = encodingCharacters[1];
            char escapeCharacter = encodingCharacters[2];
            char subcomponentSeparator = encodingCharacters[3];
            char? truncationCharacter = encodingCharacters.Length > 4 ? (char?)encodingCharacters[4] : null;

            string inboundSendingApplication = mshFields == null ? GetField(fields, 2) : mshFields.SendingApplication;
            string inboundSendingFacility = mshFields == null ? GetField(fields, 3) : mshFields.SendingFacility;
            string inboundReceivingApplication = mshFields == null ? GetField(fields, 4) : mshFields.ReceivingApplication;
            string inboundReceivingFacility = mshFields == null ? GetField(fields, 5) : mshFields.ReceivingFacility;
            if (fields.Length > 2)
            {
                fields[2] = inboundReceivingApplication;
            }
            if (fields.Length > 3)
            {
                fields[3] = inboundReceivingFacility;
            }
            if (fields.Length > 4)
            {
                fields[4] = inboundSendingApplication;
            }
            if (fields.Length > 5)
            {
                fields[5] = inboundSendingFacility;
            }

            string versionField = mshFields == null ? GetField(fields, 11) : mshFields.Version;
            ErrFormat errFormat = GetErrFormat(versionField, componentSeparator);
            if (fields.Length > 8)
            {
                string[] messageType = fields[8].Split(new[] { componentSeparator }, StringSplitOptions.None);
                if (messageType.Length == 0)
                {
                    messageType = new[] { "ACK" };
                }
                else
                {
                    messageType[0] = "ACK";
                }
                if (messageType.Length > 1 && mshFields != null)
                {
                    messageType[1] = mshFields.TriggerEvent ?? string.Empty;
                }
                if (errFormat == ErrFormat.Modern && messageType.Length < 3)
                {
                    Array.Resize(ref messageType, 3);
                }
                if (messageType.Length > 2)
                {
                    messageType[2] = "ACK";
                }
                fields[8] = string.Join(componentSeparator.ToString(), messageType);
            }

            string acknowledgmentCode = outcome.HasErrors ? "AE" : "AA";
            outcome.AcknowledgmentCode = acknowledgmentCode;
            // MSA-2 identifies the inbound message and must echo MSH-10 exactly as encoded.
            string messageControlId = mshFields == null ? GetField(fields, 9) : mshFields.MessageControlId;
            string ackMsh = string.Join(fieldSeparator.ToString(), fields);
            string msa = "MSA" + fieldSeparator + acknowledgmentCode + fieldSeparator + messageControlId;

            if (outcome.HasErrors && outcome.Errors.Count > 0)
            {
                ValidationError firstError = outcome.Errors[0];
                string summary = BuildUserMessage(firstError);
                msa += fieldSeparator + EscapeText(
                    summary,
                    fieldSeparator,
                    componentSeparator,
                    repetitionSeparator,
                    escapeCharacter,
                    subcomponentSeparator,
                    80,
                    truncationCharacter);
            }

            List<string> segments = new List<string> { ackMsh, msa };
            if (outcome.HasErrors && outcome.Errors.Count > 0)
            {
                if (errFormat == ErrFormat.Modern)
                {
                    foreach (ValidationError error in outcome.Errors)
                    {
                        segments.Add(BuildModernErr(
                            error,
                            outcome.Profile,
                            fieldSeparator,
                            componentSeparator,
                            repetitionSeparator,
                            escapeCharacter,
                            subcomponentSeparator,
                            truncationCharacter));
                    }
                }
                else
                {
                    IEnumerable<string> errors = outcome.Errors.Select(error =>
                        errFormat == ErrFormat.DetailedLegacy
                            ? BuildLegacyErrRepetition(
                                error,
                                componentSeparator,
                                repetitionSeparator,
                                escapeCharacter,
                                subcomponentSeparator,
                                fieldSeparator,
                                truncationCharacter)
                            : BuildBasicLegacyErrRepetition(error, componentSeparator));
                    segments.Add("ERR" + fieldSeparator + string.Join(repetitionSeparator.ToString(), errors));
                }
            }

            return string.Join("\r", segments);
        }

        private static string BuildModernErr(
            ValidationError error,
            string profile,
            char fieldSeparator,
            char componentSeparator,
            char repetitionSeparator,
            char escapeCharacter,
            char subcomponentSeparator,
            char? truncationCharacter)
        {
            string location = string.Join(componentSeparator.ToString(), new[]
            {
                error.ErrorSegment ?? string.Empty,
                Math.Max(1, error.ErrorSegmentOccurrence).ToString(CultureInfo.InvariantCulture),
                NullableNumber(error.ErrorField),
                NullableNumber(error.ErrorFieldRepetition),
                NullableNumber(error.ErrorComponent),
                NullableNumber(error.ErrorSubcomponent)
            }).TrimEnd(componentSeparator);

            string hl7Code = JoinComponents(componentSeparator,
                error.Hl7ErrorCode,
                EscapeText(error.Hl7ErrorText, fieldSeparator, componentSeparator, repetitionSeparator, escapeCharacter, subcomponentSeparator, int.MaxValue, truncationCharacter),
                string.IsNullOrWhiteSpace(error.Hl7ErrorCodingSystem) ? "HL70357" : error.Hl7ErrorCodingSystem);
            string applicationCode = JoinComponents(componentSeparator,
                error.ApplicationErrorCode,
                EscapeText(error.ApplicationErrorText, fieldSeparator, componentSeparator, repetitionSeparator, escapeCharacter, subcomponentSeparator, int.MaxValue, truncationCharacter),
                string.IsNullOrWhiteSpace(error.ApplicationErrorCodingSystem) ? "L" : error.ApplicationErrorCodingSystem);
            string diagnostic = EscapeText(
                "Validation profile: " + profile + "; Path: " + error.Path + "; " + error.Reason,
                fieldSeparator,
                componentSeparator,
                repetitionSeparator,
                escapeCharacter,
                subcomponentSeparator,
                2048,
                truncationCharacter);
            string userMessage = EscapeText(
                BuildUserMessage(error),
                fieldSeparator,
                componentSeparator,
                repetitionSeparator,
                escapeCharacter,
                subcomponentSeparator,
                250,
                truncationCharacter);

            return string.Join(fieldSeparator.ToString(), new[]
            {
                "ERR",
                string.Empty,
                location,
                hl7Code,
                error.Severity ?? "E",
                applicationCode,
                string.Empty,
                diagnostic,
                userMessage
            });
        }

        private static string BuildLegacyErrRepetition(
            ValidationError error,
            char componentSeparator,
            char repetitionSeparator,
            char escapeCharacter,
            char subcomponentSeparator,
            char fieldSeparator,
            char? truncationCharacter)
        {
            string location = string.Join(componentSeparator.ToString(), new[]
            {
                error.ErrorSegment ?? string.Empty,
                Math.Max(1, error.ErrorSegmentOccurrence).ToString(CultureInfo.InvariantCulture),
                NullableNumber(error.ErrorField)
            });

            string codePrefix = JoinSubcomponents(subcomponentSeparator,
                error.Hl7ErrorCode,
                EscapeText(error.Hl7ErrorText, fieldSeparator, componentSeparator, repetitionSeparator, escapeCharacter, subcomponentSeparator, int.MaxValue, truncationCharacter),
                string.IsNullOrWhiteSpace(error.Hl7ErrorCodingSystem) ? "HL70357" : error.Hl7ErrorCodingSystem,
                error.ApplicationErrorCode);
            string suffix = subcomponentSeparator +
                            (string.IsNullOrWhiteSpace(error.ApplicationErrorCodingSystem) ? "L" : error.ApplicationErrorCodingSystem);
            int availableTextLength = Math.Max(0, 80 - location.Length - 1 - codePrefix.Length - 1 - suffix.Length);
            string localText = EscapeText(
                BuildUserMessage(error),
                fieldSeparator,
                componentSeparator,
                repetitionSeparator,
                escapeCharacter,
                subcomponentSeparator,
                availableTextLength,
                truncationCharacter);

            return location + componentSeparator + codePrefix + subcomponentSeparator + localText + suffix;
        }

        private static string BuildBasicLegacyErrRepetition(ValidationError error, char componentSeparator)
        {
            return string.Join(componentSeparator.ToString(), new[]
            {
                error.ErrorSegment ?? string.Empty,
                Math.Max(1, error.ErrorSegmentOccurrence).ToString(CultureInfo.InvariantCulture),
                NullableNumber(error.ErrorField),
                string.IsNullOrWhiteSpace(error.Hl7ErrorCode) ? "207" : error.Hl7ErrorCode
            });
        }

        private static ErrFormat GetErrFormat(string versionField, char componentSeparator)
        {
            string version = (versionField ?? string.Empty).Split(componentSeparator)[0].Trim();
            string[] parts = version.Split('.');
            int major;
            int minor;
            if (parts.Length < 2 ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out major) ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor))
            {
                return ErrFormat.DetailedLegacy;
            }

            if (major > 2 || (major == 2 && minor >= 5))
            {
                return ErrFormat.Modern;
            }

            int patch = 0;
            if (parts.Length > 2)
            {
                int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch);
            }

            return major == 2 && (minor > 3 || (minor == 3 && patch >= 1))
                ? ErrFormat.DetailedLegacy
                : ErrFormat.BasicLegacy;
        }

        private static string BuildUserMessage(ValidationError error)
        {
            string path = error == null ? string.Empty : error.Path ?? string.Empty;
            string reason = error == null ? string.Empty : error.Reason ?? string.Empty;
            if (string.IsNullOrWhiteSpace(path))
            {
                return reason;
            }
            if (string.IsNullOrWhiteSpace(reason))
            {
                return path;
            }
            return path + ": " + reason;
        }

        private static string EscapeText(
            string value,
            char fieldSeparator,
            char componentSeparator,
            char repetitionSeparator,
            char escapeCharacter,
            char subcomponentSeparator,
            int maximumLength = int.MaxValue,
            char? truncationCharacter = null)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string escape = escapeCharacter.ToString();
            StringBuilder result = new StringBuilder(Math.Min(value.Length, maximumLength));
            foreach (char character in value)
            {
                string encoded;
                if (character == '\r' || character == '\n')
                {
                    encoded = " ";
                }
                else if (character == escapeCharacter)
                {
                    encoded = escape + "E" + escape;
                }
                else if (character == fieldSeparator)
                {
                    encoded = escape + "F" + escape;
                }
                else if (character == componentSeparator)
                {
                    encoded = escape + "S" + escape;
                }
                else if (character == repetitionSeparator)
                {
                    encoded = escape + "R" + escape;
                }
                else if (character == subcomponentSeparator)
                {
                    encoded = escape + "T" + escape;
                }
                else if (truncationCharacter.HasValue && character == truncationCharacter.Value)
                {
                    encoded = escape + "P" + escape;
                }
                else
                {
                    encoded = character.ToString();
                }

                if (result.Length + encoded.Length > maximumLength)
                {
                    break;
                }
                result.Append(encoded);
            }

            return result.ToString();
        }

        private static string JoinComponents(char separator, params string[] values)
        {
            return string.Join(separator.ToString(), values.Select(value => value ?? string.Empty));
        }

        private static string JoinSubcomponents(char separator, params string[] values)
        {
            return string.Join(separator.ToString(), values.Select(value => value ?? string.Empty));
        }

        private static string NullableNumber(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        }

        private static string GetField(string[] fields, int index)
        {
            return fields != null && fields.Length > index ? fields[index] : string.Empty;
        }

        private enum ErrFormat
        {
            BasicLegacy,
            DetailedLegacy,
            Modern
        }

        private sealed class MshFieldValues
        {
            internal string EncodingCharacters { get; set; }

            internal string SendingApplication { get; set; }

            internal string SendingFacility { get; set; }

            internal string ReceivingApplication { get; set; }

            internal string ReceivingFacility { get; set; }

            internal string TriggerEvent { get; set; }

            internal string MessageControlId { get; set; }

            internal string Version { get; set; }
        }
    }
}
