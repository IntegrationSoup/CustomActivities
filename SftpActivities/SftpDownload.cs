using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace SftpActivities
{
    [DisplayName("SFTP Download")]
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
    [Parameter("Remote Path", "Full remote file path, for example /inbound/messages/message.hl7.", isRequired: true)]
    [Parameter("Delete Remote File After Download", "True to remove the remote file after a successful download.", isRequired: false)]
    [ParameterUi("Delete Remote File After Download", EditorType = "Checkbox")]
    [InMessage(@"", TypeOfMessages.Text)]
    [OutMessage(@"TVNIfF5+XCZ8U2VuZGluZ0FwcHxTZW5kaW5nRmFjaWxpdHl8UmVjZWl2aW5nQXBwfFJlY2VpdmluZ0ZhY2lsaXR5fDIwMjUwMzAxMDkwMHx8QURUXkEwMXwxMjM0NXxQfDIuNS4x", TypeOfMessages.UserDefined, TypeOfMessages.Binary)]
    public class SftpDownload : SftpActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance, requireMessage: false);

            SftpActivitySupport.SftpCommandRequest request = CreateRequest(parameters, operation: "download");
            request.DeleteRemoteFileAfterDownload = GetOptionalBoolean(parameters, "Delete Remote File After Download");

            SftpActivitySupport.SftpDownloadResult response = SftpActivitySupport.Download(request);
            SetResponseMessageBytes(activityInstance.ResponseMessage, response.Bytes);
        }
    }
}
