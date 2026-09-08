using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using HL7Soup.Integrations.ExtensionBridge;

namespace Popokey.ExtensionRunners
{
    public sealed class AwsDesigner : IExtensionDesigner
    {
        public ExtensionDesignerCapabilities Describe() => new ExtensionDesignerCapabilities { Actions = new List<ExtensionDesignerAction> { new ExtensionDesignerAction { Id = "load-buckets", Label = "Load buckets" } } };
        public ExtensionDesignerResult Update(ExtensionDesignerRequest request, CancellationToken cancellation)
        {
            string Value(string name) => request.Parameters.FirstOrDefault(p => p.Name == name)?.Value;
            var result = new ExtensionDesignerResult();
            bool missingSettings = false;
            foreach (string name in new[] { "Access Key ID", "Secret Access Key", "Region" })
            {
                string value = Value(name);
                bool missing = string.IsNullOrWhiteSpace(value) || value.Contains("${");
                missingSettings |= missing;
                result.Fields.Add(new ExtensionDesignerFieldUpdate { Name = name, ValidationMessage = missing ? "Enter a literal value to load buckets, or enter the bucket name manually." : string.Empty });
            }
            if (missingSettings) return result;
            using (var client = new AmazonS3Client(Value("Access Key ID"), Value("Secret Access Key"), new AmazonS3Config { RegionEndpoint = RegionEndpoint.GetBySystemName(Value("Region")), Timeout = TimeSpan.FromSeconds(12) }))
            {
                var response = client.ListBucketsAsync(new ListBucketsRequest(), cancellation).GetAwaiter().GetResult();
                result.Fields.Add(new ExtensionDesignerFieldUpdate { Name = "Bucket Name", Options = response.Buckets.Select(b => b.BucketName).OrderBy(n => n, StringComparer.Ordinal).Take(256).ToList(), ValidationMessage = string.Empty });
            }
            return result;
        }
    }
}