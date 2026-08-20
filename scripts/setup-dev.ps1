param(
    [switch]$InstallMqtt,
    [switch]$SkipWorkloadRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"

Write-Host "=== Industrial Monitor - dev/test setup ===" -ForegroundColor Cyan

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "dotnet CLI not found. Install .NET SDK 10.0.302 or newer."
}

$sdkVersion = (dotnet --version).Trim()
Write-Host "SDK: $sdkVersion"

if (-not $SkipWorkloadRestore) {
    Write-Host ""
    Write-Host "Restoring MAUI workloads..." -ForegroundColor Yellow
    Push-Location $root
    try {
        dotnet workload restore
        $list = dotnet workload list 2>&1 | Out-String
        foreach ($id in @("maui-windows", "maui-android")) {
            if ($list -notmatch $id) {
                Write-Host "Installing workload: $id"
                dotnet workload install $id
            }
        }
    }
    finally {
        Pop-Location
    }
}

if ($InstallMqtt) {
    if (Get-Command mosquitto -ErrorAction SilentlyContinue) {
        Write-Host ""
        Write-Host "Mosquitto already installed."
    }
    elseif (Get-Command winget -ErrorAction SilentlyContinue) {
        Write-Host ""
        Write-Host "Installing Eclipse Mosquitto via winget..." -ForegroundColor Yellow
        winget install --id EclipseFoundation.Mosquitto -e --accept-package-agreements --accept-source-agreements
    }
    else {
        Write-Host ""
        Write-Host "winget not found. Install Mosquitto manually: https://mosquitto.org/download/" -ForegroundColor Yellow
    }
}

$androidHome = $env:ANDROID_HOME
if ([string]::IsNullOrWhiteSpace($androidHome)) {
    $androidHome = $env:ANDROID_SDK_ROOT
}
if ([string]::IsNullOrWhiteSpace($androidHome)) {
    Write-Host ""
    Write-Host "Android SDK not configured. Run: .\scripts\install-android-sdk.ps1" -ForegroundColor Yellow
}
else {
    Write-Host ""
    Write-Host "Android SDK: $androidHome"
}

Write-Host ""
Write-Host "Verifying Windows Debug build..." -ForegroundColor Yellow
dotnet build $project -f net10.0-windows10.0.19041.0 -c Debug --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

Write-Host ""
Write-Host "Dev environment ready." -ForegroundColor Green
Write-Host "  Debug: open HuaGuang.Monitor.slnx, F5 on Windows Machine"
Write-Host "  Test MQTT: .\scripts\start-test-mqtt.ps1"
Write-Host "  Publish: .\scripts\publish-windows.ps1"
