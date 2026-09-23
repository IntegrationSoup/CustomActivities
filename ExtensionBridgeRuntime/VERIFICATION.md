# Review evidence and handoff — 7 September 2026

Ten local unsigned MSI packages, version/revision 5.0.1, are staged in
`artifacts/bridge-review/5.0.1/`. `sha256.json` identifies their exact bytes.
`connections/index.json` lists the absolute development executable and JSON
manifest paths for every provider. No registry registration or host settings are
created by generating these development connection files. Installed MSI manifests
instead use the protected ProgramData paths described in README.md.

## Completed checks

- All ten v4 DLLs and ten isolated runners built in Release with zero warnings/errors.
- Canonical DTO source matches exactly after BOM/line-ending normalization.
- 1,025 compatibility checks pass: all ten packages' real v4 descriptor attributes,
  parameter editor/secret/options/binding metadata, stable saved identifiers,
  seven HL7 Value Transformers and 28 exact v4/v5 result comparisons, absent vs
  authored-empty input, resolved output-variable annotation and negative requests.
- Real unchanged v4 adapters and updated runners agree on synthetic PDF extraction
  and all three ZIP operations. Lifecycle/session rejection is checked.
- Real HTML-to-PDF rendering through v4 and v5 yields matching extracted text.
  Encryption crosses versions both ways with a Unicode fixture.
- Unchanged v4 Azure, AWS, SFTP and RTF adapters use synthetic legacy business
  boundaries; captured operation payloads match v5 source-adapter dispatch exactly.
  Binary upload/download and RTF response bytes are additionally checked. No cloud,
  SFTP or customer endpoints were contacted by those fixtures.
- 63 runtime/validation checks plus real transport fixtures pass: strict UTF-8,
  duplicate-member and frame rejection; ACL allow/deny identities; actual child
  `.control` cancellation during work; discarded late results and Unknown outcome;
  session invalidation; parent death during incomplete frame; loopback HTTP auth
  rejection before the identical unframed JSON dispatch. The shipping runner
  exposes pipes and an HTTP-hostable seam, not a production HTTP listener.
- Actual installed HL7 parsing/highlighter code executes synthetic in-memory valid
  and invalid profiles. The unchanged Validate DLL and bridge agree on JSON/ACK,
  variables, workflow error and ACK promotion. No customer profiles were changed.
- All ten MSI databases verify own HKLM64 provider registration, protected manifest
  file/directory DACLs, v4 DLL destinations, excluded HL7SoupIntegrations.dll,
  early rollback-capable major upgrade and deferred writer/rollback/commit ordering.
- The manifest writer filesystem fixture verifies paths with spaces, byte-exact
  rollback, commit cleanup, no-op rollback and safe failure for a missing executable.
- Real RTF rendering is explicitly waived by Jason because LibreOffice is absent.
  No LibreOffice installation was attempted.

## Live host integration handoff

Use `connections/index.json`; each referenced manifest contains its complete
argument list, including the trusted development parser directory for Validate.
All providers accept Describe and empty Prepare/Close lifecycle testing without
external business I/O. The host adds its usual bridge pipe/parent arguments.

Safe operation inputs:

| Package | Synthetic operation proof |
| --- | --- |
| HL7 Value Transformers | All seven types; Text `fallback message 123`, `Value` `(021) 555-0100`, `Output Variable` ` CustomResult `; also omit Value and author it empty separately. |
| Data from PDF | Use `artifacts/bridge-fixtures/` test PDFs, or an HTML-generated fixture PDF; Binary input as base64, JSON response. |
| ZIP | Each `artifacts/bridge-fixtures/<id>/source/sample.txt` contains synthetic text; use a new disposable output directory and ZIP path. |
| HTML to PDF | Text `<html><body><p>Bridge fixture patient</p></body></html>`; Binary response. |
| Encryption | Text `Synthetic Unicode message: café 日本語`; `Encryption Key` `fixture-key-at-least-12`; cross-decrypt the returned cipher. |
| Validate | HL7 `MSH|^~\&|Fixture|Sender|Fixture|Receiver|20260907120000||ADT^A01|BRIDGE1|P|2.5.1` followed by CR and `PID|1||123||Example^Patient||notdate|U`. JSON or HL7 response according to type; `Error if invalid=true`. The runtime suite supplies in-memory profiles; a separately launched production runner requires a genuine existing saved profile accessible to the service identity. Do not invent or overwrite customer profiles. |
| Azure/AWS/SFTP | Describe and Prepare/Close on real runners; use the explicit mock-boundary suite for Process unless a separately authorized disposable service is supplied. Never use customer credentials/endpoints. |
| RTF | Describe and mock request/binary-response comparison; actual rendering waived. |

Run the two test executables through `BuildBridgeReview.ps1`, or run
BridgeCompatibility.Tests followed by BridgeRuntime.Tests. The latter consumes
the former's `artifacts/boundary-proof/*.json` captures. These contain synthetic
credentials only. Validation loads the prebuilt host runtime in the test process;
it does not build or modify that repository.

## Outstanding release acceptance (not claimed as passed)

The host owner performs real-runner browser/MCP/WPF catalog and invocation checks,
saved-workflow reuse, generation refresh and the final host-side integration.

No disposable Windows Installer environment was available through the checked
Hyper-V/VMware/VirtualBox/Windows Sandbox tooling. Production review MSIs have not
been installed over the user's existing v4 packages. MSI database inspection and
helper tests are not a Windows Installer transaction test.

Required isolated release procedure for each of the ten packages:

1. Snapshot a clean disposable Windows VM with version 4 host/designer and relevant
   prerequisites. Install the package with verbose MSI logging, before any workflow
   execution; verify own HKLM64 ManifestPath, protected manifest and v4 locations.
2. Run a synthetic saved v4 workflow using its unchanged identifiers/parameters.
   Keep a second provider installed as an ownership sentinel.
3. Upgrade that host to version 5 **without reinstalling the extension**. Verify
   registration survives, catalog discovery succeeds and the saved workflow works.
4. Repair the extension; verify manifest remains valid and a forced later failure
   rolls its bytes back. Build a test-only failure action in a distinct fixture MSI
   after manifest writing; do not inject it into a distributed production package.
5. Upgrade the extension to a strictly newer revision; check old-product removal,
   versioned payload selection and service drain behavior. Force upgrade failure
   and verify rollback restores the prior registration and executable generation.
6. Uninstall the extension; verify only its files/key are removed, the sentinel
   provider is intact and the host's independently stored visibility/settings remain.
   Capture logs, before/after registry/ACLs and exact package hashes; revert the VM.

Website and product documentation edits are local and unpublished. The existing
download-copy list already covers all ten MSI names and was not executed.
