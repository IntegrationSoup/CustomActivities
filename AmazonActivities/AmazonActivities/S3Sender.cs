using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace AmazonActivities
{
    [DisplayName("AWS S3 Upload")]
    [Parameter("Bucket Name", "Name of target AWS S3 Bucket", isRequired: true)]
    [Parameter("File Name", "Name to give your file in S3", isRequired: true)]
    [Parameter("Region", "Region your S3 Bucket is located in. NOTE: Must be in System Name format (eg: us-west-1)", isRequired: true)]
    [Parameter("Access Key ID", "ID of your AWS Access Key", isRequired: true)]
    [Parameter("Secret Access Key", "Your AWS Secret Access Key", isRequired: true)]
    [ParameterUi("Secret Access Key", Purpose = "Secret")]
    [InMessage(@"", TypeOfMessages.UserDefined)]
    [OutMessage(@"Code Execute Successfully", TypeOfMessages.Text)]
    public class S3Sender : AwsActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            AwsActivitySupport.S3UploadRequest request = CreateRequest(parameters);
            byte[] messageBytes = GetMessageBytes(activityInstance.Message);
            AwsActivitySupport.S3UploadResponse response = AwsActivitySupport.Upload(request, messageBytes);

            activityInstance.ResponseMessage.SetText(response.Message ?? "Code Execute Successfully");
        }
    }
}
