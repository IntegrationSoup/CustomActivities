using HL7Soup.Integrations;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace HtmlToPdfActivities
{
    [DisplayName("Convert HTML to PDF")]
    [InMessage(@"<html><body><h1>Sample PDF</h1><p>This HTML will be rendered to PDF.</p></body></html>", TypeOfMessages.Text)]
    [OutMessage(@"JVBERi0xLjQKJ...", TypeOfMessages.Binary)]
    public class HtmlToPdfConverter : CustomActivity
    {
        private const string RendererPathEnvironmentVariable = "HTMLTOPDF_RENDERER_PATH";
        private const string RendererDirectoryName = "HtmlToPdfRenderer";
        private const string RendererExecutableName = "HtmlToPdfRenderer.exe";
        private const int RendererTimeoutMilliseconds = 180000;

        private static readonly PersistentRunnerClient runnerClient = new PersistentRunnerClient("HTML-to-PDF renderer");

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

            string html = activityInstance.Message.Text ?? string.Empty;
            byte[] pdfBytes = ConvertHtmlToPdf(html);

            // HL7Soup binary messages are stored as base64 in the Text payload.
            activityInstance.ResponseMessage.SetText(Convert.ToBase64String(pdfBytes));
        }

        private static byte[] ConvertHtmlToPdf(string html)
        {
            string rendererExecutablePath = ResolveRendererExecutablePath();

            try
            {
                HtmlToPdfPipeResponse response = runnerClient.Invoke<HtmlToPdfPipeRequest, HtmlToPdfPipeResponse>(
                    rendererExecutablePath,
                    "convert-html-to-pdf",
                    new HtmlToPdfPipeRequest
                    {
                        Html = string.IsNullOrWhiteSpace(html) ? "<html><body></body></html>" : html
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
                    "The HTML-to-PDF renderer process failed. Renderer path: "
                    + rendererExecutablePath
                    + "; "
                    + ex.Message,
                    ex);
            }
        }

        private static string ResolveRendererExecutablePath()
        {
            return PersistentRunnerExecutableResolver.ResolveExecutablePath(
                typeof(HtmlToPdfConverter),
                RendererPathEnvironmentVariable,
                RendererDirectoryName,
                RendererExecutableName,
                "HTML-to-PDF renderer");
        }

        [DataContract]
        private sealed class HtmlToPdfPipeRequest
        {
            [DataMember(Order = 1)]
            public string Html { get; set; }
        }

        [DataContract]
        private sealed class HtmlToPdfPipeResponse
        {
            [DataMember(Order = 1)]
            public string PdfBase64 { get; set; }

            [DataMember(Order = 2)]
            public string BrowserPath { get; set; }

            [DataMember(Order = 3)]
            public string HeadlessMode { get; set; }
        }
    }
}
