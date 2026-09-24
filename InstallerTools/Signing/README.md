# Unattended CustomActivities signing

All ten `Setup.*` Release builds use `Sign-Files.ps1` for the existing payload,
bridge and MSI signing stages. Payloads are signed before WiX packaging; MSIs
are signed afterwards. No signing service or extra desktop app is required.
Keep this folder as source build tooling.

## One-time local setup

Under the Windows account that will build, with the existing code-signing
certificate in Current User / Personal, run from the repository root:

```powershell
powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\InstallerTools\Signing\Setup-Signing.ps1
```

Enter the existing YubiKey PIV PIN twice in **CustomActivities - save signing
PIN** and select **Save encrypted PIN**. Setup does not authenticate the token.
The next signed build performs that operation. The key must remain connected;
the current token uses PIN ONCE and touch NEVER. This tooling never changes
the token or exports its private key.

The PIN is encrypted with current-user Windows DPAPI and saved in
`%LOCALAPPDATA%\Popokey\CustomActivitiesSigning\pin.dpapi`, outside the checkout.
The directory allows only the current user. DPAPI protects it at rest for that
Windows account; other code running under that account can decrypt it. The
masked setup form briefly holds the input in memory. No PIN is passed through
command arguments, build properties, logs or source files.

Run the same setup command to replace the saved PIN. To remove it:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\InstallerTools\Signing\Setup-Signing.ps1 -Remove
```

## Ordinary builds

Build the activity and runner outputs first, as before. Then Visual Studio
Release builds and normal MSBuild / `dotnet build` installer builds use the
saved credential without a dialog. For example:

```powershell
dotnet build .\Setup.ZipActivities\Setup.ZipActivities.wixproj -c Release -m:1 -p:InstallerVersion=5.0.7
```

The existing full release verification entry point also uses this integration:

```powershell
.\InstallerTools\BuildBridgeSignedRelease.ps1 -InstallerVersion 5.0.7
```

For a representative package check, add `-Setups ZipActivities`. The default
still builds and verifies all ten installers. `-AlreadyBuilt ZipActivities`
verifies an existing ZIP build without rebuilding it.

It requires already-built Release payloads and restored installer projects,
extracts each MSI, verifies the embedded first-party signatures and timestamps,
and compares all installed files with the build outputs. It does not publish,
install or copy releases to download staging. Choose the intended release
version explicitly. Use serial builds (`-m:1`) and do not rebuild shared
payloads in a second build while WiX is packaging them.

`-p:SkipCodeSigning=true` remains available for unsigned review builds.
`-p:SkipTimestamp=true` skips timestamping for local signing tests; release
verification still requires timestamps. `SigntoolPath` and `TimestampUrl`
remain the existing build properties. The signing identity is pinned to
`1586FB787BD45270D870F90918EF887A121EB6DB` (Popokey Limited / HL7 Soup).

## Failure behavior and verification

The helper serializes signing across processes with an account-specific named
mutex. It sets `attempt.lock` before token use and removes it only after the
signature, expected publisher and requested timestamp have been verified.
A failed or interrupted operation leaves that lock in place, so later targets
and builds cannot automatically resubmit the PIN. Check the cause and the PIN
before explicitly rerunning local setup. A timestamp/network failure also
leaves the lock; there is deliberately no automatic signing retry.

The native Windows smart-card provider runs with silent flags on a detached
certificate context. Missing credentials or hardware fail the build rather
than displaying a PIN prompt. The existing MSI descriptions are retained via
[Windows Authenticode attributes](https://learn.microsoft.com/en-us/windows/win32/seccrypto/signer-attr-authcode).
SignTool handles RFC3161 SHA256 timestamps and trusted Authenticode verification.
Unchanged, already-valid first-party payloads with the required timestamp are
verified and reused, avoiding repeated writes to shared bridge helper files.
MSIs are signed on every signing invocation.

For a nonsecret preflight that constructs the form and checks native
compilation, synthetic DPAPI storage, permissions and the failure lock:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\InstallerTools\Signing\Setup-Signing.ps1 -ValidateOnly
```

This uses a temporary synthetic credential and never reads the saved PIN or
accesses the token. The supported setup is the current Windows account.
Dedicated service accounts, signed-out execution and remote triggering need
separate setup and validation; they are not installed by this integration.
