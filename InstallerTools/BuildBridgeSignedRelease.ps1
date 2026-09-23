param(
    [string]$InstallerVersion = '5.0.2',
    [string]$SigntoolPath = 'C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe',
    [string[]]$AlreadyBuilt = @()
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repository ("artifacts/bridge-signed/$InstallerVersion-"+[guid]::NewGuid().ToString('N'))
if (Test-Path -LiteralPath $artifactRoot) { throw 'Use a new release directory; do not overwrite release evidence.' }
$thumbprint = '1586FB787BD45270D870F90918EF887A121EB6DB'
$wix = Join-Path $env:USERPROFILE '.nuget/packages/wixtoolset.sdk/5.0.2/tools/net6.0/wix.dll'
$setups = @('ZipActivities','DataFromPdfActivities','HtmlToPdfActivities','RtfToPdfActivities','AzureActivities','AwsActivities','EncryptionActivities','SftpActivities','HL7ValueTransformers','ValidateHl7Transformer')
if (@($AlreadyBuilt | Where-Object { $_ -notin $setups }).Count) { throw 'Unknown already-built package.' }
New-Item -ItemType Directory -Path $artifactRoot | Out-Null
$sourceCommit = (& git -C $repository rev-parse HEAD).Trim()
$msi = New-Object -ComObject WindowsInstaller.Installer
function Rows($database, [string]$table, [string[]]$columns) {
    $query = 'SELECT ' + (($columns | ForEach-Object { '`'+$_+'`' }) -join ',') + ' FROM `'+$table+'`'
    $view = $database.OpenView($query); $view.Execute() | Out-Null
    try { while ($record = $view.Fetch()) {
        $row = [ordered]@{}
        for ($index=0; $index -lt $columns.Count; $index++) { $row[$columns[$index]]=$record.StringData($index+1) }
        [pscustomobject]$row
    } } finally { $view.Close() | Out-Null }
}
function Signature([string]$path) {
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne $thumbprint -or !$signature.TimeStamperCertificate) {
        throw "Valid expected publisher and timestamp required: $path (status $($signature.Status))"
    }
    [pscustomobject]@{Path=$path;SHA256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;Status=$signature.Status.ToString();Publisher=$signature.SignerCertificate.Subject;SignerThumbprint=$signature.SignerCertificate.Thumbprint;TimestampAuthority=$signature.TimeStamperCertificate.Subject;TimestampThumbprint=$signature.TimeStamperCertificate.Thumbprint}
}
$results = @()
foreach ($setup in $setups) {
    $project = Join-Path $repository "Setup.$setup/Setup.$setup.wixproj"
    if ($setup -notin $AlreadyBuilt) {
        & dotnet build $project -c Release --no-restore --nologo -m:1 "-p:InstallerVersion=$InstallerVersion" "-p:SigntoolPath=$SigntoolPath" '-p:SkipCodeSigning=false' '-p:SkipTimestamp=false'
        if ($LASTEXITCODE -ne 0) { throw "Signed packaging failed: $setup" }
    }
    $name = "IntegrationSoup.$setup.msi"
    $installer = Join-Path $artifactRoot $name
    Copy-Item -LiteralPath (Join-Path $repository "Setup.$setup/bin/Release/$name") -Destination $installer
    $installerSignature = Signature $installer
    $database = $msi.OpenDatabase($installer,0)
    $properties = @{}; foreach ($row in (Rows $database 'Property' @('Property','Value'))) { $properties[$row.Property]=$row.Value }
    $variables = Get-Content -LiteralPath (Join-Path $repository "Setup.$setup/ProductVariables.wxi") -Raw
    $expectedName = [regex]::Match($variables,'define ProductDisplayName\s*=\s*"([^"]+)"').Groups[1].Value
    $expectedUpgrade = [regex]::Match($variables,'define UpgradeCode\s*=\s*"([^"]+)"').Groups[1].Value
    $directories = @(Rows $database 'Directory' @('Directory','DefaultDir'))
    if ($properties.ProductVersion -ne $InstallerVersion -or $properties.ProductName -ne $expectedName -or $properties.UpgradeCode -ne $expectedUpgrade -or @($directories | Where-Object { $_.Directory -eq 'BridgeRevisionDirectory' -and ($_.DefaultDir -split '\|')[-1] -eq $InstallerVersion }).Count -ne 1) { throw "Package identity/revision mismatch: $setup" }
    $files = @(Rows $database 'File' @('File','FileName'))
    if ($files.FileName -match 'HL7SoupIntegrations\.dll') { throw 'Host API must not be packaged.' }
    $legacyName = [regex]::Match($variables,'define (?:ActivityFileName|PayloadFileName)\s*=\s*"([^"]+)"').Groups[1].Value
    [xml]$projectXml = Get-Content -LiteralPath $project -Raw
    $runnerName = [string]$projectXml.SelectSingleNode('//BridgeExecutableName').InnerText
    $ownedNames = @($legacyName,$runnerName,([IO.Path]::GetFileNameWithoutExtension($runnerName)+'.dll'))
    $inspection = Join-Path $artifactRoot "inspection/$setup"
    New-Item -ItemType Directory -Path $inspection | Out-Null
    & dotnet $wix msi decompile $installer -x $inspection -o (Join-Path $inspection 'package.wxs') *> (Join-Path $artifactRoot "$setup-extraction.log")
    if ($LASTEXITCODE -ne 0) { throw "Read-only MSI extraction failed: $setup" }
    $payloadSignatures = @()
    foreach ($file in $files) {
        $longName = ($file.FileName -split '\|')[-1]
        if ($longName -notin $ownedNames) { continue }
        $extracted = Join-Path $inspection ('File/'+$file.File)
        $named = Join-Path $inspection ($file.File+'__'+$longName)
        Copy-Item -LiteralPath $extracted -Destination $named
        $payloadSignatures += Signature $named
    }
    $writer = Join-Path $inspection 'BridgeManifestWriter.exe'
    Copy-Item -LiteralPath (Join-Path $inspection 'Binary/BridgeWriter') -Destination $writer
    $payloadSignatures += Signature $writer
    if (!$legacyName -or !($files | Where-Object File -eq 'BridgeExecutableFile') -or $payloadSignatures.Count -lt 3) { throw 'Expected owned payloads missing.' }
    $verifyPaths = @($installer)+@($payloadSignatures.Path)
    & $SigntoolPath verify /pa /all /tw @verifyPaths *> (Join-Path $inspection 'trusted-signature-verification.log')
    if ($LASTEXITCODE -ne 0) { throw "Trusted Authenticode/timestamp verification failed: $setup" }
    $results += [pscustomobject]@{Name=$name;ProductVersion=$properties.ProductVersion;ProductName=$properties.ProductName;UpgradeCode=$properties.UpgradeCode;ProductCode=$properties.ProductCode;SourceCommit=$sourceCommit;MSI=$installerSignature;Payloads=$payloadSignatures}
    $results | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $artifactRoot 'signed-release-verification.json') -Encoding utf8
    Write-Output "VERIFIED ${name}: $InstallerVersion, Popokey signature and trusted timestamp, $($payloadSignatures.Count) embedded owned payload copies."
}
Write-Output "SIGNED_RELEASE=$artifactRoot"
Write-Output 'All ten packages verified. No installation, staging or publication performed by this script.'
