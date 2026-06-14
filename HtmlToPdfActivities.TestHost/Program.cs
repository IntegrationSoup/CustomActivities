using HL7Soup.Integrations;
using HtmlToPdfActivities;
using System;
using System.Collections.Generic;
using System.IO;

namespace HtmlToPdfActivities.TestHost
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                string htmlInput = ResolveHtmlInput(args);
                string outputPdfPath = ResolveOutputPath(args);

                HtmlToPdfConverter converter = new HtmlToPdfConverter();
                TestMessage requestMessage = new TestMessage(htmlInput);
                TestMessage responseMessage = new TestMessage(string.Empty);
                TestActivityInstance activityInstance = new TestActivityInstance("HtmlToPdf test", requestMessage, responseMessage);

                converter.Process(null, activityInstance, new Dictionary<string, string>());

                byte[] pdfBytes = Convert.FromBase64String(responseMessage.Text ?? string.Empty);
                File.WriteAllBytes(outputPdfPath, pdfBytes);

                Console.WriteLine($"PDF created: {outputPdfPath}");
                Console.WriteLine($"Bytes written: {pdfBytes.Length}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Conversion failed:");
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static string ResolveHtmlInput(string[] args)
        {
            if (args.Length > 0)
            {
                string firstArg = args[0];
                if (File.Exists(firstArg))
                {
                    return File.ReadAllText(firstArg);
                }

                return firstArg;
            }

            return "<html><body><h1>HtmlToPdf test</h1><p>Generated from TestHost.</p></body></html>";
        }

        private static string ResolveOutputPath(string[] args)
        {
            if (args.Length > 1 && !string.IsNullOrWhiteSpace(args[1]))
            {
                return Path.GetFullPath(args[1]);
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output.pdf");
        }

        private sealed class TestActivityInstance : IActivityInstance
        {
            public TestActivityInstance(string name, IMessage message, IMessage responseMessage)
            {
                Name = name;
                Message = message;
                ResponseMessage = responseMessage;
                Id = Guid.NewGuid();
            }

            public bool Filtered => false;
            public Guid Id { get; }
            public IMessage Message { get; }
            public IMessage ResponseMessage { get; }
            public string Name { get; }
        }

        private sealed class TestMessage : IMessage
        {
            public TestMessage(string text)
            {
                Text = text;
            }

            public string Text { get; private set; }

            public string GetValueAtPath(string path)
            {
                throw new NotSupportedException("Path operations are not required for this test harness.");
            }

            public void SetValueAtPath(string toPath, string fromValue)
            {
                throw new NotSupportedException("Path operations are not required for this test harness.");
            }

            public void SetStructureAtPath(string toPath, string fromValue)
            {
                throw new NotSupportedException("Path operations are not required for this test harness.");
            }

            public void SetText(string text)
            {
                Text = text;
            }

            public void Dispose()
            {
                // No unmanaged resources.
            }
        }
    }
}
