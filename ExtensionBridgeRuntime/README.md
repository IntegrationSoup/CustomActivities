# Version 5 Extension Library runners

This directory is source-linked into the isolated executables, never into the
version 4 activity DLLs. `ExtensionRunnerHosting/ExtensionBridgeContract.cs` is
an exact normalized copy of the canonical HL7SoupIntegrations DTO source. Run
`InstallerTools/VerifyBridgeContract.ps1` before builds when either repository
changes. Envelope version 1 and extension schema 1 are independent.

## Execution and compatibility

Each runner explicitly lists its supported adapter classes. Describe derives
metadata from the unchanged adapter attributes and returns their saved legacy
assembly-qualified identifiers as data. No host CLR extension loading is needed.
The narrow `SourceAdapterApi.cs` is a runner-private source compilation boundary,
not a binary-compatible replacement for HL7SoupIntegrations. Unsupported API
usage fails compilation instead of silently pretending to implement host objects.
The actual v4 DLL uses the real host API and its existing synchronous pipe client.

Eight existing runners accept their original command-line and persistent-pipe
operations. `--bridge-version 1` selects the new operations. HL7 Value Transformers
and Validate HL7 have new bridge executables; their v4 DLL execution stays in the
v4 host. Nine bridge executables target net48. Validate targets net10.0-windows
because its actual parsing/highlighter business engine is the installed v5
`HL7SoupWorkflow.dll`; it is not reimplemented or bundled in this MSI.

Validation loads the trusted server runtime lazily on invocation. The installed
versioned payload resolves four parent directories to the server directory. A
trusted manual launch may specify `--runtime-directory <absolute-server-path>`.
Describe neither loads the parser nor reads validation profiles. Profiles and
service account access use the real installed engine. Invalid JSON validation
returns its report and workflow error. Invalid ACK validation also signals the
canonical `PromoteResponseToWorkflow` flag, with `workflow-response-v1` declared.
The host validates and promotes the ACK before applying the workflow error.

Activities retain one object per Prepare-to-Close session. Transformers use fresh
objects. Business calls serialize; cancellation bypasses that lock via `.control`.
Acknowledging cancel does not mean external work stopped. Late results are
discarded, outcome is Unknown, and the session is invalidated. No request is
replayed after dispatch. Memory-only session/request capacity is bounded.

Named pipes use strict length-prefixed UTF-8, bounded envelopes, launch-identity
and SYSTEM ACLs, a network-logon deny ACE, bounded frame I/O and parent monitoring.
`BridgeWire.DispatchJson` is the same unframed JSON seam for an HTTP host. The
shipping executable does not expose an HTTP listener. Deploying that seam behind
HTTP requires administrator-owned HTTPS/authentication before dispatch; the
loopback test host is only a synthetic test fixture, not a deployable server.

## Building review artifacts

With the canonical API already built, run from PowerShell:

```powershell
./InstallerTools/BuildBridgeReview.ps1 -HL7SoupRoot D:/source/repos/HL7Soup
```

This serially builds ten v4 adapters and ten runners, runs synthetic compatibility
and runtime tests, then builds ten unsigned MSIs. It only stages review copies in
`artifacts/bridge-review/<version>` and records SHA-256 hashes. It does not sign,
install, deploy, contact customer services, or run the download-copy helper.
The full solution includes other historical projects and an adjacent host source
reference; the dedicated script supports a separate worktree without host edits.

`BridgeCompatibility.Tests` invokes real compiled v4 DLLs through the real API and
the updated legacy runners, then compares v5 operations. `BridgeRuntime.Tests`
tests framing, ACLs, actual child/.control behavior, parent death, a loopback HTTP
auth fixture, mocked cloud/SFTP boundaries and the actual host validation engine
with synthetic in-memory profiles. Fixture scope must not be described as a
customer deployment, live cloud/SFTP test, or full host integration test.

## Installer ownership and upgrades

The existing MSI UpgradeCodes and legacy component GUIDs are preserved. Legacy
DLLs go to all three product Custom Libraries directories; runner dependencies
only go under the server. No MSI includes HL7SoupIntegrations.dll.

Bridge payloads use `Custom Libraries/ExtensionProviders/<id>/<revision>/`.
Revision defaults to the generated MSI version (or explicit BridgeRevision).
Every released payload change must advance the version/revision. Do not reuse a
revision to overwrite a running generation. Existing service restart actions
deliberately drain the installed host; seamless hot replacement is not promised.

The protected manifest is installed to
`%ProgramData%/Popokey/ExtensionProviders/<id>/<id>.json`. Its exact path is stored
in the installer's own 64-bit HKLM provider key. MSI resolves the executable path;
a deferred writer atomically replaces the disabled template before service start.
Separate rollback and commit actions restore/discard its backup. Early
RemoveExistingProducts runs after InstallInitialize, inside the MSI transaction,
so the old product cannot subsequently remove the new registration.

`VerifyBridgeInstallers.ps1` reads MSI databases without installing. It verifies
key ownership/bitness, protected manifest ACLs, v4 copies, excluded host API and
transaction ordering. This does not establish actual install/upgrade/rollback or
survival of a host-v5 upgrade. Those acceptance checks require a disposable
Windows host with both product versions; never install these review packages
over the user's existing v4 packages merely to obtain that evidence.

Manifest custom actions use formatted EXE targets, not a DLL-style
CustomActionData property. See [Microsoft's Type 2 action contract](https://learn.microsoft.com/en-us/windows/win32/msi/custom-action-type-2).

## Optional designer assistance

The version 5 browser sends `extension.designer` only when the selected declaration
opts in through `Designer`. Runner-only `ExtensionDesignerAttribute` chooses an
`IExtensionDesigner`; the v4 DLL keeps its original host API and manual fields.
The callback receives schema/provider/type identity, a revision, deadline,
`Initialize`, `FieldChanged` or `Action`, and declared parameter values. Values
include hidden fields so hiding a field never erases its saved configuration.
Expressions are passed as authored strings, not evaluated workflow data.

A result contains only declared field patches: optional value, visibility,
enabled state, choices and validation text. Null leaves that property alone;
an empty string clears a value or validation message. The browser debounces
field changes, rejects outdated replies and records returned value changes as
one undoable draft edit. Presentation state is not stored in the workflow.
Callbacks are deadline-bounded, serialized with business calls, and never replayed.

SFTP uses field changes to show the passphrase field when a private-key path is
present. AWS S3 and Azure Blob offer explicit Load buckets / Load containers
buttons; those read-only requests need literal connection settings and execute
under the extension host's network permissions. Manual names and credentials
remain sufficient in every supported designer. Version 4 does not need callbacks
or additional buttons to configure or run these activities.

Output samples are read-only unless the runner-only
`EditableResponseTemplateAttribute` opts in. Data from PDF opts in to an editable
JSON design sample; its binary input and actual extracted JSON are unchanged.
Run a representative PDF and copy its response from the logs into that sample
to expose downstream fields without adding a Code activity just for a sample.

For a signed distribution use `InstallerTools/BuildBridgeSignedRelease.ps1` with
a new installer version. It signs the v4 adapter, v5 runner, manifest helper and
MSI, verifies publisher/timestamps and records the signed artifact hashes.
Review-build MSIs are unsigned and must not be used as signed release downloads.