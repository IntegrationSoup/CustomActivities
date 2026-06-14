param(
    [switch]$Force
)

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

    if ($Force) {
        $arguments += "-Force"
    }

    try {
        Start-Process -FilePath $hostPath -ArgumentList $arguments -Verb RunAs -WorkingDirectory $PSScriptRoot | Out-Null
        Write-Host "A UAC prompt has been opened. Approve it to continue clearing the Custom Libraries folders."
    }
    catch {
        Write-Warning "Clearing the Custom Libraries folders requires administrator access. Re-run the script as administrator if you cancel the UAC prompt."
        Read-Host "Press Enter to close"
    }

    exit
}

$destinations = @(
    "C:\Program Files (x86)\Popokey\Integration Host Server\Custom Libraries",
    "C:\Program Files (x86)\Popokey\HL7 Soup\Custom Libraries",
    "C:\Program Files (x86)\Popokey\Integration Workflow Designer\Custom Libraries"
)

if (-not $Force) {
    Write-Host "This will permanently remove all files and subfolders from the following Custom Libraries folders:"
    $destinations | ForEach-Object { Write-Host " - $_" }

    $confirmation = Read-Host "Type CLEAR to continue"
    if ($confirmation -ne "CLEAR") {
        Write-Host "Cancelled."
        Read-Host "Press Enter to close"
        exit
    }
}

foreach ($destination in $destinations) {
    if (-not (Test-Path -LiteralPath $destination)) {
        Write-Host "Skipping missing folder: $destination"
        continue
    }

    Get-ChildItem -LiteralPath $destination -Force | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }

    Write-Host "Cleared: $destination"
}

Write-Host "All Custom Libraries folders have been cleared."
Read-Host "Press Enter to close"
