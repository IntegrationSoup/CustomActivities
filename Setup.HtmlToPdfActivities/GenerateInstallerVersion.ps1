param(
    [Parameter(Mandatory = $true)]
    [string]$CounterFile,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile,

    [string]$InstallerVersion,
    [int]$Major = 1,
    [int]$MinorBlockSize = 10000
)

$ErrorActionPreference = "Stop"

if (-not [string]::IsNullOrWhiteSpace($InstallerVersion)) {
    if ($InstallerVersion -notmatch '^(?<major>\d+)\.(?<minor>\d+)\.(?<build>\d+)$') {
        throw "InstallerVersion must use the format Major.Minor.Build."
    }

    $majorPart = [int]$Matches.major
    $minorPart = [int]$Matches.minor
    $buildPart = [int]$Matches.build

    if ($majorPart -lt 0 -or $majorPart -gt 255) {
        throw "InstallerVersion major field '$majorPart' must be between 0 and 255."
    }

    if ($minorPart -lt 0 -or $minorPart -gt 255) {
        throw "InstallerVersion minor field '$minorPart' must be between 0 and 255."
    }

    if ($buildPart -lt 0 -or $buildPart -gt 65535) {
        throw "InstallerVersion build field '$buildPart' must be between 0 and 65535."
    }

    $counter = 0
}
else {
    if ($MinorBlockSize -le 0) {
        throw "MinorBlockSize must be greater than zero."
    }

    $counterDirectory = Split-Path -Path $CounterFile -Parent
    if (-not [string]::IsNullOrWhiteSpace($counterDirectory) -and -not (Test-Path -LiteralPath $counterDirectory)) {
        New-Item -ItemType Directory -Path $counterDirectory -Force | Out-Null
    }

    $counter = 0
    if (Test-Path -LiteralPath $CounterFile) {
        $rawCounter = (Get-Content -LiteralPath $CounterFile -Raw).Trim()
        if (-not [string]::IsNullOrWhiteSpace($rawCounter)) {
            $counter = [int]$rawCounter
        }
    }

    $counter++

    $minor = [math]::Floor($counter / $MinorBlockSize)
    $build = $counter % $MinorBlockSize

    if ($minor -gt 255) {
        throw "Installer build counter '$counter' exceeded the MSI version limit for the second field."
    }

    $InstallerVersion = "{0}.{1}.{2}" -f $Major, $minor, $build
    Set-Content -LiteralPath $CounterFile -Value $counter -Encoding ASCII
}

$outputDirectory = Split-Path -Path $OutputFile -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$generatedInclude = @"
<Include xmlns="http://wixtoolset.org/schemas/v4/wxs/include">
  <?define InstallerVersion = "$InstallerVersion" ?>
  <?define ProductVersionString = "$InstallerVersion" ?>
  <?define InstallerBuildCounter = "$counter" ?>
</Include>
"@

Set-Content -LiteralPath $OutputFile -Value $generatedInclude -Encoding UTF8

if ($counter -gt 0) {
    Write-Host "Generated installer version $InstallerVersion (build counter $counter)."
}
else {
    Write-Host "Using explicit installer version $InstallerVersion."
}
