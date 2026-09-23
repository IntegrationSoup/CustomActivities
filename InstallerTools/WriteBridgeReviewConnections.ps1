param([Parameter(Mandatory=$true)][string]$HL7SoupRoot, [string]$Version='5.0.1')
$ErrorActionPreference='Stop'
$repository=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$destination=Join-Path $repository "artifacts/bridge-review/$Version/connections"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$rows=@()
foreach($name in @('DataFromPdfActivities','HL7ValueTransformers','ZipActivities','HtmlToPdfActivities','RtfToPdfActivities','AzureActivities','AwsActivities','EncryptionActivities','SftpActivities','ValidateHl7Transformer')) {
    [xml]$project=Get-Content -LiteralPath (Join-Path $repository "Setup.$name/Setup.$name.wixproj") -Raw
    $group=$project.Project.PropertyGroup | Where-Object BridgeProviderId
    $executable=Join-Path $repository "$($group.BridgeRunnerProject)/bin/Release/$($group.BridgeTargetFramework)/$($group.BridgeExecutableName)"
    if(!(Test-Path -LiteralPath $executable)){throw "Missing $executable"}
    $arguments=@()
    if($name -eq 'ValidateHl7Transformer'){$arguments=@('--runtime-directory',(Join-Path ([IO.Path]::GetFullPath($HL7SoupRoot)) 'HL7SoupWorkflowHost/HL7SoupWorkflowHostWindowsService/bin/Debug/net10.0-windows'))}
    $manifest=[ordered]@{SchemaVersion=1;ProviderId=[string]$group.BridgeProviderId;Revision=$Version;Transport='NamedPipe';ExecutablePath=$executable;Arguments=$arguments;TimeoutSeconds=180;MaxMessageBytes=67108864;Enabled=$true}
    $path=Join-Path $destination ($group.BridgeProviderId+'.json')
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $path -Encoding utf8
    $rows += [pscustomobject]@{ProviderId=$group.BridgeProviderId;ExecutablePath=$executable;ManifestPath=$path}
}
$rows | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $destination 'index.json') -Encoding utf8
Write-Output "Development-only connection manifests: $destination. No registry or host settings changed."
