using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace HL7Soup.Integrations
{
    /// <summary>
    /// Sets that this activity returns a message
    /// </summary>
    [System.AttributeUsage(System.AttributeTargets.Class |
                       System.AttributeTargets.Struct,
                       AllowMultiple = false)]
    [Serializable]
    public class OutMessageAttribute : System.Attribute
    {
        //For JSON deserialization
        public OutMessageAttribute()
        {
        }

        /// <summary>
        /// Sets that this activity returns a message
        /// </summary>
        /// <param name="sampleResponseMessage">The sample message that will be used to help transformers create binding.</param>
        /// <param name="returnsResponse">Does this activity return a response</param>
        public OutMessageAttribute(string sampleResponseMessage, TypeOfMessages messageType)
        {
            SampleResponseMessage = sampleResponseMessage;
            MessageType = messageType;
            DefaultMessageType = messageType;
        }

        /// <summary>
        /// Sets that this activity returns a message and allows a default type to be suggested when the response type is user-defined.
        /// </summary>
        /// <param name="sampleResponseMessage">The sample message that will be used to help transformers create binding.</param>
        /// <param name="messageType">The declared response type.</param>
        /// <param name="defaultMessageType">The default response type to use when messageType is UserDefined.</param>
        public OutMessageAttribute(string sampleResponseMessage, TypeOfMessages messageType, TypeOfMessages defaultMessageType)
        {
            SampleResponseMessage = sampleResponseMessage;
            MessageType = messageType;
            DefaultMessageType = defaultMessageType;
        }

        /// <summary>
        /// The sample message that will be used to help transformers create binding.
        /// </summary>
        public string SampleResponseMessage { get; set; }

        /// <summary>
        /// The type of message that is returned
        /// </summary>
        public TypeOfMessages MessageType { get; set; }

        /// <summary>
        /// The default type to use when the returned message type is user-defined.
        /// </summary>
        public TypeOfMessages DefaultMessageType { get; set; } = TypeOfMessages.UserDefined;

        [field: NonSerialized]
        [Newtonsoft.Json.JsonExtensionData]
        public IDictionary<string, Newtonsoft.Json.Linq.JToken> UnknownJsonProperties { get; set; }

    }


}
