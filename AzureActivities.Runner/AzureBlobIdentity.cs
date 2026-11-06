using Azure.Storage.Blobs;
using System;

namespace AzureActivities.Runner
{
    internal static class AzureBlobIdentity
    {
        internal static BlobClient ExactClient(BlobContainerClient container, string name)
        {
            var blob = container.GetBlobClient(name);
            string expected = Uri.UnescapeDataString(container.Uri.AbsolutePath.TrimEnd('/')) + "/" + name;
            if (!string.Equals(Uri.UnescapeDataString(blob.Uri.AbsolutePath), expected, StringComparison.Ordinal))
                throw new ArgumentException("File Name cannot be represented exactly by this Azure client. Use a key without URI-normalized dot segments.");
            return blob;
        }
    }
}
