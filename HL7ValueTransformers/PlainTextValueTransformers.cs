using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace HL7ValueTransformers
{
    public abstract class PlainTextValueTransformer : CustomTransformer
    {
        public override void Transform(IWorkflowInstance workflowInstance, IMessage message, Dictionary<string, string> parameters)
        {
            if (message == null)
            {
                throw new ArgumentNullException(nameof(message));
            }

            message.SetText(FormatValue(message.Text ?? string.Empty));
        }

        protected abstract string FormatValue(string value);
    }

    [DisplayName("Digits only")]
    public sealed class DigitsOnlyTransformer : PlainTextValueTransformer
    {
        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatDigitsOnly(value);
        }
    }

    [DisplayName("Letters and digits only")]
    public sealed class LettersAndDigitsOnlyTransformer : PlainTextValueTransformer
    {
        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatLettersAndDigitsOnly(value);
        }
    }

    [DisplayName("Letters, digits and spaces only")]
    public sealed class LettersDigitsAndSpacesOnlyTransformer : PlainTextValueTransformer
    {
        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatLettersDigitsAndSpacesOnly(value);
        }
    }

    [DisplayName("Text to HL7 name (XPN)")]
    public sealed class TextToHl7NameTransformer : PlainTextValueTransformer
    {
        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatHl7Xpn(value);
        }
    }

    [DisplayName("Text to HL7 address (XAD)")]
    public sealed class TextToHl7AddressTransformer : PlainTextValueTransformer
    {
        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatHl7Xad(value);
        }
    }

    [DisplayName("Text to HL7 phone (XTN)")]
    public sealed class TextToHl7PhoneTransformer : PlainTextValueTransformer
    {
        protected override string FormatValue(string value)
        {
            return Hl7ValueFormatter.FormatHl7Xtn(value);
        }
    }
}
