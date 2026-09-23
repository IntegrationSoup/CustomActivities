param([Parameter(Mandatory=$true)][string]$HL7SoupRoot)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$canonical = Join-Path $HL7SoupRoot 'HL7Parser/HL7SoupIntegrations/ExtensionBridgeContract.cs'
$vendored = Join-Path $repositoryRoot 'ExtensionRunnerHosting/ExtensionBridgeContract.cs'
function Normalize-Source([string]$path) { return [IO.File]::ReadAllText($path).TrimStart([char]0xfeff).Replace("`r`n", "`n").TrimEnd() }
if ((Normalize-Source $canonical) -cne (Normalize-Source $vendored)) { throw 'Vendored bridge DTO differs from canonical source. Refresh it with a reviewed patch and re-run compatibility tests.' }
Write-Output 'Canonical schema source matches the vendored DTO (normalizing BOM/line endings only).'
