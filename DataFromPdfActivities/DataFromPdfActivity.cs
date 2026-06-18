using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;

namespace DataFromPdfActivities
{
    [DisplayName("Data from PDF")]
    [InMessage("", TypeOfMessages.Binary)]
    [OutMessage("{}", TypeOfMessages.UserDefined, TypeOfMessages.JSON)]
    public class DataFromPdfActivity : CustomActivity
    {
        private const string RunnerPathEnvironmentVariable = "DATAFROMPDF_RUNNER_PATH";
        private const string RunnerDirectoryName = "DataFromPdfRunner";
        private const string RunnerExecutableName = "DataFromPdfRunner.exe";
        private const int RunnerTimeoutMilliseconds = 120000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("Data from PDF runner");

        public override void Process(IWorkflowInstance workflowInstance, IActivityInstance activityInstance, Dictionary<string, string> parameters)
        {
            if (activityInstance == null)
            {
                throw new ArgumentNullException(nameof(activityInstance));
            }

            if (activityInstance.Message == null)
            {
                throw new InvalidOperationException("Error: Activity Message not set.");
            }

            if (activityInstance.ResponseMessage == null)
            {
                throw new InvalidOperationException("Error: Response Message not set.");
            }

            byte[] pdfBytes = DecodeInboundBinaryMessage(activityInstance.Message.Text);
            string json = ExtractDataFromPdf(pdfBytes);
            activityInstance.ResponseMessage.SetText(json);
        }

        private static string ExtractDataFromPdf(byte[] pdfBytes)
        {
            string runnerExecutablePath = ResolveRunnerExecutablePath();

            try
            {
                PdfDataPipeResponse response = runnerClient.Invoke<PdfDataPipeRequest, PdfDataPipeResponse>(
                    runnerExecutablePath,
                    "extract-data-from-pdf",
                    new PdfDataPipeRequest
                    {
                        PdfBase64 = Convert.ToBase64String(pdfBytes ?? new byte[0])
                    },
                    RunnerTimeoutMilliseconds);

                if (response == null || string.IsNullOrWhiteSpace(response.Json))
                {
                    throw new InvalidOperationException("The runner completed without returning a JSON payload.");
                }

                return response.Json;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The Data from PDF runner process failed. Runner path: "
                    + runnerExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

        private static byte[] DecodeInboundBinaryMessage(string messageText)
        {
            if (string.IsNullOrWhiteSpace(messageText))
            {
                throw new InvalidOperationException("The inbound binary PDF message was empty.");
            }

            string value = messageText.Trim();
            int dataUriMarker = value.IndexOf("base64,", StringComparison.OrdinalIgnoreCase);
            if (dataUriMarker >= 0)
            {
                value = value.Substring(dataUriMarker + "base64,".Length);
            }

            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException ex)
            {
                if (value.StartsWith("%PDF", StringComparison.Ordinal))
                {
                    return messageText.Select(ch => (byte)ch).ToArray();
                }

                throw new InvalidOperationException("The inbound binary PDF message must contain PDF bytes encoded as base64 text.", ex);
            }
        }

        private static string ResolveRunnerExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(DataFromPdfActivity),
                RunnerPathEnvironmentVariable,
                RunnerDirectoryName,
                RunnerExecutableName,
                "Data from PDF runner");
        }

        [DataContract]
        private sealed class PdfDataPipeRequest
        {
            [DataMember(Order = 1)]
            public string PdfBase64 { get; set; }
        }

        [DataContract]
        private sealed class PdfDataPipeResponse
        {
            [DataMember(Order = 1)]
            public string Json { get; set; }
        }
    }
}
