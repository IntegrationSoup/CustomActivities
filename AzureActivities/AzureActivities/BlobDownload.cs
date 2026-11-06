using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;

namespace AzureActivities
{
    [DisplayName("Azure Blob Download")]
    [Parameter("Connection String", "Azure Blob Storage connection string. Read-only SAS credentials are supported.", isRequired: true)]
    [ParameterUi("Connection String", Purpose = "ConnectionString")]
    [Parameter("Container Name", "Container containing the blob.", isRequired: true)]
    [Parameter("File Name", "Exact, case-sensitive blob name, including any prefix. Do not enter a URL.", isRequired: true)]
    [Parameter("Version ID", "Optional exact stored blob version to retrieve.", isRequired: false)]
    [Parameter("Expected ETag", "Optional ETag; fail if the blob has changed.", isRequired: false)]
    [Parameter("Maximum Download MB", "Maximum download size in MiB: 1 to 8. Blank uses 8 to allow for encoded bridge responses.", isRequired: false)]
    [ParameterUi("Maximum Download MB", EditorType = "Integer")]
    [Parameter("Text Encoding", "Text response encoding. Blank uses strict UTF-8. Binary and DICOM responses preserve the bytes.", isRequired: false)]
    [Variable("AzureBlobMetadataJson", @"{""name"":""inbound/message.hl7"",""size"":123,""etag"":""example"",""contentType"":""text/plain"",""versionId"":null}")]
    [InMessage("", TypeOfMessages.Text)]
    [OutMessage("TVNIfF5+XFwm", TypeOfMessages.UserDefined, TypeOfMessages.Binary)]
    public sealed class BlobDownload : CustomActivity
    {
        public override void Process(IWorkflowInstance workflow, IActivityInstance activity, Dictionary<string, string> parameters)
        {
            if (workflow == null) throw new ArgumentNullException(nameof(workflow));
            if (activity?.ResponseMessage == null) throw new InvalidOperationException("A response message is required.");
            string limit = Value(parameters, "Maximum Download MB");
            int maximum = 8;
            if (!string.IsNullOrWhiteSpace(limit) && (!int.TryParse(limit, NumberStyles.Integer, CultureInfo.InvariantCulture, out maximum) || maximum < 1 || maximum > 8))
                throw new ArgumentException("Maximum Download MB must be between 1 and 8.");
            var request = new BlobDownloadRequest
            {
                ConnectionString = Required(parameters, "Connection String").Trim(),
                ContainerName = Required(parameters, "Container Name").Trim(),
                // Spaces, slashes and case are part of the blob identity.
                FileName = Required(parameters, "File Name"),
                VersionId = Value(parameters, "Version ID"),
                ExpectedETag = Value(parameters, "Expected ETag"),
                MaximumBytes = maximum * 1024 * 1024
            };
            string encodingName = Value(parameters, "Text Encoding");
            object kind = activity.ResponseMessage.GetType().GetProperty("MessageType")?.GetValue(activity.ResponseMessage);
            bool binary = activity.ResponseMessage is IDicomMessage || activity.ResponseMessage.GetType().Name == "BinaryMessage" || string.Equals(kind?.ToString(), "Binary", StringComparison.OrdinalIgnoreCase);
            // Validate text encoding before contacting Azure.
            Encoding encoding = binary ? null : Encoding.GetEncoding(string.IsNullOrWhiteSpace(encodingName) ? "utf-8" : encodingName.Trim(), EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            BlobDownloadResponse result = AzureActivitySupport.Download(request);
            byte[] bytes = Convert.FromBase64String(result.Base64);
            activity.ResponseMessage.SetText(binary ? result.Base64 : encoding.GetString(bytes));
            workflow.SetVariable("AzureBlobMetadataJson", result.MetadataJson);
        }

        private static string Value(Dictionary<string, string> parameters, string name) => parameters != null && parameters.TryGetValue(name, out string value) ? value : null;
        private static string Required(Dictionary<string, string> parameters, string name)
        {
            string value = Value(parameters, name);
            if (name == "File Name" ? string.IsNullOrEmpty(value) : string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Parameter '" + name + "' is required.");
            return value;
        }
    }

    [DataContract]
    internal sealed class BlobDownloadRequest
    {
        [DataMember(Order = 1)] public string ConnectionString { get; set; }
        [DataMember(Order = 2)] public string ContainerName { get; set; }
        [DataMember(Order = 3)] public string FileName { get; set; }
        [DataMember(Order = 4)] public string VersionId { get; set; }
        [DataMember(Order = 5)] public string ExpectedETag { get; set; }
        [DataMember(Order = 6)] public int MaximumBytes { get; set; }
    }

    [DataContract]
    internal sealed class BlobDownloadResponse
    {
        [DataMember(Order = 1)] public string Base64 { get; set; }
        [DataMember(Order = 2)] public string MetadataJson { get; set; }
    }
}
