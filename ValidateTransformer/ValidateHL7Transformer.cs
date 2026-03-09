using HL7Soup.Integrations;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.SqlClient;

namespace ValidateTransformer
{
    [InMessage(@"MSH|^~\&|HL7Soup|Instance1|HL7Soup|Instance2|20010520173800| |SIU^S12|93710600|P|2.5.1|AL||
SCH|Placer001|Filler001|||||^Unklare Beschwerden||||^^^20010701100000^20010701103000
PID|1||001000||TEST^TEST^TEST^^^TEST|Schulz|19670808|M|||||(089) 14002243|(089) 1234|||||198708080150|||||||||D|20020131063", TypeOfMessages.HL7)]
    [OutMessage(@"{
  ""Profile"": ""ADT A01 Validation"",
  ""Errors"": [
    {
      ""Path"": ""MSH-15"",
      ""Reason"": ""Red (Invalid) when not in messsage""
    }
]}", TypeOfMessages.JSON)]
    [DisplayName("Validate HL7 Message")]
    [Parameter("Profile", "The name of the validation set", isRequired: true)]
    public class ValidateHL7Transformer: CustomActivity
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            string profile = parameters["Profile"];
            IHL7Message hl7Message = activityInstance.Message as IHL7Message;
            if (hl7Message==null)
            {
                throw new System.Exception("ValidateHL7Transformer requires receiving an HL7 message in it's HL7 message template");
            }
            string outcome = hl7Message.ValidateWithHighlighters(profile);
            //workflowInstance.SetVariable("ValidationOutcome", outcome);
            ((IJsonMessage)activityInstance.ResponseMessage).SetText(outcome);
        }
    }
}
