using HL7Soup.Integrations;
using System.Collections.Generic;
using System.ComponentModel;

namespace HL7SoupEncryptionActivities
{
    [DisplayName("Encrypt Message")]
    [Parameter("Encryption Key", "The key used to encrypt and decrypt the message. Provide a good key of at least 12 characters.", isRequired: true)]
    [ParameterUi("Encryption Key", Purpose = "Secret", ValidationRegex = "^.{12,}$", ValidationMessage = "Encryption Key must be at least 12 characters.")]
    [InMessage(@"", TypeOfMessages.UserDefined)]
    [OutMessage(@"Encrypted Text", TypeOfMessages.Text)]
    public class EncryptMessage : EncryptionActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            EncryptionActivitySupport.EncryptionRequest request = CreateRequest(parameters, "encrypt");
            string encrypted = EncryptionActivitySupport.Execute(request, activityInstance.Message.Text ?? string.Empty);
            activityInstance.ResponseMessage.SetText(encrypted);
        }
    }
}
