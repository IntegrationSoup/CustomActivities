using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Azure.Storage.Blobs;
using HL7Soup.Integrations.ExtensionBridge;

namespace Popokey.ExtensionRunners
{
    public sealed class AzureDesigner : IExtensionDesigner
    {
        public ExtensionDesignerCapabilities Describe() => new ExtensionDesignerCapabilities { Actions = new List<ExtensionDesignerAction> { new ExtensionDesignerAction { Id = "load-containers", Label = "Load containers" } } };
        public ExtensionDesignerResult Update(ExtensionDesignerRequest request, CancellationToken cancellation)
        {
            string connection = request.Parameters.FirstOrDefault(p => p.Name == "Connection String")?.Value;
            var result = new ExtensionDesignerResult();
            if (string.IsNullOrWhiteSpace(connection) || connection.Contains("${"))
            {
                result.Fields.Add(new ExtensionDesignerFieldUpdate { Name = "Connection String", ValidationMessage = "Enter a literal connection string to load containers, or enter the container name manually." });
                return result;
            }
            result.Fields.Add(new ExtensionDesignerFieldUpdate { Name = "Connection String", ValidationMessage = string.Empty });
            var options = new BlobClientOptions(); options.Retry.MaxRetries = 0; options.Retry.NetworkTimeout = TimeSpan.FromSeconds(12);
            var client = new BlobServiceClient(connection, options);
            result.Fields.Add(new ExtensionDesignerFieldUpdate { Name = "Container Name", Options = client.GetBlobContainers(cancellationToken: cancellation).Take(256).Select(c => c.Name).ToList(), ValidationMessage = string.Empty });
            return result;
        }
    }
}