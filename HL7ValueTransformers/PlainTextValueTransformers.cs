using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HL7ValueTransformers
{
    public abstract class PlainTextValueTransformer : CustomTransformer
    {
        private const string ValueParameterName = "Value";
        private const string OutputVariableParameterName = "Output Variable";

        protected abstract string DefaultOutputVariableName { get; }
        protected abstract string FormatValue(string value);

        protected virtual string FormatValue(string value, Dictionary<string, string> parameters)
        {
            return FormatValue(value);
        }

        protected static string GetParameterValue(Dictionary<string, string> parameters, string name)
        {
            if (parameters != null && parameters.TryGetValue(name, out string value))
            {
                return value ?? string.Empty;
            }

            return string.Empty;
        }

        private static string GetValueToFormat(IMessage message, Dictionary<string, string> parameters)
        {
            if (parameters != null && parameters.TryGetValue(ValueParameterName, out string value))
            {
                return value ?? string.Empty;
            }

            return message.Text ?? string.Empty;
        }

        private string GetOutputVariableName(Dictionary<string, string> parameters)
        {
            string outputVariableName = GetParameterValue(parameters, OutputVariableParameterName).Trim();
            return string.IsNullOrWhiteSpace(outputVariableName)
                ? DefaultOutputVariableName
                : outputVariableName;
        }

        public override void Transform(IWorkflowInstance workflowInstance, IMessage message, Dictionary<string, string> parameters)
        {
            if (workflowInstance == null)
            {
                throw new ArgumentNullException(nameof(workflowInstance));
            }

            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            workflowInstance.SetVariable(
                GetOutputVariableName(parameters),
                FormatValue(GetValueToFormat(message, parameters), parameters));
        }
    }

    [Parameter("Value", "The plain text value to convert.", isRequired: true)]
    [Parameter("Output Variable", "Optional workflow variable to receive the converted value. Defaults to Digits.")]
    [Variable("Digits", "0215550100")]
    [DisplayName("Digits only")]
    public sealed class DigitsOnlyTransformer : PlainTextValueTransformer
    {
        protected override string DefaultOutputVariableName => "Digits";

        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatDigitsOnly(value);
        }
    }

    [Parameter("Value", "The plain text value to convert.", isRequired: true)]
    [Parameter("Output Variable", "Optional workflow variable to receive the converted value. Defaults to LettersAndDigits.")]
    [Variable("LettersAndDigits", "ZAA0067")]
    [DisplayName("Letters and digits only")]
    public sealed class LettersAndDigitsOnlyTransformer : PlainTextValueTransformer
    {
        protected override string DefaultOutputVariableName => "LettersAndDigits";

        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatLettersAndDigitsOnly(value);
        }
    }

    [Parameter("Value", "The plain text value to convert.", isRequired: true)]
    [Parameter("Output Variable", "Optional workflow variable to receive the converted value. Defaults to LettersDigitsSpaces.")]
    [Variable("LettersDigitsSpaces", "ABC 123 DEF")]
    [DisplayName("Letters, digits and spaces only")]
    public sealed class LettersDigitsAndSpacesOnlyTransformer : PlainTextValueTransformer
    {
        protected override string DefaultOutputVariableName => "LettersDigitsSpaces";

        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatLettersDigitsAndSpacesOnly(value);
        }
    }

    [Parameter("Value", "The plain text name to convert.", isRequired: true)]
    [Parameter("Output Variable", "Optional workflow variable to receive the converted value. Defaults to XPN.")]
    [Parameter("Name Order", "Optional. Leave blank for automatic parsing. Use FML, FL, LFM, LF, or words such as First Middle Last. Use / for alternatives, for example L,F M / F M L.")]
    [Variable("XPN", "Patient^Example")]
    [DisplayName("Text to HL7 person name (XPN)")]
    public sealed class TextToHl7NameTransformer : PlainTextValueTransformer
    {
        protected override string DefaultOutputVariableName => "XPN";

        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatHl7Xpn(value);
        }

        protected override string FormatValue(string value, Dictionary<string, string> parameters)
        {
            return Hl7ValueFormatter.FormatHl7Xpn(value, GetParameterValue(parameters, "Name Order"));
        }
    }

    [Parameter("Value", "The plain text clinician/provider name to convert.", isRequired: true)]
    [Parameter("Output Variable", "Optional workflow variable to receive the converted value. Defaults to XCN.")]
    [Parameter("Name Order", "Optional. Leave blank for automatic parsing. Use FML, FL, LFM, LF, or words such as First Middle Last. Use I or ID for an identifier in the source value.")]
    [Parameter("Person Identifier", "Optional. Identifier to place in XCN component 1. This overrides an ID detected in the value.")]
    [Variable("XCN", "12345^Doctor^Example")]
    [DisplayName("Text to HL7 clinician name (XCN)")]
    public sealed class TextToHl7ClinicianNameTransformer : PlainTextValueTransformer
    {
        protected override string DefaultOutputVariableName => "XCN";

        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatHl7Xcn(value, string.Empty, string.Empty);
        }

        protected override string FormatValue(string value, Dictionary<string, string> parameters)
        {
            return Hl7ValueFormatter.FormatHl7Xcn(
                value,
                GetParameterValue(parameters, "Name Order"),
                GetParameterValue(parameters, "Person Identifier"));
        }
    }

    [Parameter("Value", "The plain text value to convert.", isRequired: true)]
    [Parameter("Output Variable", "Optional workflow variable to receive the converted value. Defaults to XAD.")]
    [Variable("XAD", "1 Example Street^^Wollongong^NSW^2500")]
    [DisplayName("Text to HL7 address (XAD)")]
    public sealed class TextToHl7AddressTransformer : PlainTextValueTransformer
    {
        protected override string DefaultOutputVariableName => "XAD";

        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatHl7Xad(value);
        }
    }

    [Parameter("Value", "The plain text value to convert.", isRequired: true)]
    [Parameter("Output Variable", "Optional workflow variable to receive the converted value. Defaults to XTN.")]
    [Variable("XTN", "+64215550100")]
    [DisplayName("Text to HL7 phone (XTN)")]
    public sealed class TextToHl7PhoneTransformer : PlainTextValueTransformer
    {
        protected override string DefaultOutputVariableName => "XTN";

        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatHl7Xtn(value);
        }
    }
}
