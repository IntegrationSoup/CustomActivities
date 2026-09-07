param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$writer = Join-Path $PSScriptRoot "BridgeManifestWriter/bin/$Configuration/net48/BridgeManifestWriter.exe"
$fixture = Join-Path $repository ('artifacts/manifest fixtures/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$provider = 'popokey.fixture'
$manifest = Join-Path $fixture ($provider + '.json')
$original = '{"SchemaVersion":1,"ProviderId":"popokey.fixture","Revision":"4.0.1","Enabled":false}'
[IO.File]::WriteAllText($manifest, $original)
function Invoke-Writer([string]$mode) {
    & $writer $mode $manifest $writer $provider '5.0.1'
    if ($LASTEXITCODE -ne 0) { throw "Manifest writer $mode failed" }
}
Invoke-Writer 'write'
$registered = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
if (!$registered.Enabled -or $registered.ProviderId -ne $provider -or $registered.ExecutablePath -ne $writer -or $registered.Revision -ne '5.0.1') { throw 'Resolved registration differs' }
Invoke-Writer 'rollback'
if ([IO.File]::ReadAllText($manifest) -ne $original) { throw 'Rollback did not restore original bytes' }
Invoke-Writer 'write'
Invoke-Writer 'commit'
if (Test-Path -LiteralPath ($manifest + '.5.0.1.rollback')) { throw 'Commit retained backup' }
$committed = [IO.File]::ReadAllText($manifest)
Invoke-Writer 'rollback'
if ([IO.File]::ReadAllText($manifest) -ne $committed) { throw 'No-op rollback changed committed registration' }
& $writer write $manifest (Join-Path $fixture 'missing.exe') $provider '5.0.2'
if ($LASTEXITCODE -eq 0 -or [IO.File]::ReadAllText($manifest) -ne $committed) { throw 'Invalid executable changed registration' }
Write-Output 'Manifest writer PASS: paths containing spaces, exact rollback bytes, commit cleanup, safe no-op rollback and rejected missing executable (filesystem fixture only).'
