using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;

namespace AzureActivities
{
    [DisplayName("Send Blob")]
    [Parameter("Connection String", "Connection String to your Azure Blob Storage account.", isRequired: true)]
    [Parameter("Container Name", "Name of your Azure Blob Container to upload the file to.", isRequired: true)]
    [Parameter("File Name", "Name to give your file in Blob Storage.", isRequired: true)]
    [InMessage(@"", TypeOfMessages.HL7)]
    [OutMessage(@"Code Executed Successfully", TypeOfMessages.Text)]
    internal class BlobSender : AzureActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            AzureActivitySupport.BlobUploadRequest request = CreateRequest(parameters);
            byte[] messageBytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(activityInstance.Message.Text ?? string.Empty);
            AzureActivitySupport.BlobUploadResponse response = AzureActivitySupport.Upload(request, messageBytes);

            activityInstance.ResponseMessage.SetText(response.Message ?? "Code Executed Successfully");
        }
    }
}
