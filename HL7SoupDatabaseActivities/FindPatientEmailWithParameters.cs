using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HL7Soup.Integrations;

namespace CustomTransformersSample
{
    [Parameter("Param1", "Example parameter.", isRequired: true)]
    [Parameter("Param2", "Another example parameter.")]
    [Variable("Patient Email", "sample@sample.com")]
    [DisplayName("Find Patient Email with Parameters")]
    public class FindPatientEmailWithParameters : CustomTransformer
    {
        public override void Transform(IWorkflowInstance workflowInstance, IMessage message, Dictionary<string, string> parameters)
        {
            string email = "";
            //Get the incoming message
            IHL7Message hl7 = workflowInstance.Activities[0].Message as IHL7Message;

            //Find the PID-14
            IHL7Field pid14 = (IHL7Field)hl7.GetPart("PID-14");
            if (pid14 != null)
            {
                //Look at all the PID 14s
                foreach (var repeatField in pid14.GetRelatedRepeatFields())
                {
                    //Get the 4th component (which is email)
                    IHL7Component component = repeatField.GetComponent("4");
                    if (component != null && component.Text != "")
                    {
                        //Found the email.  Return it.
                        email = component.Text;
                        break;
                    }
                }
            }

            workflowInstance.SetVariable("Patient Email", email);
        }
    }
}
