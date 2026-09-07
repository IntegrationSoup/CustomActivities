using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HL7Soup.Integrations
{
    /// <summary>
    /// Sets the inbound message that this activity is expecting to recieve.
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class |
                       System.AttributeTargets.Struct,
                       AllowMultiple = false)]
    [Serializable]
    public class InMessageAttribute : System.Attribute
    {
        //Default constructor required for JSON
        public InMessageAttribute()
        {

        }

        /// <summary>
        /// Sets the inbound message that this activity is expecting to receive.  The workflow designer user will be able to edit the template used for binding, which could make the available segments and fields unpredicatable, but allows the message to be bound in it's entirety to another activity.
        /// </summary>
        /// <param name="sampleTemplateMessage">The sample message that will be used to help transformers create binding.</param>
        /// <param name="returnsResponse">Does this activity return a response</param>
        public InMessageAttribute(string sampleTemplateMessage, TypeOfMessages messageType)
        {
            SampleTemplateMessage = sampleTemplateMessage;
            MessageType = messageType;
            UserCanEditTemplate = true;
        }

        /// <summary>
        /// Sets the inbound message that this activity is expecting to receive.
        /// </summary>
        /// <param name="sampleTemplateMessage">The sample message that will be used to help transformers create binding.</param>
        /// <param name="returnsResponse">Does this activity return a response</param>
        /// <param name="returnsResponse">Can the user edit the template, or will they have to use only what the sampleTemplateMessage provides to help with binding.</param>
        public InMessageAttribute(string sampleTemplateMessage, TypeOfMessages messageType, bool userCanEditTemplate)
        {
            SampleTemplateMessage = sampleTemplateMessage;
            MessageType = messageType;
            UserCanEditTemplate = userCanEditTemplate;
        }

        /// <summary>
        /// The sample message that will be used to help transformers create binding.
        /// </summary>
        public string SampleTemplateMessage { get; set; }
        
        /// <summary>
        /// The type of message that is returned
        /// </summary>
        public TypeOfMessages MessageType { get; set; }

        /// <summary>
        /// Can the user edit the template, or will they have to use only what the sampleTemplateMessage provides to help with binding.
        /// </summary>
        [AiIgnore] 
        public bool UserCanEditTemplate { get; set; }

        [field: NonSerialized]
        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken> UnknownJsonProperties { get; set; }
    }

    
}
