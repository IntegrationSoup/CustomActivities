using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace RtfToPdfActivities
{
    [DisplayName("Convert RTF to PDF")]
    [InMessage(@"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 Sample RTF content.\par}", TypeOfMessages.Text)]
    [OutMessage(@"JVBERi0xLjQKJ...", TypeOfMessages.Binary)]
    public class RtfToPdfConverter : CustomActivity
    {
        private const string RendererPathEnvironmentVariable = "RTFTOPDF_RENDERER_PATH";
        private const string RendererDirectoryName = "RtfToPdfRenderer";
        private const string RendererExecutableName = "RtfToPdfRenderer.exe";
        private const int RendererTimeoutMilliseconds = 180000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("RTF-to-PDF renderer");

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

            string rtf = activityInstance.Message.Text ?? string.Empty;
            byte[] pdfBytes = ConvertRtfToPdf(rtf);

            activityInstance.ResponseMessage.SetText(Convert.ToBase64String(pdfBytes));
        }

        private static byte[] ConvertRtfToPdf(string rtf)
        {
            string rendererExecutablePath = ResolveRendererExecutablePath();

            try
            {
                RtfToPdfPipeResponse response = runnerClient.Invoke<RtfToPdfPipeRequest, RtfToPdfPipeResponse>(
                    rendererExecutablePath,
                    "convert-rtf-to-pdf",
                    new RtfToPdfPipeRequest
                    {
                        Rtf = string.IsNullOrWhiteSpace(rtf)
                            ? @"{\rtf1\ansi\deff0{\fonttbl{\f0 Calibri;}}\f0\fs24 \par}"
                            : rtf
                    },
                    RendererTimeoutMilliseconds);

                if (response == null || string.IsNullOrWhiteSpace(response.PdfBase64))
                {
                    throw new InvalidOperationException("The renderer completed without returning a PDF payload.");
                }

                return Convert.FromBase64String(response.PdfBase64);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "The RTF-to-PDF renderer process failed. Renderer path: "
                    + rendererExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

        private static string ResolveRendererExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(RtfToPdfConverter),
                RendererPathEnvironmentVariable,
                RendererDirectoryName,
                RendererExecutableName,
                "RTF-to-PDF renderer");
        }

        [DataContract]
        private sealed class RtfToPdfPipeRequest
        {
            [DataMember(Order = 1)]
            public string Rtf { get; set; }
        }

        [DataContract]
        private sealed class RtfToPdfPipeResponse
        {
            [DataMember(Order = 1)]
            public string PdfBase64 { get; set; }

            [DataMember(Order = 2)]
            public string LibreOfficePath { get; set; }
        }
    }
}
