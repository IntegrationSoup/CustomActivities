using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HL7Soup.Integrations
{
    /// <summary>
    /// Specifies a parameter that brings values into a custom transformer.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class |
                       System.AttributeTargets.Struct,
                       AllowMultiple = true)]
    public class ParameterAttribute : System.Attribute
    {
        //For JSON deserialization
        public ParameterAttribute()
        {
        }

        public ParameterAttribute(string name)
        {
            Name = name;
        }

        public ParameterAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public ParameterAttribute(string name, string description, bool isRequired)
        {
            Name = name;
            Description = description;
            IsRequired = isRequired;
        }

        public ParameterAttribute(string name, bool isRequired)
        {
            Name = name;
            IsRequired = isRequired;
        }

        /// <summary>
        /// The name of the parameter
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the parameter 
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Must there be a value mapped to this parameter
        /// </summary>
        public bool IsRequired { get; set; }
    }
}
