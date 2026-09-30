param([Parameter(Mandatory=$true)][string]$ExpectedVersion, [string]$OutputFile)
$ErrorActionPreference='Stop'
$root=Join-Path $env:ProgramData 'Popokey\ExtensionProviders'
$manifests=@(Get-ChildItem $root -Filter '*.json' -Recurse | ForEach-Object {Get-Content $_.FullName -Raw | ConvertFrom-Json} | Where-Object Revision -eq $ExpectedVersion)
if($manifests.Count -ne 10){throw "Expected ten installed provider manifests at version $ExpectedVersion; found $($manifests.Count)"}
function Read-FrameBytes($stream,[int]$length){
    $bytes=New-Object byte[] $length
    $offset=0
    while($offset -lt $length){
        $read=$stream.ReadAsync($bytes,$offset,$length-$offset)
        if(!$read.Wait(15000)){throw 'Runner response timed out'}
        if($read.Result -eq 0){throw 'Runner closed an incomplete response'}
        $offset+=$read.Result
    }
    return ,$bytes
}
$results=@()
foreach($manifest in $manifests){
    $process=$null
    $result=[ordered]@{Provider=$manifest.ProviderId;Revision=$manifest.Revision;CatalogSize=0;Requests=0;Error=$null}
    try{
        if(!(Test-Path -LiteralPath $manifest.ExecutablePath)){throw 'Registered runner is missing'}
        $signature=Get-AuthenticodeSignature -LiteralPath $manifest.ExecutablePath
        if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Thumbprint -ne '1586FB787BD45270D870F90918EF887A121EB6DB'){throw 'Installed runner publisher verification failed'}
        $pipeName='installer-acceptance-'+[guid]::NewGuid().ToString('N')
        $process=Start-Process -FilePath $manifest.ExecutablePath -ArgumentList @('--server','--pipe-name',$pipeName,'--parent-pid',$PID,'--bridge-version','1') -WindowStyle Hidden -PassThru
        foreach($iteration in 1..2){
            $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
            try{
                $pipe.Connect(15000)
                $requestId=[guid]::NewGuid().ToString('N')
                $payload=@{SchemaVersion=1;ProviderId=$manifest.ProviderId} | ConvertTo-Json -Compress
                $envelope=@{ProtocolVersion=1;RequestId=$requestId;Operation='extension.describe';PayloadJson=$payload} | ConvertTo-Json -Compress
                $bytes=[Text.Encoding]::UTF8.GetBytes($envelope)
                $header=[BitConverter]::GetBytes($bytes.Length)
                $pipe.Write($header,0,4);$pipe.Write($bytes,0,$bytes.Length);$pipe.Flush()
                $length=[BitConverter]::ToInt32((Read-FrameBytes $pipe 4),0)
                if($length -le 0 -or $length -gt 4MB){throw 'Invalid catalog frame size'}
                $response=[Text.Encoding]::UTF8.GetString((Read-FrameBytes $pipe $length)) | ConvertFrom-Json
                if(!$response.Success -or $response.RequestId -ne $requestId){throw 'Runner catalog request failed or returned the wrong request ID'}
                $catalog=$response.PayloadJson | ConvertFrom-Json
                if($catalog.ProviderId -ne $manifest.ProviderId -or !$catalog.Extensions.Count){throw 'Runner catalog is empty or has the wrong provider identity'}
                $result.CatalogSize=$catalog.Extensions.Count
                $result.Requests++
            } finally {$pipe.Dispose()}
        }
    }catch{$result.Error=$_.Exception.Message}
    finally{
        # Only stop the temporary process created by this test, never a host runner.
        if($process){if(!$process.HasExited){$process.Kill();[void]$process.WaitForExit(10000)};$process.Dispose()}
    }
    $results += [pscustomobject]$result
}
$json=$results | ConvertTo-Json -Depth 4
if($OutputFile){$json | Set-Content -LiteralPath $OutputFile -Encoding UTF8}
$json
if(@($results | Where-Object Error).Count){exit 1}
