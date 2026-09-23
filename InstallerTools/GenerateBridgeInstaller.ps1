param(
    [Parameter(Mandatory=$true)][string]$RunnerSourceDir,
    [Parameter(Mandatory=$true)][string]$ExecutableName,
    [Parameter(Mandatory=$true)][string]$ProviderId,
    [string]$Revision,
    [string]$InstallerVersionFile,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [Parameter(Mandatory=$true)][string]$WriterPath
)
$ErrorActionPreference = 'Stop'
if (!$Revision) {
    $versionText = [IO.File]::ReadAllText($InstallerVersionFile)
    $versionMatch = [regex]::Match($versionText, 'InstallerVersion\s*=\s*"([0-9.]+)"')
    if (!$versionMatch.Success) { throw 'Generated installer version is required for payload revision' }
    $Revision = $versionMatch.Groups[1].Value
}
if ($ProviderId -notmatch '^[a-z0-9.-]+$' -or $Revision -notmatch '^[0-9.]+$') { throw 'Invalid provider or revision' }
$payloadRoot = [IO.Path]::GetFullPath($RunnerSourceDir)
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
if (!(Test-Path -LiteralPath (Join-Path $payloadRoot $ExecutableName))) { throw 'Runner executable has not been built' }
New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
$template = Join-Path $outputRoot ($ProviderId + '.json')
[IO.File]::WriteAllText($template, '{"SchemaVersion":1,"Enabled":false}', [Text.UTF8Encoding]::new($false))
$settings = [Xml.XmlWriterSettings]::new(); $settings.Indent = $true
$writer = [Xml.XmlWriter]::Create((Join-Path $outputRoot 'BridgeRegistration.wxs'), $settings)
$namespace = 'http://wixtoolset.org/schemas/v4/wxs'
function Start-Element($name, $attributes) {
    $writer.WriteStartElement($name, $namespace)
    foreach ($key in $attributes.Keys) { $writer.WriteAttributeString($key, [string]$attributes[$key]) }
}
function End-Element { $writer.WriteEndElement() }
function Element($name, $attributes) { Start-Element $name $attributes; End-Element }
try {
    Start-Element 'Wix' @{}
    Start-Element 'Fragment' @{}
    Start-Element 'DirectoryRef' @{ Id='SERVERCUSTOMLIBRARIESFOLDER' }
    Start-Element 'Directory' @{ Id='BridgeProvidersDirectory'; Name='ExtensionProviders' }
    Start-Element 'Directory' @{ Id='BridgeProviderDirectory'; Name=$ProviderId }
    Start-Element 'Directory' @{ Id='BridgeRevisionDirectory'; Name=$Revision }
    End-Element; End-Element; End-Element; End-Element
    Start-Element 'StandardDirectory' @{ Id='CommonAppDataFolder' }
    Start-Element 'Directory' @{ Id='BridgeDataCompany'; Name='Popokey' }
    Start-Element 'Directory' @{ Id='BridgeManifestRoot'; Name='ExtensionProviders' }
    Start-Element 'Directory' @{ Id='BridgeManifestDirectory'; Name=$ProviderId }
    End-Element; End-Element; End-Element; End-Element
    Start-Element 'ComponentGroup' @{ Id='BridgeProviderComponents' }
    $files = @(Get-ChildItem -LiteralPath $payloadRoot -File | Where-Object { $_.Extension -in '.exe','.dll','.config','.json' -and $_.Name -ne 'HL7SoupIntegrations.dll' })
    $nestedRuntimeFiles = @(Get-ChildItem -LiteralPath $payloadRoot -Directory | Get-ChildItem -Recurse -File | Where-Object { $_.Extension -in '.exe','.dll','.config','.json' })
    if ($nestedRuntimeFiles.Count) { throw 'Nested runtime payloads require explicit installer directory mapping; do not silently omit them' }
    if (Test-Path -LiteralPath (Join-Path $payloadRoot 'HL7SoupIntegrations.dll')) { throw 'Runner must not ship HL7SoupIntegrations.dll' }
    $index = 0
    foreach ($file in $files) {
        $fileId = if ($file.Name -eq $ExecutableName) { 'BridgeExecutableFile' } else { 'BridgePayloadFile' + $index }
        Start-Element 'Component' @{ Id=('BridgePayloadComponent' + $index); Directory='BridgeRevisionDirectory'; Guid='*'; Bitness='always32' }
        Element 'File' @{ Id=$fileId; Source=$file.FullName; KeyPath='yes' }
        End-Element
        $index++
    }
    Start-Element 'Component' @{ Id='BridgeManifestComponent'; Directory='BridgeManifestDirectory'; Guid='*'; Bitness='always64' }
    Start-Element 'CreateFolder' @{}
    Element 'PermissionEx' @{ Sddl='D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)(A;OICI;FRFX;;;BU)' }
    End-Element
    Start-Element 'File' @{ Id='BridgeManifestFile'; Source=$template; KeyPath='yes' }
    Element 'PermissionEx' @{ Sddl='D:P(A;;FA;;;SY)(A;;FA;;;BA)(A;;FR;;;BU)' }
    End-Element
    Start-Element 'RegistryKey' @{ Root='HKLM'; Key=('SOFTWARE\Popokey\IntegrationSoup\ExtensionProviders\'+$ProviderId); ForceDeleteOnUninstall='yes' }
    Element 'RegistryValue' @{ Name='ManifestPath'; Type='string'; Value='[#BridgeManifestFile]' }
    End-Element
    End-Element
    End-Element
    Element 'Binary' @{ Id='BridgeWriter'; SourceFile=$WriterPath }
    $arguments = '"[#BridgeManifestFile]" "[#BridgeExecutableFile]" "'+$ProviderId+'" "'+$Revision+'"'
    # Type-2 EXE targets are formatted when scheduled. CustomActionData is for
    # MSI-handle DLL/script actions, not a property to substitute into this target.
    Element 'CustomAction' @{ Id='RollbackBridgeManifest'; BinaryRef='BridgeWriter'; ExeCommand=('rollback '+$arguments); Execute='rollback'; Impersonate='no'; Return='check' }
    Element 'CustomAction' @{ Id='WriteBridgeManifest'; BinaryRef='BridgeWriter'; ExeCommand=('write '+$arguments); Execute='deferred'; Impersonate='no'; Return='check' }
    Element 'CustomAction' @{ Id='CommitBridgeManifest'; BinaryRef='BridgeWriter'; ExeCommand=('commit '+$arguments); Execute='commit'; Impersonate='no'; Return='check' }
    Start-Element 'InstallExecuteSequence' @{}
    $installCondition = 'NOT REMOVE~="ALL" AND $BridgeManifestComponent=3'
    Element 'Custom' @{ Action='RollbackBridgeManifest'; After='InstallFiles'; Condition=$installCondition }
    Element 'Custom' @{ Action='WriteBridgeManifest'; After='RollbackBridgeManifest'; Condition=$installCondition }
    Element 'Custom' @{ Action='CommitBridgeManifest'; After='WriteBridgeManifest'; Condition=$installCondition }
    End-Element
    End-Element
    End-Element
} finally { $writer.Dispose() }
