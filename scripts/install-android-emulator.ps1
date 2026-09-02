param(
    [string]$SdkRoot = "D:\Android\Sdk",
    [string]$AvdName = "pixel_7_-_api_36_0",
    [switch]$StartAfterInstall
)

$ErrorActionPreference = "Stop"

function Get-SdkManager {
    param([string]$Root)
    $sdkmanager = Join-Path $Root "cmdline-tools\latest\bin\sdkmanager.bat"
    if (-not (Test-Path -LiteralPath $sdkmanager)) {
        throw "sdkmanager not found. Run .\scripts\install-android-sdk.ps1 first."
    }

    return $sdkmanager
}

function Ensure-AndroidEnv {
    param([string]$Root)

    $env:ANDROID_HOME = $Root
    $env:ANDROID_SDK_ROOT = $Root

    $paths = @(
        (Join-Path $Root "platform-tools"),
        (Join-Path $Root "emulator"),
        (Join-Path $Root "cmdline-tools\latest\bin")
    )

    foreach ($path in $paths) {
        if ((Test-Path $path) -and ($env:Path -notlike "*$path*")) {
            $env:Path = "$path;$env:Path"
        }
    }
}

function Install-EmulatorPackages {
    param(
        [string]$Root,
        [string]$SdkManager
    )

    $packages = @(
        "emulator",
        "system-images;android-36;google_apis_playstore;x86_64"
    )

    Write-Host "Installing Android Emulator packages..." -ForegroundColor Yellow
    Write-Host "  emulator"
    Write-Host "  system-images;android-36;google_apis_playstore;x86_64"
    Write-Host "This may take several minutes on first run." -ForegroundColor DarkGray

    $yes = ("y`n" * 100)
    $yes | & $SdkManager --sdk_root=$Root @packages
    if ($LASTEXITCODE -ne 0) {
        throw "sdkmanager failed to install emulator packages."
    }
}

function Test-AvdExists {
    param([string]$Name)

    $ini = Join-Path $env:USERPROFILE ".android\avd\$Name.ini"
    return Test-Path -LiteralPath $ini
}

function Start-AndroidEmulator {
    param(
        [string]$Root,
        [string]$Name
    )

    $emulator = Join-Path $Root "emulator\emulator.exe"
    if (-not (Test-Path -LiteralPath $emulator)) {
        throw "emulator.exe not found at $emulator"
    }

    if (-not (Test-AvdExists $Name)) {
        throw "AVD '$Name' not found. Create one in Android Device Manager (Pixel 7 / API 36 / x86_64)."
    }

    Write-Host "Starting emulator: $Name" -ForegroundColor Green
    Start-Process -FilePath $emulator -ArgumentList @("-avd", $Name) -WindowStyle Normal
}

Write-Host "=== Android Emulator setup -> $SdkRoot ===" -ForegroundColor Cyan

if (-not (Test-Path -LiteralPath $SdkRoot)) {
    throw "Android SDK not found at $SdkRoot. Run .\scripts\install-android-sdk.ps1 first."
}

Ensure-AndroidEnv -Root $SdkRoot
$sdkmanager = Get-SdkManager -Root $SdkRoot

if (-not (Test-Path (Join-Path $SdkRoot "emulator\emulator.exe"))) {
    Install-EmulatorPackages -Root $SdkRoot -SdkManager $sdkmanager
}
else {
    Write-Host "emulator.exe already present." -ForegroundColor DarkGray
}

$systemImageDir = Join-Path $SdkRoot "system-images\android-36\google_apis_playstore\x86_64"
if (-not (Test-Path -LiteralPath $systemImageDir)) {
    Install-EmulatorPackages -Root $SdkRoot -SdkManager $sdkmanager
}
else {
    Write-Host "System image already present." -ForegroundColor DarkGray
}

$emulatorDir = Join-Path $SdkRoot "emulator"
$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$emulatorDir*") {
    $newPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $emulatorDir } else { "$userPath;$emulatorDir" }
    [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
    Write-Host "Added emulator to user PATH." -ForegroundColor DarkGray
}

Write-Host ""
Write-Host "Android Emulator ready." -ForegroundColor Green
Write-Host "  emulator: $(Join-Path $SdkRoot 'emulator\emulator.exe')"
Write-Host "  AVD: $AvdName"
Write-Host ""
Write-Host "Start manually:"
Write-Host "  .\scripts\start-android-emulator.ps1"
Write-Host "Or from VS: Debug target -> Android Emulators"

if ($StartAfterInstall) {
    Start-AndroidEmulator -Root $SdkRoot -Name $AvdName
}
