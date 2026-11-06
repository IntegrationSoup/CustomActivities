# Azure Blob Download and container creation

The Azure Activities Extension Library includes **Azure Blob Upload** and **Azure Blob Download**. The thin host DLL calls the paired .NET Framework runner synchronously over the existing named pipe. Version 5 discovers both activities through the same runner's extension bridge. This package does not add an Azure Function host or change the bridge session contract.

## Download contract

- No input message is needed. Supply **Connection String**, **Container Name** and the exact **File Name** (blob key, not a URL). File Name preserves case, Unicode, prefixes and significant whitespace.
- A connection string containing a read-only SAS works. The download performs only blob properties and content reads; it does not list, create, upload, delete or change access policies.
- The response defaults to **Binary**. Binary and DICOM preserve file bytes. Select a text response and **Text Encoding** to decode it. Blank encoding uses strict UTF-8; invalid bytes fail. A UTF-8 BOM is preserved as U+FEFF, not removed. HTTP content type is reported as metadata and does not choose a decoder or trigger decompression.
- **Maximum Download MB** defaults to 8 MiB and accepts 1–8. Both the initial properties and streamed byte count enforce the bound. The conservative limit allows base64 and nested JSON escaping inside the default 64 MiB version 5 envelope. Large files must use another transfer path.
- Optional **Version ID** selects a stored version. **Expected ETag** rejects a changed object. The GET is pinned to the ETag read by HEAD even without an explicit Expected ETag. Incomplete reads fail.
- **AzureBlobMetadataJson** contains `name`, `size`, `etag`, `contentType`, `versionId` and `lastModifiedUtc`. The activity does not persist a polling checkpoint or delete a processed blob.
- Download's SDK client has automatic retries disabled. No activity is replayed after uncertain dispatch. Missing/denied objects, expired credentials, invalid encodings, exceeded bounds and transport failures fail the workflow invocation. Version 5 returns its existing sanitized execution failure.

## Upload creation access and compatibility

**New Container Access** is optional: blank or `Private` requests a private ACL when creating a container; `Blob` explicitly requests anonymous blob reads in that new container. It does not request anonymous container listing. Azure can reject `Blob` when account policy disallows public access; the activity fails without weakening that policy or falling back to a different ACL.

**Create Container If Missing** defaults to blank/`False`, so normal uploads only write to an existing container. Select `True` to provision a missing container and permit the creation request; only this mode uses New Container Access. `Blob` access without explicit creation is rejected. Keeping creation off lets the storage account be provisioned separately and the workflow use narrower write credentials.

The creation request is atomic via `CreateIfNotExists(publicAccessType)`. No subsequent `SetAccessPolicy` call is made. Existing containers retain their permissions, including public containers. Azure's `$web` static website endpoint has separate public-serving semantics: this option is not a guarantee that content in `$web` or an existing public container is private.

Existing saved workflows contain none of the creation or public-access options. An omitted creation option requires an existing container. To create future new containers, explicitly select Create Container If Missing=True; they request private creation unless Blob access is also selected. No saved workflow or existing Azure ACL is migrated automatically.

**Overwrite Existing Blob** defaults to blank/`False`. The upload uses `If-None-Match: *` to fail if the blob already exists. Select `True` deliberately to replace content; optional **Expected ETag** then requires an atomic ETag match. Expected ETag without explicit replacement is rejected.

Uploads accept at most 8 MiB of content and use a known-length single PUT. Larger input is rejected before any Azure request, rather than using staged-block uploads or OpenWrite. No blob or snapshot is deleted before replacement. The resulting ETag and version ID are returned in **AzureBlobETag** and **AzureBlobVersionId**.

Replacement writes content using the upload's default HTTP properties and empty metadata; it does not merge the old metadata, properties or tags. No tag-preservation guarantee is made: ETags do not detect tag-only changes. Snapshots are retained by Azure. A lost response may mean a write completed; inspect the blob before manually retrying, and never interpret a later 412 as proof that an earlier attempt did not write.

Existing workflows that omit the new replacement option fail on duplicate blob names. Set `Overwrite Existing Blob=True` only where replacement is intended. No workflow configuration is migrated automatically.

## Verification

Run `dotnet run --project AzureActivities.Tests -c Release`. The executable uses a synthetic Azure HTTP transport and the real SDK/bridge serializer. It tests exact identity, read-only SAS, ETag/version guards, empty/binary/DICOM/text content, explicit encoding and BOM behavior, oversized/incomplete streams, control-heavy serialized envelopes, failures, and default/explicit creation access for both new and existing containers. No real cloud data or customer workflows are read or modified.

Use the existing signed installer workflow and verify its actual embedded payloads before release. Installers exclude `HL7SoupIntegrations.dll`, copy the activity DLL to all three product folders and place the paired runner only under the Integration Host Server folder. .NET Framework 4.8 or later is required.
