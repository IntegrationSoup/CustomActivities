param(
    [string]$ExpectedVersion,
    [string]$ExpectedHashesJson = '{}',
    [string]$OutputDirectory = 'C:\InstallerAcceptance',
    [switch]$DownloadOnly
)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$runDirectory = Join-Path $OutputDirectory ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$expectedHashes = $ExpectedHashesJson | ConvertFrom-Json
$packages = @('AwsActivities','AzureActivities','DataFromPdfActivities','EncryptionActivities','HL7ValueTransformers','HtmlToPdfActivities','RtfToPdfActivities','SftpActivities','ValidateHl7Transformer','ZipActivities')
$installer = New-Object -ComObject WindowsInstaller.Installer
$results = @()
$environment = [ordered]@{
    OS = (Get-CimInstance Win32_OperatingSystem).Caption
    FrameworkRelease = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction SilentlyContinue).Release
    Net35Enabled = (Get-WindowsOptionalFeature -Online -FeatureName NetFx3 -ErrorAction SilentlyContinue).State.ToString()
    RunDirectory = $runDirectory
}
foreach($package in $packages) {
    $name = 'IntegrationSoup.'+$package+'.msi'
    $url = 'https://www.integrationsoup.com/downloads/customactivities/'+$name.ToLowerInvariant()
    $file = Join-Path $runDirectory $name
    $result = [ordered]@{Package=$package;Url=$url;Version=$null;Sha256=$null;ExitCode=$null;AutomaticLogs=@();Error=$null}
    try {
        Invoke-WebRequest -Uri $url -OutFile $file -UseBasicParsing
        $result.Sha256 = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash
        $signature = Get-AuthenticodeSignature -LiteralPath $file
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne '1586FB787BD45270D870F90918EF887A121EB6DB' -or !$signature.TimeStamperCertificate) {throw 'Website MSI signature or publisher verification failed'}
        $database=$installer.OpenDatabase($file,0)
        $view=$database.OpenView('SELECT `Value` FROM `Property` WHERE `Property`=''ProductVersion''')
        $view.Execute(); $record=$view.Fetch(); $result.Version=$record.StringData(1); $view.Close()
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view)
        [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
        if($ExpectedVersion -and $result.Version -ne $ExpectedVersion) {throw 'Website MSI version does not match the release'}
        $expected=$expectedHashes.PSObject.Properties[$name]
        if($ExpectedVersion -and !$expected) {throw 'Expected release hash missing'}
        if($expected -and $result.Sha256 -ne $expected.Value) {throw 'Website MSI hash does not match the verified build'}
        if(!$DownloadOnly) {
            $start=Get-Date
            # Deliberately omit /L: automatic MSI logging is under test.
            $process=Start-Process msiexec.exe -ArgumentList @('/i',('"'+$file+'"'),'/qn','/norestart') -WindowStyle Hidden -PassThru
            if(!$process.WaitForExit(180000)) {throw 'Installer exceeded three minutes; stop this test before running another MSI'}
            $result.ExitCode=$process.ExitCode
            $result.AutomaticLogs=@(Get-ChildItem $env:TEMP -Filter 'MSI*.log' -ErrorAction SilentlyContinue | Where-Object LastWriteTime -ge $start | Select-Object -ExpandProperty FullName)
            if($process.ExitCode -notin @(0,3010)) {throw ('Installer failed with exit code '+$process.ExitCode)}
            if($ExpectedVersion -and !$result.AutomaticLogs.Count) {throw 'Installation did not produce an automatic log'}
        }
    } catch { $result.Error=$_.Exception.Message }
    $results += [pscustomobject]$result
    @{Environment=$environment;Results=$results} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $runDirectory 'results.json') -Encoding UTF8
    if($result.Error -like '*exceeded three minutes*') {break}
}
# Keep Azure Run Command output within its bounded tail; retain full evidence on disk.
@{Environment=$environment;Results=@($results | Select-Object Package,Version,ExitCode,Error);Failures=@($results | Where-Object Error).Count} | ConvertTo-Json -Depth 5 -Compress
if(@($results | Where-Object Error).Count) {exit 1}
