param(
    [string]$SdkRoot = "D:\Android\Sdk",
    [string]$AvdName = "pixel_7_-_api_36_0"
)

$ErrorActionPreference = "Stop"
$installScript = Join-Path $PSScriptRoot "install-android-emulator.ps1"

if (-not (Test-Path (Join-Path $SdkRoot "emulator\emulator.exe"))) {
    Write-Host "Emulator not installed. Running setup..." -ForegroundColor Yellow
    & $installScript -SdkRoot $SdkRoot -AvdName $AvdName
}

$env:ANDROID_HOME = $SdkRoot
$env:ANDROID_SDK_ROOT = $SdkRoot
$emulator = Join-Path $SdkRoot "emulator\emulator.exe"
$adb = Join-Path $SdkRoot "platform-tools\adb.exe"

if (-not (Test-Path -LiteralPath $emulator)) {
    throw "emulator.exe still missing. Run: .\scripts\install-android-emulator.ps1"
}

$running = & $adb devices 2>$null | Select-String -Pattern "emulator-\d+\s+device"
if ($running) {
    Write-Host "Emulator already running:" -ForegroundColor Green
    & $adb devices
    exit 0
}

Write-Host "Launching $AvdName ..." -ForegroundColor Cyan
Start-Process -FilePath $emulator -ArgumentList @("-avd", $AvdName) -WindowStyle Normal

Write-Host "Waiting for emulator to boot (up to 3 minutes)..." -ForegroundColor DarkGray
$deadline = (Get-Date).AddMinutes(3)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    $ready = & $adb shell getprop sys.boot_completed 2>$null
    if ($ready -match "1") {
        Write-Host "Emulator is ready." -ForegroundColor Green
        & $adb devices
        exit 0
    }
}

Write-Host "Emulator started but boot is still in progress. Check the emulator window." -ForegroundColor Yellow
& $adb devices
