param(
    [Parameter(Mandatory=$true)][string]$HL7SoupRoot,
    [string]$InstallerVersion = '5.0.1',
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$hostRoot = [IO.Path]::GetFullPath($HL7SoupRoot)
$api = Join-Path $hostRoot 'HL7Parser/HL7SoupIntegrations/bin/Release/netstandard2.0/HL7SoupIntegrations.dll'
if (!(Test-Path -LiteralPath $api)) { throw 'Build the canonical host API first; this script never builds or edits the host.' }
& (Join-Path $PSScriptRoot 'VerifyBridgeContract.ps1') -HL7SoupRoot $hostRoot
$adapters = @(
    'DataFromPdfActivities', 'ZipActivities', 'HtmlToPdfActivities', 'RtfToPdfActivities',
    'AzureActivities/AzureActivities', 'AmazonActivities/AmazonActivities',
    'HL7SoupEncryptionActivities/HL7SoupEncryptionActivities', 'SftpActivities',
    'HL7ValueTransformers', 'ValidateTransformer'
)
$setups = @('DataFromPdfActivities','ZipActivities','HtmlToPdfActivities','RtfToPdfActivities','AzureActivities','AwsActivities','EncryptionActivities','SftpActivities','HL7ValueTransformers','ValidateHl7Transformer')
function Build-Project([string]$relative, [string[]]$options = @()) {
    & dotnet build (Join-Path $repository $relative) -c Release --nologo -m:1 @options
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $relative" }
}
# Single-process sequence; no publishing, signing, deployment, or global counters.
foreach ($adapter in $adapters) {
    $name = Split-Path $adapter -Leaf
    $framework = if ($name -eq 'HL7ValueTransformers') {'net472'} elseif ($name -eq 'ValidateTransformer') {'netstandard2.0'} else {'net48'}
    Build-Project "$adapter/$name.csproj" @('-f',$framework,"-p:HL7SoupIntegrationsAssembly=$api")
}
foreach ($name in @('HL7ValueTransformers.Runner','ValidateTransformer.Runner')) { Build-Project "$name/$name.csproj" }
if (!$SkipTests) {
    Build-Project 'BridgeCompatibility.Tests/BridgeCompatibility.Tests.csproj'
    & (Join-Path $repository 'BridgeCompatibility.Tests/bin/Release/net48/BridgeCompatibility.Tests.exe') $repository $api
    if ($LASTEXITCODE -ne 0) { throw 'Legacy compatibility fixture failed' }
    & dotnet run --project (Join-Path $repository 'BridgeRuntime.Tests/BridgeRuntime.Tests.csproj') -c Release -- (Join-Path $hostRoot 'HL7SoupWorkflowHost/HL7SoupWorkflowHostWindowsService/bin/Debug/net10.0-windows')
    if ($LASTEXITCODE -ne 0) { throw 'Runtime/validation fixture failed' }
}
$artifactRoot = Join-Path $repository "artifacts/bridge-review/$InstallerVersion"
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
foreach ($setup in $setups) {
    Build-Project "Setup.$setup/Setup.$setup.wixproj" @("-p:InstallerVersion=$InstallerVersion",'-p:SkipCodeSigning=true')
    $installer = Get-Item -LiteralPath (Join-Path $repository "Setup.$setup/bin/Release/IntegrationSoup.$setup.msi")
    Copy-Item -LiteralPath $installer.FullName -Destination (Join-Path $artifactRoot $installer.Name)
}
& (Join-Path $PSScriptRoot 'VerifyBridgeInstallers.ps1') -ArtifactDirectory $artifactRoot
& (Join-Path $PSScriptRoot 'TestBridgeManifestWriter.ps1')
& (Join-Path $PSScriptRoot 'WriteBridgeReviewConnections.ps1') -HL7SoupRoot $hostRoot -Version $InstallerVersion
Get-ChildItem -LiteralPath $artifactRoot -Filter '*.msi' -File | Get-FileHash -Algorithm SHA256 | Select-Object Hash,Path | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $artifactRoot 'sha256.json') -Encoding utf8
Write-Output "Unsigned local review MSIs: $artifactRoot. Nothing installed or published."
