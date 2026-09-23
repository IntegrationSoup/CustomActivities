# Corrected activity installers 5.0.7

All ten corrected installers are built, signed and verified for review. The
unnecessary rejection of stopped legacy V5 upgrades has been removed. No package
was installed, staged or published, and no installed service was changed.

Packaged implementation: `3c93abc`. Verifier follow-up: `4e1f6ff` (PowerShell
array initialization only). The signed 5.0.6 review set is superseded and preserved
unchanged; its restrictions do not describe this corrected set.

Artifacts: `artifacts/bridge-signed/5.0.7-5f2011bf19774adda05b8169aa58cffa/`.
The folder contains ten MSIs, `SHA256SUMS.txt`, per-package extracted evidence and
`signed-release-verification.json` with source commits, hashes, file matches and
certificate/timestamp details. It is separate from production downloads staging.

## Corrected behavior

The same shared policy and supported early-removal sequence are retained.

| Host state during a legacy 5.0.4/5.0.5 upgrade | 5.0.7 behavior |
| --- | --- |
| Confirmed V4 running | Automatically drain and restore running state |
| V4 stopped | Allow without automatic start |
| V5 stopped | Allow without automatic start |
| Unknown host stopped | Allow without guessing host version or starting it |
| V5 running | Require operator to close host for destructive maintenance |
| Unknown host running | Require operator to close host; never assume V4 |
| Absent | No service control |
| Transitional/paused | Require a stable state |

An active target runner can still prevent replacement, including an interactive
runner whose installed service is stopped. The installer does not kill it.
Fresh installation of a new provider on confirmed V5 does not require a restart.

The old Event35 contains uninstall Stop, not uninstall Start; the new package
does not inspect cached native controls and veto stopped upgrades. Native rollback
relies on the documented Windows Installer original-state contract. This is a
reasoned implementation choice, not a claim that live rollback was tested.
See [ServiceControl](https://learn.microsoft.com/en-us/windows/win32/msi/servicecontrol-table)
and [rollback installation](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-installation).

Disabled rollback is handled explicitly: immediate `RequireInstallerRollback`
rejects `RollbackDisabled` or `DISABLEROLLBACK = 1` before capture, transaction and
old-product removal. This follows Microsoft's
[custom-action rollback guidance](https://learn.microsoft.com/en-us/windows/win32/msi/rollback-custom-actions).

## Completed validation

- 53 production-linked policy checks, including stopped V4/V5/unknown legacy upgrades.
- All ten corrected unsigned and signed MSI sets pass service-policy, rollback-guard,
  bridge packaging and .NET prerequisite table checks.
- Release builds have zero warnings/errors.
- Ten MSI signatures and 65 embedded owned/helper signatures pass expected Popokey
  publisher and trusted timestamp verification.
- All 154 installed-file copies match built payloads or package sources; embedded
  helpers also match their signed build outputs.
- The earlier three supplied PDF JSON replays and synthetic regressions passed;
  this correction changes no PDF/activity implementation or workflow mapping.

## Next validation decision

Actual Windows Installer upgrades and rollback were not run. The direct next
acceptance case is PDF 5.0.5 to 5.0.7 with the installed V5 service intentionally
stopped and no target runner, verifying it remains stopped and the correct new
files/manifest are installed. Doing that on the user's machine requires a separate
decision and a concrete backup/logging plan; this task did not authorize it.

For destructive failure/rollback testing, use a disposable Windows VM or spare
Windows test machine with V4/V5 installations. Read-only local discovery found no
Get-VM command or WindowsSandbox.exe, so no ready local disposable environment was
established. A user-approved normal upgrade can exercise the real installed path;
it does not replace injected-failure tests on a disposable system. Test legacy and
policy-aware upgrades, stopped/running/absent, UI/silent, disabled rollback policy,
active runners and failures before/after file changes and service restoration.
Verify state and file restoration before publishing the corrected set broadly.
