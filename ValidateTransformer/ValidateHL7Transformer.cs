using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ValidateTransformer
{
    [InMessage(ValidationActivitySamples.InputMessage, TypeOfMessages.HL7)]
    [OutMessage(ValidationActivitySamples.JsonResponse, TypeOfMessages.JSON)]
    [DisplayName("Validate HL7 Message")]
    [Parameter("Profile", "The name of the validation set", isRequired: true)]
    [Parameter(ValidationActivity.ErrorIfInvalidParameterName, "Mark Workflow Error on Invalid", isRequired: false)]
    [ParameterUi(ValidationActivity.ErrorIfInvalidParameterName, EditorType = "Checkbox")]
    public class ValidateHL7Transformer : CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            string profile = ValidationActivity.GetRequiredProfile(parameters);
            bool errorIfInvalid = ValidationActivity.GetErrorIfInvalid(parameters);
            IDisposable ownedInputMessage;
            IHL7Message hl7Message = ValidationActivity.GetRequiredInputMessage(
                workflowInstance,
                activityInstance,
                nameof(ValidateHL7Transformer),
                out ownedInputMessage);
            try
            {
                IJsonMessage responseMessage = activityInstance.ResponseMessage as IJsonMessage;
                if (responseMessage == null)
                {
                    throw new Exception("ValidateHL7Transformer requires a JSON response message.");
                }

                ValidationOutcome outcome = ValidationActivity.Validate(hl7Message, profile);
                responseMessage.SetText(ValidationJson.Serialize(outcome));
                ValidationActivity.HandleInvalidOutcome(workflowInstance, responseMessage, outcome, errorIfInvalid, promoteResponse: false);
            }
            finally
            {
                if (ownedInputMessage != null)
                {
                    ownedInputMessage.Dispose();
                }
            }
        }
    }
}
