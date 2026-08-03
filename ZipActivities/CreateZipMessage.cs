using HL7Soup.Integrations;
using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace ZipActivities
{
    [DisplayName("Create ZIP Message")]
    [Parameter("Source Directory", "Full path to the directory whose contents will be added to the ZIP archive.", isRequired: true)]
    [Parameter("Include Base Directory", "True to place the source directory itself at the root of the ZIP archive.", isRequired: false)]
    [ParameterUi("Include Base Directory", EditorType = "Checkbox")]
    [InMessage("", TypeOfMessages.Text)]
    [OutMessage("UEsFBgAAAAAAAAAAAAAAAAAAAAAAAA==", TypeOfMessages.Binary)]
    public class CreateZipMessage : ZipActivityBase
    {
        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            EnsureActivityInstanceReady(activityInstance);

            ZipActivitySupport.ZipResponse response = ZipActivitySupport.CreateZipMessage(CreateDirectoryRequest(parameters));
            activityInstance.ResponseMessage.SetText(response.OutputBase64 ?? Convert.ToBase64String(Array.Empty<byte>()));
        }
    }
}
