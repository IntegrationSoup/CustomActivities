param(
    [Parameter(Mandatory = $true)]
    [string[]]$MsiPath
)

$ErrorActionPreference = 'Stop'

# Open MSI databases/sessions only. Never run an install sequence or change the registry.
function Get-MsiRows($Database, [string]$Table, [string[]]$Columns) {
    $view = $Database.OpenView('SELECT * FROM `' + $Table + '`')
    try {
        $view.Execute()
        while ($record = $view.Fetch()) {
            try {
                $row = [ordered]@{}
                for ($index = 0; $index -lt $Columns.Count; $index++) {
                    $row[$Columns[$index]] = $record.GetType().InvokeMember(
                        'StringData', 'GetProperty', $null, $record, @($index + 1))
                }
                [pscustomobject]$row
            }
            finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    }
    finally {
        $view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
    }
}

function Set-MsiProperty($Session, [string]$Name, [string]$Value) {
    [void]$Session.GetType().InvokeMember('Property', 'SetProperty', $null, $Session, @($Name, $Value))
}

function Assert-Check([bool]$Passed, [string]$Message) {
    if (-not $Passed) { throw $Message }
}

$installer = New-Object -ComObject WindowsInstaller.Installer
try {
    foreach ($path in $MsiPath) {
        $fullPath = (Resolve-Path -LiteralPath $path).Path
        $database = $installer.OpenDatabase($fullPath, 0)
        $session = $null
        try {
            $launch = @(Get-MsiRows $database 'LaunchCondition' @('Condition', 'Description') |
                Where-Object { $_.Condition -match 'WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED' })
            Assert-Check ($launch.Count -eq 1) "$path must contain exactly one .NET 4.8 launch condition."
            Assert-Check ($launch[0].Description -match '4\.8 Runtime' -and $launch[0].Description -match '/net48') 'The prerequisite message must identify the runtime and its download page.'

            $setter = @(Get-MsiRows $database 'CustomAction' @('Action', 'Type', 'Source', 'Target', 'ExtendedType') |
                Where-Object { $_.Source -eq 'WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED' })
            Assert-Check ($setter.Count -eq 1 -and $setter[0].Type -eq '51' -and $setter[0].Target -eq '1') 'Expected a native MSI property setter for the prerequisite.'

            $detectionCondition = $null
            foreach ($table in @('InstallUISequence', 'InstallExecuteSequence')) {
                $rows = @(Get-MsiRows $database $table @('Action', 'Condition', 'Sequence'))
                $search = @($rows | Where-Object Action -eq 'AppSearch')
                $detect = @($rows | Where-Object Action -eq $setter[0].Action)
                $gate = @($rows | Where-Object Action -eq 'LaunchConditions')
                Assert-Check ($search.Count -eq 1 -and $detect.Count -eq 1 -and $gate.Count -eq 1) "$table must search, detect .NET, and enforce launch conditions."
                Assert-Check ([int]$search[0].Sequence -lt [int]$detect[0].Sequence -and [int]$detect[0].Sequence -lt [int]$gate[0].Sequence) "$table must detect .NET before checking prerequisites."
                if ($null -ne $detectionCondition) {
                    Assert-Check ($detect[0].Condition -eq $detectionCondition) 'Interactive and silent installations must use the same detection.'
                }
                $detectionCondition = $detect[0].Condition
            }

            $searches = @(Get-MsiRows $database 'AppSearch' @('Property', 'Signature'))
            $frameworkSearch = @($searches | Where-Object Property -eq 'WIXNETFX4RELEASEINSTALLED')
            $locators = @(Get-MsiRows $database 'RegLocator' @('Signature', 'Root', 'Key', 'Name', 'Type'))
            $frameworkLocator = @($locators | Where-Object Signature -eq $frameworkSearch[0].Signature)
            Assert-Check ($frameworkLocator.Count -eq 1 -and $frameworkLocator[0].Root -eq '2' -and $frameworkLocator[0].Key -eq 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -and $frameworkLocator[0].Name -eq 'Release' -and $frameworkLocator[0].Type -eq '2') 'Expected the .NET Full Release DWORD in the 32-bit HKLM registry view.'

            # Ignore installed-product state; use Windows Installer's own expression evaluator.
            $session = $installer.OpenPackage($fullPath, 1)
            Assert-Check ($session.DoAction('AppSearch') -eq 1) 'Native framework registry search failed.'
            $registryBase = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine, [Microsoft.Win32.RegistryView]::Registry32)
            $frameworkKey = $registryBase.OpenSubKey('SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full')
            try {
                $actualRelease = if ($null -ne $frameworkKey) { $frameworkKey.GetValue('Release', 0) } else { 0 }
                Assert-Check ($session.EvaluateCondition($detectionCondition) -eq [int]($actualRelease -ge 528040)) 'Native MSI detection did not match this computer.'
            }
            finally {
                if ($null -ne $frameworkKey) { $frameworkKey.Dispose() }
                $registryBase.Dispose()
            }
            Set-MsiProperty $session 'Installed' ''
            foreach ($case in @(
                @{ Name = 'Missing framework'; Release = ''; Expected = 0 },
                @{ Name = 'Customer .NET 4.7.2'; Release = '#461814'; Expected = 0 },
                @{ Name = 'Below 4.8 threshold'; Release = '#528039'; Expected = 0 },
                @{ Name = '.NET 4.8 minimum'; Release = '#528040'; Expected = 1 },
                @{ Name = '.NET 4.8 Server 2019'; Release = '#528049'; Expected = 1 },
                @{ Name = '.NET 4.8 Server 2022'; Release = '#528449'; Expected = 1 },
                @{ Name = '.NET 4.8.1'; Release = '#533325'; Expected = 1 },
                @{ Name = '.NET 4.8.1 current'; Release = '#533509'; Expected = 1 }
            )) {
                Set-MsiProperty $session 'WIXNETFX4RELEASEINSTALLED' $case.Release
                Set-MsiProperty $session 'WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED' ''
                $detected = $session.EvaluateCondition($detectionCondition)
                Assert-Check ($detected -eq $case.Expected) "Incorrect detection: $($case.Name)."
                if ($detected -eq 1) {
                    Assert-Check ($session.DoAction($setter[0].Action) -eq 1) 'Failed to set prerequisite property.'
                }
                Assert-Check ($session.EvaluateCondition($launch[0].Condition) -eq $case.Expected) "Incorrect install/upgrade decision: $($case.Name)."
            }

            Set-MsiProperty $session 'WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED' '0'
            Assert-Check ($session.EvaluateCondition($launch[0].Condition) -eq 0) 'A false prerequisite property must block installation.'
            Set-MsiProperty $session 'WIX_IS_NETFRAMEWORK_48_OR_LATER_INSTALLED' ''
            Set-MsiProperty $session 'Installed' '1'
            Assert-Check ($session.EvaluateCondition($launch[0].Condition) -eq 1) 'Maintenance must remain available without .NET.'
            Set-MsiProperty $session 'REMOVE' 'ALL'
            Assert-Check ($session.EvaluateCondition($launch[0].Condition) -eq 1) 'Uninstall must remain available without .NET.'
            Write-Host "PASS: $(Split-Path $fullPath -Leaf) - 8 release cases, false-property, maintenance/uninstall, registry lookup, and UI/silent sequencing."
        }
        finally {
            if ($null -ne $session) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) }
            [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        }
    }
}
finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
