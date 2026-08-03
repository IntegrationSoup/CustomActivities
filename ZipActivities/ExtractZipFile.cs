using HL7Soup.Integrations;
using System.Collections.Generic;
using System.ComponentModel;

namespace ZipActivities
{
    [DisplayName("Extract ZIP File")]
    [Parameter("ZIP File Path", "Full path to the ZIP file to extract.", isRequired: true)]
    [Parameter("Destination Directory", "Full path to the directory that will receive the extracted files and folders.", isRequired: true)]
    [Parameter("Overwrite Existing Files", "True to replace files that already exist in the destination directory.", isRequired: false)]
    [ParameterUi("Overwrite Existing Files", EditorType = "Checkbox")]
    [InMessage("", TypeOfMessages.Text)]
    [OutMessage("Extracted ZIP file.", TypeOfMessages.Text)]
    public class ExtractZipFile : ZipActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            ZipActivitySupport.ZipRequest request = new ZipActivitySupport.ZipRequest
            {
                ZipFilePath = GetRequiredParameter(parameters, "ZIP File Path"),
                DestinationDirectory = GetRequiredParameter(parameters, "Destination Directory"),
                OverwriteExistingFiles = GetOptionalBoolean(parameters, "Overwrite Existing Files")
            };

            ZipActivitySupport.ZipResponse response = ZipActivitySupport.ExtractZipFile(request);
            activityInstance.ResponseMessage.SetText(response.Message);
        }
    }
}
