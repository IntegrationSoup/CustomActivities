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
    [Parameter("Create Container If Missing", "False (default) uploads to an existing container. True also permits container creation.", isRequired: false)]
    [ParameterUi("Create Container If Missing", Options = new[] { "False", "True" })]
    [Parameter("New Container Access", "Private (default) or Blob for anonymous blob reads. Applies only when creating a container; existing permissions are preserved. Azure must allow public access.", isRequired: false)]
    [ParameterUi("New Container Access", Options = new[] { "Private", "Blob" })]
    [Parameter("Overwrite Existing Blob", "False (default) fails if the blob exists. True replaces content without deleting the blob or its snapshots first.", isRequired: false)]
    [ParameterUi("Overwrite Existing Blob", Options = new[] { "False", "True" })]
    [Parameter("Expected ETag", "Optional current blob ETag required for replacement. Requires Overwrite Existing Blob=True.", isRequired: false)]
    [InMessage(@"", TypeOfMessages.UserDefined)]
    [OutMessage(@"Code Executed Successfully", TypeOfMessages.Text)]
    [Variable("AzureBlobETag", "\"example\"")]
    [Variable("AzureBlobVersionId", "")]
#if EXTENSION_BRIDGE_RUNNER
    [Popokey.ExtensionRunners.ExtensionDesigner(typeof(Popokey.ExtensionRunners.AzureDesigner))]
#endif
    public class BlobSender : AzureActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            AzureActivitySupport.BlobUploadRequest request = CreateRequest(parameters);
            byte[] messageBytes = GetMessageBytes(activityInstance.Message);
            AzureActivitySupport.BlobUploadResponse response = AzureActivitySupport.Upload(request, messageBytes);

            activityInstance.ResponseMessage.SetText(response.Message ?? "Code Executed Successfully");
            workflowInstance.SetVariable("AzureBlobETag", response.ETag);
            workflowInstance.SetVariable("AzureBlobVersionId", response.VersionId ?? string.Empty);
        }
    }
}
