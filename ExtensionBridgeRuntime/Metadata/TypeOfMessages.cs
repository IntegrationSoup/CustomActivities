using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HL7Soup.Integrations
{
    public enum TypeOfMessages
    {
        UserDefined = 0,
        HL7 = 1,
        //HL7V3=2,
        //FHIR=3,
        XML = 4,
        CSV = 5,
        //SQL,
        //TextWithVariables,
        //HL7V2Path,
        //XPath,
        //CSVPath
        JSON = 11,
        //JSONPath,
        Text = 13,
        Binary = 14,
    }
}
