param([switch]$SkipModels, [switch]$NoLaunch,
    [ValidateSet('qwen35', 'gemma', 'all')][string]$TextModel = 'qwen35')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$installRoot = Join-Path $env:USERPROFILE 'Applications\LazyType'
$appDir = Join-Path $installRoot 'app'
if (-not $SkipModels) {
    & python (Join-Path $PSScriptRoot 'setup_models.py') --text-model $TextModel
    if ($LASTEXITCODE -ne 0) { throw 'Model setup failed.' }
}
$existing = Join-Path $appDir 'LazyType.exe'
if (Test-Path -LiteralPath $existing) {
    $signal = Start-Process -FilePath $existing -ArgumentList '--quit' -WindowStyle Hidden -PassThru
    $signal.WaitForExit()
    Start-Sleep -Milliseconds 700
}
& dotnet publish (Join-Path $projectRoot 'src/LazyType/LazyType.csproj') -c Release -r win-x64 --self-contained false -o $appDir
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$exe = Join-Path $appDir 'LazyType.exe'
# Explorer brokers registration outside packaged-terminal filesystem/registry
# virtualization, so the normal Windows shell can see the startup entry.
$shell = New-Object -ComObject Shell.Application
$receipt = Join-Path $installRoot 'installation.json'
$previousReceipt = if (Test-Path -LiteralPath $receipt) { (Get-Item -LiteralPath $receipt).LastWriteTimeUtc.Ticks } else { 0 }
$shell.ShellExecute($exe, '--register-startup', $appDir, 'open', 0)
$registered = $false
for ($attempt = 0; $attempt -lt 50; $attempt++) {
    Start-Sleep -Milliseconds 200
    if ((Test-Path -LiteralPath $receipt) -and (Get-Item -LiteralPath $receipt).LastWriteTimeUtc.Ticks -ne $previousReceipt) {
        try {
            $registration = Get-Content -LiteralPath $receipt -Raw | ConvertFrom-Json
            $registered = $registration.startup -and $registration.executable -eq $exe
        } catch { }
        if ($registered) { break }
    }
}
if (-not $registered) { throw 'Startup registration did not finish. Run LazyType.exe --register-startup.' }
Write-Output "Installed: $exe"
Write-Output 'Startup enabled. Hotkey: Ctrl+Alt+Space. Pause: Ctrl+Alt+Shift+P.'
if (-not $NoLaunch) { $shell.ShellExecute($exe, '', $appDir, 'open', 0) }
