param()

$ErrorActionPreference = "Stop"

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    $hostPath = (Get-Process -Id $PID).Path
    $arguments = @(
        "-ExecutionPolicy", "Bypass",
        "-File", "`"$PSCommandPath`""
    )

    try {
        Start-Process -FilePath $hostPath -ArgumentList $arguments -Verb RunAs -WorkingDirectory $PSScriptRoot | Out-Null
        Write-Host "A UAC prompt has been opened. Approve it to continue deployment."
    }
    catch {
        Write-Warning "Deployment requires administrator access to write into Program Files. Re-run the script as administrator if you cancel the UAC prompt."
        Read-Host "Press Enter to close"
    }

    exit
}

$destinations = @(
    "C:\Program Files (x86)\Popokey\Integration Host Server\Custom Libraries",
    "C:\Program Files (x86)\Popokey\HL7 Soup\Custom Libraries",
    "C:\Program Files (x86)\Popokey\Integration Workflow Designer\Custom Libraries"
)

$excludedNames = @(
    "HL7SoupIntegrations.dll",
    "HL7SoupIntegrations.dll.config",
    "HL7SoupIntegrations.pdb"
)

$excludedExtensions = @(
    ".config",
    ".deps.json",
    ".json",
    ".ps1",
    ".xml"
)

$sourcePath = $PSScriptRoot

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Source path '$sourcePath' was not found."
}

foreach ($destination in $destinations) {
    if (-not (Test-Path -LiteralPath $destination)) {
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
    }

    Get-ChildItem -LiteralPath $sourcePath -Force | ForEach-Object {
        if ($excludedNames -contains $_.Name) {
            return
        }

        if ($_.PSIsContainer) {
            return
        }

        if ($excludedExtensions -contains $_.Extension) {
            return
        }

        $targetPath = Join-Path -Path $destination -ChildPath $_.Name

        Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Force
    }
}

try {
    $serviceName = "IntegrationSoupHostService"
    $service = Get-Service -Name $serviceName -ErrorAction Stop

    if ($service.Status -eq "Running") {
        Restart-Service -Name $serviceName -Force -ErrorAction Stop
        Write-Host "Restarted service '$serviceName'."
    }
    else {
        Start-Service -Name $serviceName -ErrorAction Stop
        Write-Host "Started service '$serviceName'."
    }
}
catch {
    Write-Warning "Could not restart the Integration Soup Host Service automatically. Restart service 'IntegrationSoupHostService' manually if required. $($_.Exception.Message)"
}

Write-Host "Deployment complete."
Write-Host "Restart any open Integration Soup applications for the updated custom libraries to take effect."
Read-Host "Press Enter to close"
