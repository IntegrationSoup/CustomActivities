param()

$ErrorActionPreference = "Stop"

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
    ".pdb",
    ".deps.json",
    ".xml"
)

$excludedDirectories = @(
    "cs",
    "de",
    "es",
    "fr",
    "it",
    "ja",
    "ko",
    "pl",
    "pt-BR",
    "ru",
    "tr",
    "zh-Hans",
    "zh-Hant"
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

        if ($_.PSIsContainer -and ($excludedDirectories -contains $_.Name)) {
            return
        }

        if (-not $_.PSIsContainer -and ($excludedExtensions -contains $_.Extension)) {
            return
        }

        $targetPath = Join-Path -Path $destination -ChildPath $_.Name

        if ($_.PSIsContainer) {
            Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Recurse -Force
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $targetPath -Force
        }
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
