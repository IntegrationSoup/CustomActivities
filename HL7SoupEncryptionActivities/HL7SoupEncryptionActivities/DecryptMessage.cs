using HL7Soup.Integrations;
using System.Collections.Generic;
using System.ComponentModel;

namespace HL7SoupEncryptionActivities
{
    [DisplayName("Decrypt Message")]
    [Parameter("Encryption Key", "The key used to encrypt and decrypt the message>", isRequired: true)]
    [ParameterUi("Encryption Key", Purpose = "Secret", ValidationRegex = "^.{12,}$", ValidationMessage = "Encryption Key must be at least 12 characters.")]
    [InMessage(@"", TypeOfMessages.Text)]
    [OutMessage(@"Decrypted Text", TypeOfMessages.UserDefined, TypeOfMessages.Text)]
    public class DecryptMessage : EncryptionActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            EncryptionActivitySupport.EncryptionRequest request = CreateRequest(parameters, "decrypt");
            string decryptedText = EncryptionActivitySupport.Execute(request, activityInstance.Message.Text ?? string.Empty);
            activityInstance.ResponseMessage.SetText(decryptedText);
        }
    }
}
