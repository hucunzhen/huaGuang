param(
    [int]$Port = 1883,
    [switch]$InstallIfMissing
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$config = Join-Path $root "test\mosquitto.conf"

function Find-Mosquitto {
    $candidates = @(
        (Join-Path $env:ProgramFiles "mosquitto\mosquitto.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "mosquitto\mosquitto.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\mosquitto\mosquitto.exe")
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    $cmd = Get-Command mosquitto -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$mosquitto = Find-Mosquitto
if (-not $mosquitto) {
    if ($InstallIfMissing -and (Get-Command winget -ErrorAction SilentlyContinue)) {
        Write-Host "Installing Mosquitto..." -ForegroundColor Yellow
        winget install --id EclipseFoundation.Mosquitto -e --accept-package-agreements --accept-source-agreements
        $mosquitto = Find-Mosquitto
    }
}

if (-not $mosquitto) {
    throw @"
Mosquitto not found. Install a local MQTT broker first:
  winget install EclipseFoundation.Mosquitto
Or run: .\scripts\setup-dev.ps1 -InstallMqtt
"@
}

Write-Host "Starting test MQTT broker: $mosquitto" -ForegroundColor Cyan
Write-Host "  Listen: 127.0.0.1:$Port"
Write-Host "  Config: $config"
Write-Host "  Subscribe: .\scripts\subscribe-telemetry.ps1"
Write-Host "Press Ctrl+C to stop." -ForegroundColor DarkGray

if (Test-Path -LiteralPath $config) {
    & $mosquitto -c $config -v
}
else {
    & $mosquitto -p $Port -v
}
