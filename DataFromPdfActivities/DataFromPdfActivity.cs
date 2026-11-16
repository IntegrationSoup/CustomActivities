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
    [Description("Extracts form fields and text from a PDF and returns JSON.")]
    [InMessage("", TypeOfMessages.Binary)]
    [OutMessage("{}", TypeOfMessages.UserDefined, TypeOfMessages.JSON)]
#if EXTENSION_BRIDGE_RUNNER
    [EditableResponseTemplate]
    [ExtensionDesigner(typeof(DataFromPdfActivities.Runner.PdfSampleDesigner))]
#endif
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
#if EXTENSION_BRIDGE_RUNNER
                if (ex is ExtensionReasonException)
                {
                    throw;
                }

                string reason = DescribeKnownFailure(pdfBytes, ex);
                if (reason != null)
                {
                    throw new ExtensionReasonException(reason);
                }
#endif
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
                throw Failure("The inbound binary PDF message was empty.");
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

                throw Failure("The inbound binary PDF message must contain PDF bytes encoded as base64 text.", ex);
            }
        }

        // Under the bridge the sentence is the reason the Host shows; elsewhere the exception is exactly as before.
        private static Exception Failure(string sentence, Exception inner = null)
        {
#if EXTENSION_BRIDGE_RUNNER
            return new ExtensionReasonException(sentence);
#else
            return inner == null ? new InvalidOperationException(sentence) : new InvalidOperationException(sentence, inner);
#endif
        }

#if EXTENSION_BRIDGE_RUNNER
        // Version 5 runs this library through the extension bridge, which shows the Host only a sentence
        // the library wrote on purpose. These are decided after the read has already failed, from the bytes
        // themselves or the PDF reader's exception type, never from another exception's text, and a sentence
        // never holds the message's content.
        internal const string NotPdfText = "The message is not a PDF: no PDF header was found in its first 1024 bytes.";
        internal const string EncryptedText = "The PDF is encrypted and could not be decrypted. It may need a password, which this step cannot supply.";
        internal const string DamagedText = "The PDF could not be read: its structure is not valid or not supported. The file may be damaged or incomplete.";

        private static string DescribeKnownFailure(byte[] pdfBytes, Exception failure)
        {
            if (failure is OperationCanceledException)
            {
                return null;
            }

            if (!HasPdfHeader(pdfBytes))
            {
                return NotPdfText;
            }

            for (Exception current = failure; current != null; current = current.InnerException)
            {
                if (current is UglyToad.PdfPig.Exceptions.PdfDocumentEncryptedException)
                {
                    return EncryptedText;
                }

                if (current is UglyToad.PdfPig.Core.PdfDocumentFormatException)
                {
                    return DamagedText;
                }
            }

            return null;
        }

        // A PDF reader looks for "%PDF-" within the first 1024 bytes.
        private static bool HasPdfHeader(byte[] pdfBytes)
        {
            if (pdfBytes == null)
            {
                return false;
            }

            byte[] marker = { (byte)'%', (byte)'P', (byte)'D', (byte)'F', (byte)'-' };
            int last = Math.Min(pdfBytes.Length, 1024) - marker.Length;
            for (int start = 0; start <= last; start++)
            {
                int i = 0;
                while (i < marker.Length && pdfBytes[start + i] == marker[i])
                {
                    i++;
                }

                if (i == marker.Length)
                {
                    return true;
                }
            }

            return false;
        }
#endif

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
