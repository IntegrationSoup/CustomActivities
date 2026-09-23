param([Parameter(Mandatory=$true)][string]$ArtifactDirectory)
$ErrorActionPreference = 'Stop'
$installer = New-Object -ComObject WindowsInstaller.Installer
function Rows($database, [string]$table, [string[]]$columns) {
    $query = 'SELECT '+(($columns | ForEach-Object { '`'+$_+'`' }) -join ',')+' FROM `'+$table+'`'
    $view = $database.OpenView($query)
    try {
        $view.Execute() | Out-Null
        while ($record = $view.Fetch()) {
            $row = [ordered]@{}
            for($i=0;$i -lt $columns.Count;$i++) { $row[$columns[$i]]=$record.StringData($i+1) }
            [pscustomobject]$row
        }
    } finally { $view.Close() | Out-Null }
}
function Assert($condition, [string]$message) { if (!$condition) { throw $message } }
$results = @()
foreach ($file in Get-ChildItem -LiteralPath $ArtifactDirectory -Filter 'IntegrationSoup.*.msi') {
    $db=$installer.OpenDatabase($file.FullName,0)
    $tables=@(Rows $db '_Tables' @('Name')).Name
    if('ServiceControl' -in $tables) { Assert (@(Rows $db ServiceControl @('Name')).Count -eq 0) 'Unconditional native service controls remain' }
    $sequence=@{}; $conditions=@{}
    foreach($row in Rows $db InstallExecuteSequence @('Action','Condition','Sequence')) { $sequence[$row.Action]=[int]$row.Sequence; $conditions[$row.Action]=$row.Condition }
    Assert ($sequence.CaptureHostServices -lt $sequence.InstallInitialize) 'Safety guard must precede transaction/old removal'
    Assert ($sequence.RemoveExistingProducts -gt $sequence.InstallInitialize) 'Keep early removal in transaction'
    Assert ($sequence.RollbackLegacyHostServices -gt $sequence.RemoveExistingProducts) 'Do not generate rollback script before early removal (ICE63)'
    Assert ($sequence.StopLegacyHostServices -gt $sequence.RollbackLegacyHostServices -and $sequence.StopLegacyHostServices -lt $sequence.RemoveFiles) 'Drain before own file removal'
    Assert ($sequence.RollbackStopLegacyHostServices -gt $sequence.InstallFiles -and $sequence.RollbackStopLegacyHostServices -lt $sequence.RestoreLegacyHostServices) 'Late rollback must stop a restored host before rolling files back'
    Assert ($sequence.RestoreLegacyHostServices -gt $sequence.WriteBridgeManifest -and $sequence.RestoreLegacyHostServices -lt $sequence.InstallFinalize) 'Restore only after payload/manifest'
    Assert ($conditions.RollbackLegacyHostServices -eq 'NOT WIX_UPGRADE_DETECTED') 'Upgrade rollback must leave restoration to removed old package'
    Assert ($conditions.RestoreLegacyHostServices -eq 'NOT UPGRADINGPRODUCTCODE') 'Nested old-package removal must not prematurely restart service'
    Assert ([string]::IsNullOrEmpty($conditions.CaptureHostServices) -and [string]::IsNullOrEmpty($conditions.StopLegacyHostServices)) 'New-policy old uninstall must capture and drain'
    $actions=@{}; foreach($row in Rows $db CustomAction @('Action','Type','Source','Target')) { $actions[$row.Action]=$row }
    foreach($name in @('CaptureHostServices','StopLegacyHostServices','RestoreLegacyHostServices','RollbackLegacyHostServices','RollbackStopLegacyHostServices')) {
        Assert ($actions[$name].Source -eq 'HostServicePolicy' -and $actions[$name].Target -eq $name) 'Managed entrypoint wiring missing'
        $expected = if($name -eq 'CaptureHostServices'){1}elseif($name.StartsWith('Rollback')){3329}else{3073}
        Assert ([int]$actions[$name].Type -eq $expected) 'Wrong action execution/privilege/rollback type'
    }
    $properties=@{}; foreach($row in Rows $db Property @('Property','Value')) { $properties[$row.Property]=$row.Value }
    Assert ($properties.HostServiceProviderId -match '^popokey\.[a-z0-9]+$' -and $properties.HostServiceRunnerName -match '\.exe$') 'Provider safety-check identity missing'
    $upgrades=@(Rows $db Upgrade @('VersionMin','VersionMax','Attributes','ActionProperty'))
    Assert (@($upgrades | Where-Object ActionProperty -eq 'WIX_UPGRADE_DETECTED').Count -eq 1 -and @($upgrades | Where-Object ActionProperty -eq 'WIX_DOWNGRADE_DETECTED').Count -eq 1) 'Major upgrade/downgrade protection changed'
    $results += [pscustomobject]@{Package=$file.Name;Version=$properties.ProductVersion;NativeServiceControls=0;Sequence=$sequence;Checks='Passed';LiveInstallationTested=$false}
    Write-Output "PASS $($file.Name): service policy, upgrade/rollback sequencing, no native service restart rows."
}
Assert ($results.Count -eq 10) 'Expected all ten installers'
$results | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $ArtifactDirectory 'service-policy-table-checks.json') -Encoding utf8
