[CmdletBinding(DefaultParameterSetName='Setup')]
param(
    [Parameter(ParameterSetName='Validate')][switch]$ValidateOnly,
    [Parameter(ParameterSetName='Remove')][switch]$Remove
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Signing.Common.ps1')
$mutex = $null
$form = $null
$boxes = @()
$secure = $null
$roundtrip = $null
try {
    $mutex = Enter-SigningLock
    if ($ValidateOnly) { $SigningDirectory = Join-Path $env:TEMP ('CustomActivitiesSigning-Preflight-' + [guid]::NewGuid().ToString('N')) }
    if ($Remove) {
        if (Test-Path -LiteralPath $SigningDirectory) {
            Assert-SigningAcl $SigningDirectory -Directory
            foreach ($name in @('pin.dpapi','pin.new','attempt.lock','setup-status.txt')) {
                $path = Join-Path $SigningDirectory $name
                if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
            }
        }
        Write-Output 'Saved signing PIN and failure lock removed. Future signed builds require setup.'
        return
    }
    Initialize-SigningDirectory
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    Add-Type -Path (Join-Path $PSScriptRoot 'NativeSigner.cs')
    $form = [Windows.Forms.Form]::new()
    $form.Text = 'CustomActivities - save signing PIN'
    $form.ClientSize = [Drawing.Size]::new(560,330)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.MinimizeBox = $false
    $form.TopMost = $true
    $label = [Windows.Forms.Label]::new()
    $label.Location = [Drawing.Point]::new(18,15)
    $label.Size = [Drawing.Size]::new(524,140)
    $label.Text = "Save your existing YubiKey PIV PIN for future unattended CustomActivities builds under this Windows account. Windows encrypts it with user-scoped DPAPI outside the repository. Other programs running as this account can decrypt it.`r`n`r`nSaving replaces the previous PIN and clears any failed-attempt lock. Check the PIN carefully; a failed signing attempt stops further attempts. To remove it later, run Setup-Signing.ps1 -Remove."
    $form.Controls.Add($label)
    foreach ($entry in @(@('PIN',165),@('Confirm PIN',208))) {
        $caption = [Windows.Forms.Label]::new()
        $caption.Text = $entry[0]
        $caption.Location = [Drawing.Point]::new(18,$entry[1])
        $caption.Size = [Drawing.Size]::new(110,24)
        $form.Controls.Add($caption)
        $box = [Windows.Forms.TextBox]::new()
        $box.UseSystemPasswordChar = $true
        $box.MaxLength = 8
        $box.Location = [Drawing.Point]::new(135,$entry[1])
        $box.Size = [Drawing.Size]::new(390,24)
        $form.Controls.Add($box)
        $boxes += ,$box
    }
    $submit = [Windows.Forms.Button]::new()
    $submit.Text = 'Save encrypted PIN'
    $submit.Location = [Drawing.Point]::new(270,275)
    $submit.Size = [Drawing.Size]::new(160,32)
    $cancel = [Windows.Forms.Button]::new()
    $cancel.Text = 'Cancel'
    $cancel.Location = [Drawing.Point]::new(440,275)
    $cancel.Size = [Drawing.Size]::new(90,32)
    $cancel.DialogResult = 'Cancel'
    $form.Controls.AddRange(@($submit,$cancel))
    $form.AcceptButton = $submit
    $form.CancelButton = $cancel
    $submit.Add_Click({
        if ($boxes[0].Text.Length -lt 6 -or $boxes[0].Text -cne $boxes[1].Text) {
            [Windows.Forms.MessageBox]::Show('Enter the same existing 6-8 character PIN in both boxes. No PIN was submitted.','Check entry') | Out-Null
            return
        }
        $form.DialogResult = 'OK'
        $form.Close()
    })
    if ($ValidateOnly) {
        # Exercise the actual on-disk DPAPI format (including newline handling),
        # child-file ACLs and failure lock, without reading real credentials.
        $secure = ConvertTo-SecureString 'nonsecret-format-check' -AsPlainText -Force
        $pinPath = Join-Path $SigningDirectory 'pin.dpapi'
        ($secure | ConvertFrom-SecureString) + "`r`n" | Set-Content -LiteralPath $pinPath
        Assert-SigningReady
        $roundtrip = (Get-Content -LiteralPath $pinPath -Raw).Trim() | ConvertTo-SecureString
        $pointer = [Runtime.InteropServices.Marshal]::SecureStringToGlobalAllocUnicode($roundtrip)
        try {
            if ([Runtime.InteropServices.Marshal]::PtrToStringUni($pointer) -cne 'nonsecret-format-check') { throw 'DPAPI round-trip failed.' }
        } finally { [Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($pointer) }
        [IO.File]::WriteAllText((Join-Path $SigningDirectory 'attempt.lock'), 'synthetic interrupted attempt')
        $blocked = $false
        try { Assert-SigningReady } catch { $blocked = $_.Exception.Message.StartsWith('Signing is locked') }
        if (!$blocked) { throw 'Failure lock did not block a subsequent attempt.' }
        Write-Output 'Preflight passed: native compilation, masked form construction, restricted ACLs, DPAPI file round-trip and persistent failure lock. No token or real PIN used.'
        return
    }
    Set-Content -LiteralPath (Join-Path $SigningDirectory 'setup-status.txt') -Value 'WaitingForLocalPin'
    $form.Add_Shown({ $form.Activate(); $boxes[0].Focus() })
    if ($form.ShowDialog() -ne 'OK') {
        Set-Content -LiteralPath (Join-Path $SigningDirectory 'setup-status.txt') -Value 'Cancelled'
        return
    }
    $secure = [Security.SecureString]::new()
    for ($index=0; $index -lt $boxes[0].Text.Length; $index++) { $secure.AppendChar($boxes[0].Text[$index]) }
    $secure.MakeReadOnly()
    foreach ($box in $boxes) { $box.Clear() }
    $newPath = Join-Path $SigningDirectory 'pin.new'
    $secure | ConvertFrom-SecureString | Set-Content -LiteralPath $newPath -NoNewline
    Assert-SigningAcl $newPath
    Move-Item -LiteralPath $newPath -Destination (Join-Path $SigningDirectory 'pin.dpapi') -Force
    $failureLock = Join-Path $SigningDirectory 'attempt.lock'
    if (Test-Path -LiteralPath $failureLock) { Remove-Item -LiteralPath $failureLock }
    Set-Content -LiteralPath (Join-Path $SigningDirectory 'setup-status.txt') -Value 'Saved'
    Write-Output 'Encrypted PIN saved for this Windows account. No token authentication was attempted by setup.'
} catch {
    # Never render the original ErrorRecord: it may include a secret expression.
    Write-Error ('Signing setup failed at line ' + $_.InvocationInfo.ScriptLineNumber + '. No automatic retry occurred.') -ErrorAction Continue
    exit 1
} finally {
    if ($secure) { $secure.Dispose() }
    if ($roundtrip) { $roundtrip.Dispose() }
    foreach ($box in $boxes) { if (!$box.IsDisposed) { $box.Clear() } }
    if ($form) { $form.Dispose() }
    if ($ValidateOnly -and $SigningDirectory -and (Test-Path -LiteralPath $SigningDirectory)) {
        # Only the unique synthetic test files created above; never recurse.
        foreach ($name in @('pin.dpapi','attempt.lock')) {
            $path = Join-Path $SigningDirectory $name
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
        Remove-Item -LiteralPath $SigningDirectory
    }
    if ($mutex) { $mutex.ReleaseMutex(); $mutex.Dispose() }
}
