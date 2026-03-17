using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace SftpActivities
{
    [DisplayName("Upload SFTP")]
    [Parameter("Host Name", "SFTP server host name.", isRequired: true)]
    [Parameter("Port", "SFTP server port. Defaults to 22 when left blank.", isRequired: false)]
    [Parameter("User Name", "SFTP login user name.", isRequired: true)]
    [Parameter("Password", "SFTP login password. Leave blank when using a private key only.", isRequired: false)]
    [Parameter("Private Key Path", "Path to a private key file on the Integration Soup host machine.", isRequired: false)]
    [Parameter("Private Key Passphrase", "Passphrase for the private key, if required.", isRequired: false)]
    [Parameter("SSH Host Key Fingerprint", "Server fingerprint in WinSCP format, for example ssh-rsa 2048 xx:xx:xx.", isRequired: true)]
    [Parameter("Remote Path", "Full remote file path, for example /outbound/messages/message.hl7.", isRequired: true)]
    [Parameter("Create Remote Directory", "True to create the remote directory if it does not already exist.", isRequired: false)]
    [Parameter("Treat Message As Base64", "True when the incoming message text contains base64-encoded file bytes.", isRequired: false)]
    [InMessage(@"MSH|^~\&|SendingApp|SendingFacility|ReceivingApp|ReceivingFacility|202503010900||ADT^A01|12345|P|2.5.1", TypeOfMessages.Text)]
    [OutMessage(@"Uploaded /outbound/messages/message.hl7", TypeOfMessages.Text)]
    public class SftpUpload : SftpActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            SftpActivitySupport.SftpCommandRequest request = CreateRequest(parameters, operation: "upload");
            request.CreateRemoteDirectory = GetOptionalBoolean(parameters, "Create Remote Directory");

            byte[] messageBytes = GetMessageBytes(activityInstance.Message.Text, GetOptionalBoolean(parameters, "Treat Message As Base64"));
            SftpActivitySupport.SftpCommandResponse response = SftpActivitySupport.Upload(request, messageBytes);

            activityInstance.ResponseMessage.SetText(response.Message ?? $"Uploaded {request.RemotePath}");
        }

        private static byte[] GetMessageBytes(string messageText, bool treatAsBase64)
        {
            string safeMessageText = messageText ?? string.Empty;
            if (treatAsBase64)
            {
                try
                {
                    return Convert.FromBase64String(safeMessageText);
                }
                catch (FormatException ex)
                {
                    throw new InvalidOperationException("The activity message was not valid base64.", ex);
                }
            }

            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(safeMessageText);
        }
    }
}
