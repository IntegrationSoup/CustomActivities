using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AzureActivities
{
    [DisplayName("Azure Blob Upload")]
    [Parameter("Connection String", "Connection String to your Azure Blob Storage account.", isRequired: true)]
    [ParameterUi("Connection String", Purpose = "ConnectionString")]
    [Parameter("Container Name", "Name of your Azure Blob Container to upload the file to.", isRequired: true)]
    [Parameter("File Name", "Name to give your file in Blob Storage.", isRequired: true)]
    [InMessage(@"", TypeOfMessages.UserDefined)]
    [OutMessage(@"Code Executed Successfully", TypeOfMessages.Text)]
    public class BlobSender : AzureActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            AzureActivitySupport.BlobUploadRequest request = CreateRequest(parameters);
            byte[] messageBytes = GetMessageBytes(activityInstance.Message);
            AzureActivitySupport.BlobUploadResponse response = AzureActivitySupport.Upload(request, messageBytes);

            activityInstance.ResponseMessage.SetText(response.Message ?? "Code Executed Successfully");
        }
    }
}
