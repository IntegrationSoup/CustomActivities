using HL7Soup.Integrations;
using System.Collections.Generic;
using System.ComponentModel;

namespace ZipActivities
{
    [DisplayName("Create ZIP File")]
    [Parameter("Source Directory", "Full path to the directory whose contents will be added to the ZIP archive.", isRequired: true)]
    [Parameter("ZIP File Path", "Full path of the ZIP file to create.", isRequired: true)]
    [Parameter("Include Base Directory", "True to place the source directory itself at the root of the ZIP archive.", isRequired: false)]
    [ParameterUi("Include Base Directory", EditorType = "Checkbox")]
    [Parameter("Overwrite Existing File", "True to replace an existing ZIP file at the destination path.", isRequired: false)]
    [ParameterUi("Overwrite Existing File", EditorType = "Checkbox")]
    [InMessage("", TypeOfMessages.Text)]
    [OutMessage("Created ZIP file.", TypeOfMessages.Text)]
    public class CreateZipFile : ZipActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            ZipActivitySupport.ZipRequest request = CreateDirectoryRequest(parameters);
            request.ZipFilePath = GetRequiredParameter(parameters, "ZIP File Path");
            request.OverwriteExistingFile = GetOptionalBoolean(parameters, "Overwrite Existing File");

            ZipActivitySupport.ZipResponse response = ZipActivitySupport.CreateZipFile(request);
            activityInstance.ResponseMessage.SetText(response.Message);
        }
    }
}
