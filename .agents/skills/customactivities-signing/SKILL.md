---
name: customactivities-signing
description: Set up, use, or troubleshoot unattended YubiKey signing for CustomActivities Release installer builds, and choose unsigned review builds when signing is unavailable. Does not deploy or publish installers.
---

# CustomActivities signing

Read [the maintained signing guide](../../../InstallerTools/Signing/README.md) before setup, signing, or troubleshooting. Run the documented commands from the repository root and use its existing helpers rather than recreating token or PIN handling.

- Signed builds require the attached YubiKey, the configured signing certificate, and an account-local encrypted PIN under the Windows account doing the build. If setup is already ready, reuse it without asking for the PIN again. Current-account background builds are proven; service-account, signed-out, and remote-trigger operation are not.
- Initial setup or PIN replacement uses `InstallerTools/Signing/Setup-Signing.ps1`, which opens a local masked form. Never request the PIN in chat or put it in arguments, logs, files in the repository, or screenshots. The same script's `-Remove` option removes the saved credential; `-ValidateOnly` runs a nonsecret preflight without using the token.
- Ordinary Visual Studio/MSBuild Release installer builds invoke `InstallerTools/Signing/Sign-Files.ps1` through the existing payload, bridge, and MSI stages. Preserve this order, publisher identity, descriptions, timestamps, verification, and the explicit unsigned switches.
- For release verification, use `InstallerTools/BuildBridgeSignedRelease.ps1 -InstallerVersion <intended-version>` after building the Release payloads and restoring the installer projects. It defaults to all ten installers; `-Setups ZipActivities` selects a representative package. Use serial builds (`-m:1`) and avoid concurrent rebuilding of shared payloads while installers are packaging them.
- A failed or interrupted signing attempt blocks later attempts persistently. Stop and diagnose; do not retry authentication or delete/reset the failure lock automatically. Explicit local setup is the recovery path after checking the cause and PIN. Do not change token policies or export the private key.
- On other machines or when an unsigned review is requested, use `-p:SkipCodeSigning=true`, or `InstallerTools/BuildBridgeReview.ps1 -HL7SoupRoot <host-checkout> -InstallerVersion <intended-version>`. Report the result as unsigned. Never silently substitute unsigned output for a requested signed release; the release verifier requires signatures and timestamps.

Building or signing does not authorize installation, download staging, publishing, a remote endpoint, or an always-running signing service. Follow the user's requested scope.
