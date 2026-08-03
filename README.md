# CustomActivities

This repository contains Integration Soup extension libraries, their out-of-process runner executables, and the WiX installer projects used to ship them.

## Extension Runner Model
- Activity DLLs loaded by Integration Soup stay lightweight and talk to a dedicated runner executable over named pipes.
- Each extension type keeps one long-lived runner process per host process.
- Calls are synchronous from the activity DLL point of view. If a request is queued behind earlier work, the caller still waits until its own runner operation has completed.
- Runner executables still support one-shot command-line execution for manual troubleshooting.

## Key Folders
- `ExtensionRunnerHosting`
  - shared named-pipe/process-lifetime code for the persistent runner model
- `Setup.*`
  - WiX installer projects for shipping extension libraries
- `ZipActivities` / `ZipActivities.Runner`
  - ZIP file creation, binary ZIP message creation, and safe archive extraction activities
  - [Website tutorial](https://www.integrationsoup.com/ExtensionLibraries/ZipActivities.html)
  - [Product documentation](https://integrationsoup.github.io/Documentation/integration-workflows/extension-libraries/zip-activities/)
- `CopyInstallersToDownloadsReadyforDownloadDelpoyment.cmd`
  - copies finished MSIs to the website downloads staging folder

## Documentation
- Website/tutorial/download content should be updated in:
  - `C:\Users\jason\source\repos\HL7SoupWebsite\HL7SoupWebsite`
- Product/code documentation should be updated in:
  - `C:\Users\jason\source\repos\Documentation`

## Packaging Rules
- Do not ship `HL7SoupIntegrations.dll` in extension installers.
- Install the activity DLL to all three product `Custom Libraries` folders.
- Install runner payload folders only to the Integration Host Server `Custom Libraries` folder.
