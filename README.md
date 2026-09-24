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

- Release installer builds use [unattended YubiKey signing](InstallerTools/Signing/README.md). Run `InstallerTools/Signing/Setup-Signing.ps1` once under the build account to save an encrypted PIN locally; ordinary builds then sign without PIN dialogs. Unsigned review builds retain `SkipCodeSigning=true`.
- The HTML to PDF, RTF to PDF, Data from PDF, Azure, AWS, Encryption, SFTP, and ZIP installers require .NET Framework 4.8 or later. They share `InstallerTools/RequireNetFramework48.wxi` and the WiX Netfx extension's native prerequisite detection.
- New installations and upgrades stop with a .NET Framework 4.8 Runtime download message when the prerequisite is missing. Maintenance and uninstall remain available. .NET Framework 4.8.1 satisfies the check but is not required.
- Do not ship `HL7SoupIntegrations.dll` in extension installers.
- Install the activity DLL to all three product `Custom Libraries` folders.
- Install runner payload folders only to the Integration Host Server `Custom Libraries` folder.

## Installer framework prerequisite

All ten bridge-enabled Extension Library installers require .NET Framework 4.8 or later because their shared manifest writer targets net48. New installations and upgrades use InstallerTools/RequireNetFramework48.wxi to stop with a runtime download message when the prerequisite is missing; maintenance and uninstall remain available. .NET Framework 4.8.1 also satisfies the check. InstallerTools/Test-NetFramework48Prerequisite.ps1 verifies the built MSI conditions and registry lookup without installing a package.
