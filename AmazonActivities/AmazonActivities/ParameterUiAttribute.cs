using System;

namespace HL7Soup.Integrations
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = true)]
    public sealed class ParameterUiAttribute : Attribute
    {
        public ParameterUiAttribute(string parameterName)
        {
            ParameterName = parameterName;
        }

        public string ParameterName { get; }

        public string EditorType { get; set; } = "Text";

        public string Purpose { get; set; } = "Text";

        public string[] Options { get; set; }

        public string ValidationRegex { get; set; }

        public string ValidationMessage { get; set; }
    }
}
