# No token access or secret reads occur when this file is loaded.
# MSBuild can inherit PowerShell 7's PSModulePath. Load the module belonging to
# this runtime explicitly, including its certificate provider and DPAPI cmdlets.
Import-Module (Join-Path $PSHOME 'Modules\Microsoft.PowerShell.Security\Microsoft.PowerShell.Security.psd1') -ErrorAction Stop
$SigningIdentity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$SigningDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Popokey\CustomActivitiesSigning'
$SigningThumbprint = '1586FB787BD45270D870F90918EF887A121EB6DB'

function Enter-SigningLock {
    $mutex = [Threading.Mutex]::new($false, "Global\Popokey.CustomActivitiesSigning.$SigningIdentity")
    try {
        try { $acquired = $mutex.WaitOne([TimeSpan]::FromMinutes(10)) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (!$acquired) { throw 'Timed out waiting for another signing operation.' }
        return $mutex
    } catch { $mutex.Dispose(); throw }
}

function Assert-SigningAcl([string]$Path, [switch]$Directory) {
    $item = Get-Item -LiteralPath $Path -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Signing storage must not be a link.' }
    $acl = Get-Acl -LiteralPath $Path
    if ($Directory -and !$acl.AreAccessRulesProtected) { throw 'Signing folder must have protected permissions.' }
    if ($acl.Owner -ne ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -and $acl.Owner -ne $SigningIdentity) { throw 'Unexpected signing storage owner.' }
    $rules = @($acl.Access)
    if (!$rules.Count) { throw 'Signing storage has no access rules.' }
    foreach ($rule in $rules) {
        if ($rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value -ne $SigningIdentity -or $rule.AccessControlType -ne 'Allow') {
            throw 'Signing storage has unexpected access rules.'
        }
    }
}

function Initialize-SigningDirectory {
    if (!(Test-Path -LiteralPath $SigningDirectory)) {
        New-Item -ItemType Directory -Path $SigningDirectory | Out-Null
        & icacls.exe $SigningDirectory /inheritance:r /grant:r "*$($SigningIdentity):(OI)(CI)F" | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not restrict signing storage permissions.' }
    }
    Assert-SigningAcl $SigningDirectory -Directory
}

function Assert-SigningReady {
    if (!(Test-Path -LiteralPath $SigningDirectory)) { throw 'Run InstallerTools\Signing\Setup-Signing.ps1 under this Windows account first.' }
    Assert-SigningAcl $SigningDirectory -Directory
    if (Test-Path -LiteralPath (Join-Path $SigningDirectory 'attempt.lock')) {
        throw 'Signing is locked after a failed or interrupted attempt. Check the YubiKey and run Setup-Signing.ps1 explicitly to replace the saved PIN. No retry was attempted.'
    }
    $pinPath = Join-Path $SigningDirectory 'pin.dpapi'
    if (!(Test-Path -LiteralPath $pinPath)) { throw 'No saved signing PIN. Run InstallerTools\Signing\Setup-Signing.ps1.' }
    Assert-SigningAcl $pinPath
}
