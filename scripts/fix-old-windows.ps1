param(
    [string]$InstallDir
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallDir)) {
    $InstallDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

$uiExe = Join-Path $InstallDir "HuaGuang.Monitor.exe"
$serviceExe = Join-Path $InstallDir "service\HuaGuang.Monitor.Service.exe"
$watchdogExe = Join-Path $InstallDir "service\HuaGuang.Monitor.Watchdog.Service.exe"
$targets = @($uiExe, $serviceExe, $watchdogExe) | Where-Object { Test-Path -LiteralPath $_ }

if ($targets.Count -eq 0) {
    throw "No executables found under: $InstallDir"
}

$compatScript = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "configure-old-windows-compat.ps1"
& $compatScript -ExePath $targets

Write-Host ""
Write-Host "Old Windows compatibility configured for:" -ForegroundColor Green
foreach ($path in $targets) {
    Write-Host "  $path"
}

Write-Host ""
Write-Host "If the app still fails to start, collect logs from:" -ForegroundColor Yellow
Write-Host "  $env:ProgramData\com.industrial.monitor\Data\logs"
