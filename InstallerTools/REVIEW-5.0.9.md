# Extension Library installer release 5.0.9

Published on 30 September 2026 using the normal download-copy helper and downloads-only website publisher. All ten public MSIs match the signed release built from source commit `7701cafbf55a2da372b61f94893a9782bf84b65b`.

The shared installer helper explicitly activates CLR 4 with legacy activation enabled. Native MSI failure dialogs and automatic verbose logging remain available when a managed helper cannot start. Quiet installation retains normal MSI exit codes. The HL7 validation package checks its additional x64 .NET 10 runtime prerequisite.

## Acceptance evidence

| Environment | Installation | Automatic logging | Runner startup/catalog | PDF conversion |
| --- | --- | --- | --- | --- |
| Jason's local Windows computer | All ten succeeded | All ten produced logs | Ten providers, two requests each | Local regression output identical |
| Clean Azure Windows 11 Pro | All ten succeeded | All ten produced logs | Ten providers, two requests each | Synthetic fixture passed |
| Clean Azure Windows Server 2019 | All ten succeeded | All ten produced logs | Ten providers, two requests each | Synthetic fixture passed |

The VMs downloaded the normal public website URLs. Every download was checked against the verified release's SHA256, publisher certificate, timestamp, and version before installation. No customer PDF or customer-derived JSON was uploaded. Server 2019 was updated from .NET Framework 4.7.2 to 4.8 and rebooted. Both VMs have .NET 10 x64 for the validation runner; .NET Framework 3.5 remained disabled. Windows 11 used Windows\SystemTemp for SYSTEM-account logs, so the test resolves the Windows temporary directory rather than relying exclusively on the TEMP environment variable.

The initial public baseline reproduced nine 5.0.7 installer failures on Windows 11; the patched PDF 5.0.8 package succeeded. Server 2019's initial baseline lacked the documented .NET Framework 4.8 prerequisite. The released 5.0.9 set passed after prerequisites were installed.

All ten packages passed MSI service-policy/transaction checks, bridge packaging checks, prerequisite expression checks, signature/timestamp verification, and installed-file/source hash comparison. The shared service-policy suite passed 53 checks. The local host service began stopped and remained stopped. Downloads publication transferred exactly ten files, with zero failures and zero deletions; cache purge completed.

## Scope and limits

Runner catalog tests prove installed payload startup and persistent bridge communication. They do not execute live AWS, Azure, or SFTP operations, nor replace full workflow testing against a configured host and validation profiles. The local intentional failure test produced a native error and automatic log; automatic visual inspection of the final failure screen was unavailable. MSI validation verifies the error-exit dialog wiring and displayed log property. Native Windows errors that prevent the MSI from opening cannot use package-defined UI or logging.

Signed artifacts and detailed local/VM verification reports are retained in the ignored `artifacts` area outside the completed worktree before cleanup. They are not committed to Git.
