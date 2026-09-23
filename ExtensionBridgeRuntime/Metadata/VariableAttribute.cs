using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HL7Soup.Integrations
{
    /// <summary>
    /// Specifies a variable that is used by a custom activity.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class |
                       System.AttributeTargets.Struct,
                       AllowMultiple = true)  ]
    public class VariableAttribute : System.Attribute
    {
        //For JSON deserialization
        public VariableAttribute()
        {
        }

        public VariableAttribute(string name, string sampleValue)
        {
            Name = name;
            SampleValue = sampleValue;
        }

        /// <summary>
        /// The name of the variable
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A sample value that this variable would have once populated.  Helps when parsing the messages.
        /// </summary>
        public string SampleValue { get; set; }
    }
}
