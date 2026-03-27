using HL7Soup.Integrations;
using System.Collections.Generic;
using System.ComponentModel;

namespace SftpActivities
{
    [DisplayName("SFTP Upload")]
    [Parameter("Host Name", "SFTP server host name.", isRequired: true)]
    [Parameter("Port", "SFTP server port. Defaults to 22 when left blank.", isRequired: false)]
    [ParameterUi("Port", EditorType = "Integer")]
    [Parameter("User Name", "SFTP login user name.", isRequired: true)]
    [Parameter("Password", "SFTP login password. Leave blank when using a private key only.", isRequired: false)]
    [ParameterUi("Password", Purpose = "Secret")]
    [Parameter("Private Key Path", "Path to a private key file on the Integration Soup host machine.", isRequired: false)]
    [Parameter("Private Key Passphrase", "Passphrase for the private key, if required.", isRequired: false)]
    [ParameterUi("Private Key Passphrase", Purpose = "Secret")]
    [Parameter("SSH Host Key Fingerprint", "Server fingerprint in WinSCP format, for example ssh-rsa 2048 xx:xx:xx.", isRequired: true)]
    [Parameter("Remote Path", "Full remote file path, for example /outbound/messages/message.hl7.", isRequired: true)]
    [Parameter("Create Remote Directory", "True to create the remote directory if it does not already exist.", isRequired: false)]
    [ParameterUi("Create Remote Directory", EditorType = "Checkbox")]
    [Parameter("Treat Message As Base64", "True when the incoming message text contains base64-encoded file bytes.", isRequired: false)]
    [ParameterUi("Treat Message As Base64", EditorType = "Checkbox")]
    [InMessage(@"MSH|^~\&|SendingApp|SendingFacility|ReceivingApp|ReceivingFacility|202503010900||ADT^A01|12345|P|2.5.1", TypeOfMessages.UserDefined)]
    [OutMessage(@"Uploaded /outbound/messages/message.hl7", TypeOfMessages.Text)]
    public class SftpUpload : SftpActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            SftpActivitySupport.SftpCommandRequest request = CreateRequest(parameters, operation: "upload");
            request.CreateRemoteDirectory = GetOptionalBoolean(parameters, "Create Remote Directory");

            byte[] messageBytes = GetMessageBytes(activityInstance.Message, GetOptionalBoolean(parameters, "Treat Message As Base64"));
            SftpActivitySupport.SftpCommandResponse response = SftpActivitySupport.Upload(request, messageBytes);

            activityInstance.ResponseMessage.SetText(response.Message ?? $"Uploaded {request.RemotePath}");
        }
    }
}
