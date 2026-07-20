using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ValidateTransformer
{
    [InMessage(ValidationActivitySamples.InputMessage, TypeOfMessages.HL7)]
    [OutMessage(ValidationActivitySamples.Hl7AckResponse, TypeOfMessages.HL7)]
    [DisplayName("Validate HL7 Message to HL7 ACK")]
    [Parameter("Profile", "The name of the validation set", isRequired: true)]
    public class ValidateHL7ToAckTransformer : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            string profile = ValidationActivity.GetRequiredProfile(parameters);
            IHL7Message hl7Message = ValidationActivity.GetRequiredInputMessage(activityInstance, nameof(ValidateHL7ToAckTransformer));
            IHL7Message responseMessage = activityInstance.ResponseMessage as IHL7Message;
            if (responseMessage == null)
            {
                throw new Exception("ValidateHL7ToAckTransformer requires an HL7 response message.");
            }

            ValidationOutcome outcome = ValidationActivity.Validate(hl7Message, profile);
            ((IMessage)responseMessage).SetText(Hl7AcknowledgmentBuilder.Build(hl7Message, outcome));
        }
    }
}
