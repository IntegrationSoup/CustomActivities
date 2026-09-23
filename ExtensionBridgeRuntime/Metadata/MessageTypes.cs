using System;
using System.Collections.Generic;
using System.Text;
using System.ComponentModel;

namespace HL7Soup.BaseTypes
{
    public enum MessageTypes
    {
        Unknown,
        [Description("HL7")]
        HL7V2,
        HL7V3,
        FHIR,
        XML,
        CSV,
        SQL,
        TextWithVariables, //This is a path
        HL7V2Path,
        XPath,
        CSVPath,
        JSON,
        JSONPath,
        Text, //This is a message type
        Binary,
        MessageStructure,
        DICOM
    }


}
