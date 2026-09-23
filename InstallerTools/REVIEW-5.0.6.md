# Activity installers 5.0.6 — signed review artifacts

All ten packages are built, signed and statically verified. They are NOT drop-in
production replacements: installed V5 upgrades from released 5.0.4/5.0.5 are
currently blocked, including when the host is stopped. The requested seamless
installed-V5 update behavior is not complete.

Implementation commit: `282c7cc`. Verification/docs follow-up: `76f44e7` (no
packaged implementation changes). The approved PDF recovery and existing bridge
history remain present. No host source edits, installation, service operations,
download staging, publication or remote Git push were performed for this revision.

Artifact directory relative to this checkout:
`artifacts/bridge-signed/5.0.6-84d9953cf9624c5ba4e606ca53d30ed1/`.
`signed-release-verification.json` contains full hashes, source commits, product
identities, certificate/timestamp evidence and per-file build matches.

## Completed checks

- All ten Release MSI builds: zero warnings/errors.
- 56 tests against linked production identity/state decision logic; no service calls.
- All three supplied JSON replays and synthetic PDF recovery regressions pass.
- Final signed MSI tables: service policy, upgrade/rollback sequencing, no native
  ServiceControl rows, bridge registration ownership and .NET prerequisites pass.
- Ten MSI signatures and 65 extracted owned-binary/helper signatures are valid,
  timestamped and issued to Popokey Limited; signtool trust verification passed.
- Every one of 154 installed-file copies matches a build output or package source;
  embedded helper hashes also match their signed build outputs.
- The embedded service custom-action wrapper is x64 (PE machine 0x8664).

ZIP, PDF, HTML, RTF, Azure, AWS, Encryption, SFTP, HL7 Value Transformers and
Validate HL7 are all included. The Azure text marker initially exposed an omission
in the verification search paths; the checker was corrected and resumed without
re-signing the completed packages. That was a verifier issue, not a signing failure.

## Exact first-upgrade policy from 5.0.4/5.0.5

This matrix assumes the released cached MSI contains the inspected Event35 native
service controls. Both registered service names are checked. An active runner from
an interactive host can still block progress; no runner is forcibly terminated.

| Existing host state | Current 5.0.6 policy |
| --- | --- |
| Confirmed V4 running | Allow automatic drain and restore; live rollback acceptance pending |
| Confirmed V4 stopped | Block cached legacy controls |
| Confirmed V5 running | Block destructive upgrade; no automatic stop/restart |
| Confirmed V5 stopped | Block cached legacy controls |
| Unknown running or stopped | Block |
| Both named services absent, no active provider runner | Allow without service control |
| Transitional/paused | Block until stable |

For policy-aware packages without native controls, stopped services can remain
stopped through an upgrade. Fresh installation of a new provider on confirmed V5
can proceed without restart when no existing provider registration or runner exists.

## Evidence boundary and required decision

The stopped-legacy block is conservative, based on an UNVERIFIED rollback concern.
It is not evidence that Windows Installer actually starts an originally stopped
service. Microsoft's [ServiceControl table](https://learn.microsoft.com/en-us/windows/win32/msi/servicecontrol-table)
documents separate install/uninstall bits: Event35 requests uninstall Stop, not
uninstall Start. Its [rollback contract](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-installation)
describes restoring original state. The [StopServices documentation](https://learn.microsoft.com/en-us/windows/win32/msi/stopservices-action)
does not resolve the specific stopped-service rollback case. This needs a disposable
Windows test before accepting the blanket restriction or removing it.

Only immediate read-only capture precedes InstallInitialize. Script-generating
rollback/drain actions follow early RemoveExistingProducts, consistent with its
[documented scheduling restriction](https://learn.microsoft.com/en-us/windows/win32/msi/removeexistingproducts-action).
The removed old package owns restoration after its old files are restored. Moving
an outer rollback action ahead of early removal is not a valid shortcut; a later
action rolls back before the old removal does. No verified alternative migration
procedure has been established, and cached signed installers were not modified.

Read-only local checks found neither Get-VM nor WindowsSandbox.exe; no disposable
Windows test environment was established. Required acceptance includes V4/V5,
running/stopped/absent, first legacy upgrade and policy-aware upgrade, active
interactive/leased runners, standard-user detection, repair/uninstall, and injected
failures before/after file replacement and service restoration. Rollback must be
enabled: handling a disabled rollback policy is also a release gate, since these
custom service changes depend on rollback actions. No live execution evidence is
claimed by the decision tests or MSI table checks.

The release decision is to keep these artifacts for review/test, resolve stopped
legacy transition semantics and complete the host matrix before production staging.
