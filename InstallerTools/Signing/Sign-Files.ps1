[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$Paths,
    [string]$Thumbprint = '1586FB787BD45270D870F90918EF887A121EB6DB',
    [string]$SigntoolPath = 'C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe',
    [string]$TimestampUrl = 'http://ts.ssl.com',
    [string]$SkipTimestamp = 'false',
    [string]$Description = ''
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Signing.Common.ps1')
$mutex = $null
$pin = $null
$certificate = $null
try {
    if ($Thumbprint -ne $SigningThumbprint) { throw 'Unexpected signing identity. No authentication attempted.' }
    if (!(Test-Path -LiteralPath $SigntoolPath -PathType Leaf)) { throw "SignTool not found: $SigntoolPath" }
    $files = @($Paths.Split('|') | ForEach-Object {
        if (![IO.Path]::IsPathRooted($_)) { throw 'Signing requires absolute file paths.' }
        $file = Get-Item -LiteralPath $_
        if ($file.PSIsContainer -or $file.Extension -notin @('.dll','.exe','.msi')) { throw 'Expected a DLL, EXE or MSI.' }
        $file.FullName
    } | Select-Object -Unique)
    if (!$files.Count) { throw 'No signing files were supplied.' }
    $certificate = Get-Item "Cert:\CurrentUser\My\$Thumbprint"
    if (!$certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'Signing certificate is unavailable or outside its validity period.' }
    Add-Type -Path (Join-Path $PSScriptRoot 'NativeSigner.cs')
    $mutex = Enter-SigningLock
    Assert-SigningReady
    try { $pin = (Get-Content -LiteralPath (Join-Path $SigningDirectory 'pin.dpapi') -Raw).Trim() | ConvertTo-SecureString }
    catch { throw 'Could not decrypt saved PIN under this Windows account. Run Setup-Signing.ps1.' }
    foreach ($file in $files) {
        # Shared bridge helpers occur in all ten packages. Reuse valid, timestamped
        # payloads instead of rewriting a file another installer may be packing.
        $existing = Get-AuthenticodeSignature -LiteralPath $file
        $reuse = [IO.Path]::GetExtension($file) -ne '.msi' -and !$Description -and $existing.Status -eq 'Valid' -and $existing.SignerCertificate.Thumbprint -eq $Thumbprint -and ($SkipTimestamp -eq 'true' -or $existing.TimeStamperCertificate)
        if (!$reuse) {
            # Written BEFORE any token operation; a crash or failure leaves the
            # account locked across new processes, targets and later builds.
            [IO.File]::WriteAllText((Join-Path $SigningDirectory 'attempt.lock'), 'An attempt began. Only explicit local setup may clear an unsuccessful attempt.')
            [UnattendedSigner]::Sign($certificate, $file, $pin, $Description)
            if ($SkipTimestamp -ne 'true') {
                & $SigntoolPath timestamp /tr $TimestampUrl /td SHA256 $file
                if ($LASTEXITCODE -ne 0) { throw 'Timestamp failed. Signing stopped without retry.' }
            }
        }
        $verifyArguments = @('verify','/pa','/all')
        if ($SkipTimestamp -ne 'true') { $verifyArguments += '/tw' }
        & $SigntoolPath @verifyArguments $file
        if ($LASTEXITCODE -ne 0) { throw "Trusted signature verification failed: $file" }
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $Thumbprint -or ($SkipTimestamp -ne 'true' -and !$signature.TimeStamperCertificate)) {
            throw "Expected publisher and timestamp verification failed: $file"
        }
        if (!$reuse) { Remove-Item -LiteralPath (Join-Path $SigningDirectory 'attempt.lock') }
        Write-Output "Verified signature: $file"
    }
} finally {
    if ($pin) { $pin.Dispose() }
    if ($certificate) { $certificate.Dispose() }
    if ($mutex) { $mutex.ReleaseMutex(); $mutex.Dispose() }
}
