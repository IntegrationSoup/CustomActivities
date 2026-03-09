using HL7Soup.Integrations;
using Microsoft.Playwright;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;

namespace HtmlToPdfActivities
{
    [DisplayName("Convert HTML to PDF")]
    [InMessage(@"<html><body><h1>Sample PDF</h1><p>This HTML will be rendered to PDF.</p></body></html>", TypeOfMessages.Text)]
    [OutMessage(@"JVBERi0xLjQKJ...", TypeOfMessages.Binary)]
    public class HtmlToPdfConverter : CustomActivity
    {
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
            byte[] pdfBytes = ConvertHtmlToPdfAsync(html).GetAwaiter().GetResult();

            // HL7Soup binary messages are stored as base64 in the Text payload.
            activityInstance.ResponseMessage.SetText(Convert.ToBase64String(pdfBytes));
        }

        private static async Task<byte[]> ConvertHtmlToPdfAsync(string html)
        {
            try
            {
                using IPlaywright playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true
                }).ConfigureAwait(false);

                IPage page = await browser.NewPageAsync().ConfigureAwait(false);
                await page.SetContentAsync(html, new PageSetContentOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle
                }).ConfigureAwait(false);

                return await page.PdfAsync(new PagePdfOptions
                {
                    Format = "A4",
                    PrintBackground = true
                }).ConfigureAwait(false);
            }
            catch (PlaywrightException ex)
            {
                throw new InvalidOperationException(
                    "Playwright could not render the PDF. Ensure Chromium is installed for this deployment by running 'playwright install chromium' for the published activity binaries.",
                    ex);
            }
        }
    }
}
