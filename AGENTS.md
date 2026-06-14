# CustomActivities Repository Guidance

## Architecture
- The host-loaded activity DLLs should stay thin and dependency-light. Prefer `HL7SoupIntegrations` as the only direct dependency in the activity DLL project.
- Isolated extensions use a paired runner executable in a child folder under `Custom Libraries`.
- Shared named-pipe and process-hosting code lives in `ExtensionRunnerHosting`.
- Current isolated extensions include:
  - `HtmlToPdfActivities`
  - `RtfToPdfActivities`
  - `AzureActivities`
  - `AmazonActivities`
  - `HL7SoupEncryptionActivities`
  - `SftpActivities`

## Runtime Contract
- DLL-side calls are synchronous. Do not convert them to fire-and-forget behavior.
- `PersistentRunnerClient` serializes one request at a time per extension type and blocks the caller until the full response has been received.
- Queued callers must remain blocked until their own request has finished.
- The activity DLLs use persistent named-pipe mode only.
- One-shot command-line mode remains supported in the runner executables for direct/manual execution and troubleshooting, but it is not a DLL fallback path.
- Automatic retry is only allowed before request dispatch begins. Do not auto-replay a request after partial dispatch or after the runner has started executing it.
- Runner state is memory-only and may be reused between requests while the runner process stays alive.

## Packaging And Deployment
- Installers must not include `HL7SoupIntegrations.dll`.
- Activity DLLs are installed to all three product `Custom Libraries` folders.
- Runner payload folders are installed only to `Integration Host Server\Custom Libraries\<RunnerFolder>`.
- MSI output names should use the `IntegrationSoup.` prefix.
- The website download-copy helper is `CopyInstallersToDownloadsReadyforDownloadDelpoyment.cmd`.
- Released installers are staged to:
  - `C:\Users\jason\hl7soup.com\Development - Documents\Website\downloads\CustomActivities`

## Documentation Locations
- Product website/tutorial pages belong in:
  - `C:\Users\jason\source\repos\HL7SoupWebsite\HL7SoupWebsite`
- Product/code documentation belongs in:
  - `C:\Users\jason\source\repos\Documentation`
- When a new extension library is added or behavior changes materially, update:
  - the website download/tutorial content
  - the `Documentation` repo content
  - the installer/download staging list if a new MSI is introduced

## Notes For Future Changes
- If the named-pipe protocol changes, update both client and server together.
- If a runner must use temp files internally, keep them inside the runner process only.
- Preserve parent-process monitoring so orphaned runners exit when the host goes away.
