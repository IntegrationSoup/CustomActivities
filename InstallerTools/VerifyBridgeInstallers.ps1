param([Parameter(Mandatory=$true)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
$msi = New-Object -ComObject WindowsInstaller.Installer
function Read-Rows($database, [string]$table, [string[]]$columns) {
    $query = 'SELECT ' + (($columns | ForEach-Object { '`' + $_ + '`' }) -join ',') + ' FROM `' + $table + '`'
    try { $view = $database.OpenView($query) } catch { throw "MSI query failed: $query. $($_.Exception.Message)" }
    $view.Execute() | Out-Null
    try {
        while ($record = $view.Fetch()) {
            $row = [ordered]@{}
            for ($index = 0; $index -lt $columns.Count; $index++) { $row[$columns[$index]] = $record.StringData($index + 1) }
            [pscustomobject]$row
        }
    } finally { $view.Close() | Out-Null }
}
function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
$installers = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Filter 'IntegrationSoup.*.msi' -File)
Assert ($installers.Count -eq 10) 'Expected exactly ten review installers'
$providers = @{}
foreach ($file in $installers) {
    $database = $msi.OpenDatabase($file.FullName, 0)
    $files = @(Read-Rows $database 'File' @('File','Component_','FileName'))
    $components = @(Read-Rows $database 'Component' @('Component','Directory_','Attributes'))
    $registry = @(Read-Rows $database 'Registry' @('Registry','Root','Key','Name','Value','Component_'))
    $actions = @(Read-Rows $database 'CustomAction' @('Action','Type','Source','Target'))
    $sequence = @(Read-Rows $database 'InstallExecuteSequence' @('Action','Sequence','Condition'))
    Assert (!( $files | Where-Object { $_.FileName -match 'HL7SoupIntegrations\.dll' })) "Host API shipped in $($file.Name)"
    $registration = @($registry | Where-Object { $_.Name -eq 'ManifestPath' })
    Assert ($registration.Count -eq 1) 'One provider registration required'
    $registration = $registration[0]
    Assert ($registration.Root -eq '2' -and $registration.Key -match '^SOFTWARE\\Popokey\\IntegrationSoup\\ExtensionProviders\\[a-z0-9.-]+$') 'Registration must own one exact HKLM provider key'
    Assert (!$providers.ContainsKey($registration.Key)) 'Two installers own the same provider key'
    $providers[$registration.Key] = $true
    Assert ($registration.Value -eq '[#BridgeManifestFile]') 'Manifest path must use resolved installed file'
    $component = $components | Where-Object Component -eq $registration.Component_
    Assert (([int]$component.Attributes -band 256) -ne 0) 'Provider registration must use HKLM64'
    $tables = @(Read-Rows $database '_Tables' @('Name'))
    $copies = if ($tables.Name -contains 'DuplicateFile') { @(Read-Rows $database 'DuplicateFile' @('FileKey','DestFolder')) } else { @() }
    foreach ($folder in @('SOUPCUSTOMLIBRARIESFOLDER','WORKFLOWDESIGNERCUSTOMLIBRARIESFOLDER')) {
        $direct = @($components | Where-Object Directory_ -eq $folder | ForEach-Object { $componentId=$_.Component; $files | Where-Object { $_.Component_ -eq $componentId -and $_.FileName -match '\.dll$' } })
        Assert (($copies | Where-Object DestFolder -eq $folder) -or $direct.Count -gt 0) 'Legacy DLL must install to both desktop product folders'
    }
    $permissions = @(Read-Rows $database 'MsiLockPermissionsEx' @('LockObject','Table','SDDLText'))
    Assert ($permissions.Count -ge 2 -and !($permissions | Where-Object { $_.SDDLText -notmatch '^D:P' })) 'Manifest directory/file must have protected DACLs'
    $order = @{}; foreach ($row in $sequence) { $order[$row.Action] = [int]$row.Sequence }
    Assert ($order.RemoveExistingProducts -gt $order.InstallInitialize -and $order.RemoveExistingProducts -lt $order.InstallFiles) 'Major upgrade must be inside rollback transaction and precede new files'
    Assert ($order.RollbackBridgeManifest -gt $order.InstallFiles -and $order.WriteBridgeManifest -gt $order.RollbackBridgeManifest -and $order.CommitBridgeManifest -gt $order.WriteBridgeManifest -and $order.CommitBridgeManifest -lt $order.InstallFinalize) 'Manifest action transaction ordering invalid'
    foreach ($name in @('RollbackBridgeManifest','WriteBridgeManifest','CommitBridgeManifest')) {
        $action = $actions | Where-Object Action -eq $name
        Assert ($action.Source -eq 'BridgeWriter' -and ([int]$action.Type -band 3072) -eq 3072 -and $action.Target -match '\[#BridgeManifestFile\]' -and $action.Target -notmatch 'CustomActionData') 'Manifest executable action arguments/privilege invalid'
    }
    Write-Output "$($file.Name): own HKLM64 key, protected manifest, v4 copies, no host API, transaction and upgrade scheduling verified (database only)."
}
