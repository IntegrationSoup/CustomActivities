using HL7Soup.Integrations;
using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading;

namespace DataFromPdfActivities.Runner
{
    // Compiled only into the isolated runner. The v4 DLL retains its original API.
    public sealed class PdfSampleDesigner : IExtensionDesigner
    {
        public ExtensionDesignerCapabilities Describe() => new ExtensionDesignerCapabilities
        {
            ResponseTemplateUpdates = true,
            Actions = new List<ExtensionDesignerAction>
            {
                new ExtensionDesignerAction
                {
                    Id = "extract-sample-pdf", Label = "Upload sample PDF",
                    FileUpload = new ExtensionDesignerFileUpload
                    {
                        AllowedExtensions = new List<string> { ".pdf" },
                        MaxBytes = ExtensionBridgeProtocol.MaximumDesignerFileBytes
                    }
                }
            }
        };

        public ExtensionDesignerResult Update(ExtensionDesignerRequest request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (request.EventType != "Action" || request.ControlName != "extract-sample-pdf" || request.File == null)
                throw new InvalidOperationException("Choose a sample PDF to generate the JSON response sample.");
            string json = JToken.Parse(PdfDataExtractor.Extract(Convert.FromBase64String(request.File.Base64), cancellation))
                .ToString(Newtonsoft.Json.Formatting.Indented);
            cancellation.ThrowIfCancellationRequested();
            return new ExtensionDesignerResult
            {
                ResponseTemplate = new ExtensionMessage
                {
                    MessageType = (int)TypeOfMessages.JSON,
                    Text = json
                }
            };
        }
    }
}
