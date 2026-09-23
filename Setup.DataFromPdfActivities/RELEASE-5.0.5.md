# Data from PDF 5.0.5

This release adds the approved preceding-label column recovery. Sample1 adds
`data.fields.unlabeledAfterVisitNoLine1/2/3`; sample2 adds
`data.fields.unlabeledAfterAccLine1/2/3`. Existing fields and values are preserved,
including populated ACC and the historical successful template's original keys.
Combined block and page properties accompany the new line fields. Repeated
recovery and anchored-key collisions are covered by the replay harness.

## Source and consolidation

The original repair worktree was based on legacy `main` at `84fe7e9`. The staged
5.0.4 installer came from `codex/v5-extension-runners`: release evidence records
base `4bab0fd` plus framework-prerequisite changes committed as `89cc3ea`.
Its recorded MSI SHA256 exactly matched the staged package:
`8F5BA06AB2E9045014B8D0A0E74CF43827F30EA18B9F72E3D6A63BFB2784AA22`.

Branch `codex/pdf-anchored-recovery` preserves both histories: repair `ab4d6d6`,
merge `037d68f`, and merge cleanup `c67698c`. The cleanup removed duplicate
prerequisite includes introduced by the automatic merge, retaining one per
installer. The saved-project main checkout and original bridge checkout were
clean and remain intact. No branches, worktrees, or user changes were discarded.

The build used an isolated export of `89cc3ea` with only the approved recovery
class and extractor call added. All 42 relevant source/build files were checked
against the consolidated committed source, ignoring line endings. The bridge
dispatch, registration, writer, and existing payloads remain present. No legacy-only
1.0.11 package was released; that initial packaging attempt was stopped.

## Build and verify

Use an existing prebuilt host API as a compile reference; never package it:

```powershell
dotnet build DataFromPdfActivities/DataFromPdfActivities.csproj -c Release -f net48 -m:1 -p:HL7SoupIntegrationsAssembly=<absolute-path-to-HL7SoupIntegrations.dll>
dotnet build Setup.DataFromPdfActivities/Setup.DataFromPdfActivities.wixproj -c Release -m:1 -p:InstallerVersion=5.0.5 '-p:SigntoolPath=C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe'
```

The existing targets sign both runner copies, the activity DLL, the manifest
writer, and MSI with the configured Popokey certificate and trusted timestamp.
Signing can require interactive approval. One stalled MSI attempt was stopped
and a single fresh signing attempt succeeded; no credentials were changed.

`InstallerTools/VerifyDataFromPdfPackage.ps1` checks upgrade identity, file
inventory/destinations, directory hierarchy, registration and service controls,
bridge custom actions (allowing only the revision argument change), all embedded
file hashes against this build, and owned payload/MSI signatures and timestamps.
The existing bridge verifier was run with only its ten-package count guard scoped
to this single MSI. The existing .NET 4.8 prerequisite verifier also passed.

Results: Release build had zero warnings/errors; all three JSON replays and
synthetic regressions passed; all 34 embedded files matched this build; seven
owned signatures (MSI, three activity copies, two runner copies, writer) were
valid and timestamped. Host API DLL was excluded. Package inspection did not
run an installation sequence.

## Local artifacts and staging

Under this worktree, `artifacts/pdf-installer/5.0.5/` contains the signed MSI,
build source snapshot, logs, `build-provenance.json`,
`committed-source-verification.json`, `inspection/verification.json`,
`replay-private/`, and `staging-verification.json`. Artifact folders are excluded
from Git; replay data can retain personal information. The signed MSI SHA256 is:

`7CBE19892893C5AADB49161E61DEA44C532DB25D849F93AEFF5167AA72DBBEDD`

Only `IntegrationSoup.DataFromPdfActivities.msi` was replaced in
`C:\Users\jason\hl7soup.com\Development - Documents\Website\downloads\CustomActivities`.
The predecessor is preserved as
`artifacts/pdf-installer/5.0.5/backup/IntegrationSoup.DataFromPdfActivities.5.0.4.msi`.
Artifact/staged hashes match; all other staged installers remain unchanged.
This is local download staging, not a public website upload.

JSON replay uses `pages[].lines[].text/x/y` and existing fields, not original
PDFs or fabricated word geometry. It cannot validate PDF ingestion/segmentation.
Sample1's trailing numeric token and the separate clinicalIndication truncation
remain unchanged. No workflow mappings, hosted workflows, or installations were
changed. No Git push occurred; any remote update remains a separate approved step.
