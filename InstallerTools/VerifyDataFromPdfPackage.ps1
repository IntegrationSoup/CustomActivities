param(
    [Parameter(Mandatory=$true)][string]$MsiPath,
    [Parameter(Mandatory=$true)][string]$SourceRoot,
    [Parameter(Mandatory=$true)][string]$PreviousMsiPath,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$ExpectedVersion = '5.0.5',
    [string]$SigntoolPath = 'C:\Program Files (x86)\Windows Kits\10\bin\x64\signtool.exe'
)
$ErrorActionPreference = 'Stop'
# Database inspection and cabinet extraction only. Never starts an install sequence.
$msi = New-Object -ComObject WindowsInstaller.Installer
$thumbprint = '1586FB787BD45270D870F90918EF887A121EB6DB'
function Rows($db, [string]$table, [string[]]$columns) {
    $query = 'SELECT ' + (($columns | ForEach-Object { '`'+$_+'`' }) -join ',') + ' FROM `'+$table+'`'
    $view = $db.OpenView($query)
    try {
        $view.Execute()
        while ($record = $view.Fetch()) {
            $row = [ordered]@{}
            for ($i=0; $i -lt $columns.Count; $i++) { $row[$columns[$i]] = $record.StringData($i+1) }
            [pscustomobject]$row
        }
    } finally { $view.Close() }
}
function Signature([string]$path) {
    $sig = Get-AuthenticodeSignature -LiteralPath $path
    if ($sig.Status -ne 'Valid' -or $sig.SignerCertificate.Thumbprint -ne $thumbprint -or !$sig.TimeStamperCertificate) {
        throw "Expected valid Popokey signature and timestamp: $path ($($sig.Status))"
    }
    [pscustomobject]@{Path=$path;SHA256=(Get-FileHash -LiteralPath $path).Hash;Status=$sig.Status.ToString();Publisher=$sig.SignerCertificate.Subject;TimestampAuthority=$sig.TimeStamperCertificate.Subject}
}
function Inventory($db) {
    $components = @{}; foreach ($row in (Rows $db Component @('Component','Directory_'))) { $components[$row.Component] = $row.Directory_ }
    @(Rows $db File @('File','Component_','FileName') | ForEach-Object {
        # Directory IDs are stable across revisions, unlike the installed version directory name.
        $_.File + '|' + ($_.FileName -split '\|')[-1] + '|' + $components[$_.Component_]
    } | Sort-Object)
}
$MsiPath = (Resolve-Path -LiteralPath $MsiPath).Path
$SourceRoot = (Resolve-Path -LiteralPath $SourceRoot).Path
$PreviousMsiPath = (Resolve-Path -LiteralPath $PreviousMsiPath).Path
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$db = $msi.OpenDatabase($MsiPath,0)
$old = $msi.OpenDatabase($PreviousMsiPath,0)
$props = @{}; foreach ($row in (Rows $db Property @('Property','Value'))) { $props[$row.Property]=$row.Value }
$oldProps = @{}; foreach ($row in (Rows $old Property @('Property','Value'))) { $oldProps[$row.Property]=$row.Value }
if ($props.ProductVersion -ne $ExpectedVersion -or [version]$props.ProductVersion -le [version]$oldProps.ProductVersion -or
    $props.UpgradeCode -ne $oldProps.UpgradeCode -or $props.ProductName -ne $oldProps.ProductName) { throw 'Package identity/version mismatch' }
if (Compare-Object (Inventory $old) (Inventory $db)) { throw 'Original file identities/destinations changed' }
foreach ($table in @(@('Registry','Registry','Root','Key','Name','Value','Component_'), @('CustomAction','Action','Type','Source','Target'), @('ServiceControl','ServiceControl','Name','Event','Arguments','Wait','Component_'))) {
    $oldRows = @(Rows $old $table[0] $table[1..($table.Length-1)] | ConvertTo-Json -Depth 5 -Compress)
    $newRows = @(Rows $db $table[0] $table[1..($table.Length-1)] | ConvertTo-Json -Depth 5 -Compress)
    if (Compare-Object $oldRows $newRows) { throw "Existing $($table[0]) definitions changed" }
}
$files = @(Rows $db File @('File','FileName','Version'))
if ($files.FileName -match 'HL7SoupIntegrations\.dll') { throw 'Host API must not be packaged' }
$wix = Join-Path $env:USERPROFILE '.nuget/packages/wixtoolset.sdk/5.0.2/tools/net472/x64/wix.exe'
& $wix msi decompile $MsiPath -x $OutputDirectory -o (Join-Path $OutputDirectory 'package.wxs') *> (Join-Path $OutputDirectory 'extraction.log')
if ($LASTEXITCODE -ne 0) { throw 'Cabinet extraction failed' }
$candidates = @(
    Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'DataFromPdfActivities/bin/Release/net48') -File
    Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'DataFromPdfActivities/bin/Release/net48/DataFromPdfRunner') -File
    Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'DataFromPdfActivities.Runner/bin/Release/net48') -File
    Get-ChildItem -LiteralPath (Join-Path $SourceRoot 'Setup.DataFromPdfActivities/obj/Release/bridge') -File
)
$signatures = @(Signature $MsiPath)
$verified = @()
foreach ($file in $files) {
    $name = ($file.FileName -split '\|')[-1]
    $extracted = Join-Path $OutputDirectory ('File/'+$file.File)
    $hash = (Get-FileHash -LiteralPath $extracted).Hash
    $matching = @($candidates | Where-Object { $_.Name -eq $name -and (Get-FileHash -LiteralPath $_.FullName).Hash -eq $hash })
    if (!$matching.Count) { throw "Embedded payload does not match this build: $name ($($file.File))" }
    $verified += [pscustomobject]@{FileId=$file.File;Name=$name;Version=$file.Version;SHA256=$hash;MatchedBuildPath=$matching[0].FullName}
    if ($name -in @('DataFromPdfActivities.dll','DataFromPdfRunner.exe')) {
        $named = Join-Path $OutputDirectory ($file.File+'__'+$name)
        Copy-Item -LiteralPath $extracted -Destination $named
        $signatures += Signature $named
    }
}
$writer = Join-Path $OutputDirectory 'BridgeManifestWriter.exe'
Copy-Item -LiteralPath (Join-Path $OutputDirectory 'Binary/BridgeWriter') -Destination $writer
if ((Get-FileHash $writer).Hash -ne (Get-FileHash (Join-Path $SourceRoot 'InstallerTools/BridgeManifestWriter/bin/Release/net48/BridgeManifestWriter.exe')).Hash) { throw 'Stale embedded manifest writer' }
$signatures += Signature $writer
& $SigntoolPath verify /pa /all /tw @($signatures.Path) *> (Join-Path $OutputDirectory 'signature-verification.log')
if ($LASTEXITCODE -ne 0) { throw 'Trusted signature verification failed' }
$result = [pscustomobject]@{ProductVersion=$props.ProductVersion;PreviousVersion=$oldProps.ProductVersion;UpgradeCode=$props.UpgradeCode;OriginalFileDestinationsAndRegistrationPreserved=$true;AllEmbeddedFilesMatchBuild=$true;Signatures=$signatures;Files=$verified}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $OutputDirectory 'verification.json') -Encoding utf8
Write-Output "Verified $($files.Count) embedded files against this build, original file destinations/registration, upgrade identity, and $($signatures.Count) timestamped Popokey signatures."
