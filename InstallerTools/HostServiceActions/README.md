# Host service policy (activity MSI 5.0.6)

The shared policy replaces unconditional native ServiceControl rows in all ten
activity installers. Activity MSI version 5.x does not identify the installed host.
Only a confirmed V4 host that was initially running may be stopped and restarted.

| Installed host/state | Behavior |
| --- | --- |
| V4 running | Stop, wait for provider exit, install, restore running state |
| V5 running, new provider | Allow discovery without restart if no provider runner exists |
| V5 running, upgrade/repair/removal | Block before transaction; operator must close host |
| Unknown running, or transitional/paused | Block; never assume V4 |
| Stopped or absent | Preserve state; active provider runners still block replacement |

Host identity uses read-only Windows Installer product enumeration under host
UpgradeCode `{59974DE9-ADD2-402C-8216-C4E3303A143F}`, its registered executable
component `{0319DCB1-FE86-46FA-99E1-16565C068EEF}`, and a matching service ImagePath.
The identifiers were checked against host branch `4.0` commit
`7f2585059fdba7ac294810a72a5b961d754297e3` and V5 main. ProductVersion major 4/5 is
used; the executable's assembly/file version is unsuitable. Ambiguous registrations,
unquoted paths with spaces, and other majors are unknown. No Win32_Product query,
repair, host source modification, or service control occurs during detection.
Standard-user detection is intended but still requires acceptance testing.

Capture runs before InstallInitialize. Deferred elevated actions recheck service
registration/state before control and wait for the same provider's named runner to
exit without killing it. A process-name check is conservative, not a host lease
handshake: an interactive runner also blocks. Concurrent host starts and live V5
hot replacement are not supported. Close affected interactive hosts for maintenance.

Rollback restoration is scheduled before file changes so it executes after files
are restored. A late rollback stop releases newly installed files if failure occurs
after the success restart. During a major upgrade the removed package owns rollback
restoration of its old files/service; a nested policy-aware uninstall suppresses its
success restart. No script-generating action precedes early RemoveExistingProducts
inside the transaction (ICE63). Live execution of these paths remains a release gate.

## Cached legacy installer transition

Removing native ServiceControl rows from the new MSI cannot disable an old cached
MSI's controls. The immediate guard reads each related cached MSI database without
opening an installation session. If native controls affect an existing service,
the transition is allowed only for an initially running confirmed V4 service.
An initially stopped service therefore blocks upgrades from these older packages:
their rollback could otherwise start it. Unreadable caches or unexpected service
names fail closed. Policy-aware packages without native controls can upgrade with
the service stopped. Do not bypass this guard by assuming a stopped legacy upgrade
is safe; a separately validated transition procedure is required. Direct use of an
old cached MSI still follows that old package's behavior.

## Validation and release gates

`HostServiceActions.Tests` links the production decision logic: 56 checks cover
identity, state preservation, unknown/V5 behavior and cached-control transitions.
`Test-HostServicePolicy.ps1` checks actual MSI tables for all ten packages, action
types/conditions, binary linkage, no native controls and upgrade/rollback ordering.
Bridge packaging and .NET prerequisite checks run separately. Signed packaging
compares every extracted installed file with Release outputs and verifies the MSI,
owned binaries and embedded helpers with the expected publisher and timestamp.

These are review artifacts, not approval for production installation. A disposable
Windows V4/V5 matrix must test fresh install, upgrade from shipped legacy packages,
policy-aware upgrade, repair, uninstall and injected rollback failures before/after
file replacement and service restoration. Include running/stopped/absent and unknown
hosts, pending state, standard-user detection, interactive/leased runners, silent/UI
invocation and cached-package failures. Confirm original state and original files on
rollback. No live MSI install, service changes, staging or publication is performed
by the build/verification scripts.
