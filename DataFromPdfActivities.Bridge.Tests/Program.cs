using HL7Soup.Integrations.ExtensionBridge;
using Popokey.ExtensionRunners;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DataFromPdfActivities.Bridge.Tests
{
    // A failed Data from PDF step says why (T46): the library names the failures it can decide from the bytes
    // or the PDF reader's exception type, never with the message's content or the reader's text in the
    // sentence; everything else stays masked. Every PDF here is built in this file.
    internal static class Checks
    {
        private const string Id = "popokey.datafrompdf";
        private const string Content = "Patient Name: Test Person";
        private static int checks;

        private static int Main()
        {
            try
            {
                var handler = (PersistentRunnerRequestHandler)Delegate.CreateDelegate(typeof(PersistentRunnerRequestHandler), typeof(DataFromPdfActivities.Runner.Program).GetMethod("HandleServerRequest", BindingFlags.Static | BindingFlags.NonPublic));
                var provider = new BridgeProvider(Id, "DataFromPdfActivities", handler, typeof(DataFromPdfActivity));

                var ok = Process(provider, Convert.ToBase64String(Pdf(Content)));
                Check(ok.Success && PersistentRunnerJson.Deserialize<ExtensionInvokeResult>(ok.PayloadJson).ResponseMessage.Text.Contains("Test Person"), "a PDF is still read through the bridge");

                Check(Fail(provider, "   ").ErrorMessage == "The inbound binary PDF message was empty.", "an empty message is named");
                Check(Fail(provider, "this is not base64 !").ErrorMessage == "The inbound binary PDF message must contain PDF bytes encoded as base64 text.", "text that is not Base64 is named");

                var notPdf = Fail(provider, Convert.ToBase64String(Encoding.ASCII.GetBytes("MSH|^~\\&|" + Content)));
                Check(notPdf.ErrorMessage == "The message is not a PDF: no PDF header was found in its first 1024 bytes.", "bytes that are not a PDF are named");
                Check(Clean(notPdf), "not a PDF: the content is not in the response");

                var encrypted = Fail(provider, Convert.ToBase64String(EncryptedPdf()));
                Check(encrypted.ErrorMessage == "The PDF is encrypted and could not be decrypted. It may need a password, which this step cannot supply.", "an encrypted PDF is named");
                Check(Clean(encrypted), "encrypted: the content is not in the response");

                var damaged = Fail(provider, Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4\n%garbage with no objects, no xref and no trailer\n")));
                Check(damaged.ErrorMessage == "The PDF could not be read: its structure is not valid or not supported. The file may be damaged or incomplete.", "a PDF with no structure is named");
                Check(Clean(damaged), "damaged: neither the content nor the reader's text is in the response");

                Console.WriteLine($"Data from PDF bridge checks PASS: {checks} checks.");
                return 0;
            }
            catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        }

        private static bool Clean(ExtensionResponseEnvelope r) =>
            new[] { r.ErrorMessage, r.PayloadJson }.All(t => !t.Contains("Test Person") && !t.Contains("MSH|"));

        // The smallest PDF that holds one line of text: catalog, pages, page, content stream and font.
        private static byte[] Pdf(string text, string extraTrailer = "", params string[] extraObjects)
        {
            string stream = $"BT /F1 12 Tf 10 50 Td ({text}) Tj ET";
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 100] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
                $"<< /Length {stream.Length} >>\nstream\n{stream}\nendstream",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            };
            objects.AddRange(extraObjects);
            var pdf = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Count; i++) { offsets.Add(pdf.Length); pdf.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n"); }
            int xref = pdf.Length;
            pdf.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets) pdf.Append($"{offset:D10} 00000 n \n");
            pdf.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R{extraTrailer} >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(pdf.ToString());
        }

        // Standard security handler, revision 2, whose user password is not empty: the stored /U value is
        // 32 bytes that no empty password produces, so a reader without a password cannot open it.
        private static byte[] EncryptedPdf()
        {
            string filler = string.Concat(Enumerable.Repeat("0123456789ABCDEF", 4));
            return Pdf(Content, " /Encrypt 6 0 R /ID [<00112233445566778899AABBCCDDEEFF> <00112233445566778899AABBCCDDEEFF>]",
                $"<< /Filter /Standard /V 1 /R 2 /Length 40 /P -4 /O <{filler}> /U <{filler}> >>");
        }

        private static ExtensionResponseEnvelope Fail(BridgeProvider provider, string text)
        {
            var response = Process(provider, text);
            Check(!response.Success, "the step fails");
            return response;
        }

        private static ExtensionResponseEnvelope Process(BridgeProvider provider, string text)
        {
            var catalog = PersistentRunnerJson.Deserialize<ExtensionDescribeResult>(Call(provider, ExtensionBridgeProtocol.Describe, new ExtensionDescribeRequest { ProviderId = Id }).PayloadJson);
            var request = new ExtensionInvokeRequest { ProviderId = Id, TypeName = catalog.Extensions.Single().TypeName, Kind = "Activity",
                SessionId = Guid.NewGuid().ToString("N"), Phase = "Prepare", DeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(60).ToString("O"),
                Message = new ExtensionMessage { MessageType = 14, Text = text }, ResponseMessage = new ExtensionMessage { MessageType = 13, Text = "" } };
            Call(provider, ExtensionBridgeProtocol.Invoke, request);
            request.Phase = "Process"; request.Parameters = new List<ExtensionNamedValue>();
            var response = provider.Dispatch(Envelope(ExtensionBridgeProtocol.Invoke, request));
            if (response.Success) { request.Phase = "Close"; Call(provider, ExtensionBridgeProtocol.Invoke, request); }
            return response;
        }

        private static ExtensionRequestEnvelope Envelope<T>(string operation, T value) => new ExtensionRequestEnvelope { RequestId = Guid.NewGuid().ToString("N"), Operation = operation, PayloadJson = PersistentRunnerJson.Serialize(value) };

        private static ExtensionResponseEnvelope Call<T>(BridgeProvider provider, string operation, T value)
        {
            var response = provider.Dispatch(Envelope(operation, value)); if (!response.Success) throw new Exception(response.ErrorMessage); return response;
        }

        private static void Check(bool success, string description) { if (!success) throw new Exception("Failed: " + description); checks++; }
    }
}
