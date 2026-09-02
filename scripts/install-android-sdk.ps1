param(
    [string]$SdkRoot = "D:\Android\Sdk",
    [switch]$SkipJdk
)

$ErrorActionPreference = "Stop"

function Ensure-Jdk {
    if ($SkipJdk) { return }

    $java = Get-Command java -ErrorAction SilentlyContinue
    if ($java) {
        Write-Host "Java already available: $($java.Source)"
        return
    }

    if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
        throw "Java not found and winget unavailable. Install Microsoft OpenJDK 17 first."
    }

    Write-Host "Installing Microsoft OpenJDK 17..." -ForegroundColor Yellow
    winget install --id Microsoft.OpenJDK.17 -e --accept-package-agreements --accept-source-agreements
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" +
                [System.Environment]::GetEnvironmentVariable("Path", "User")
}

function Get-CommandLineToolsUrl {
    $repo = "https://dl.google.com/android/repository/repository2-3.xml"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $xml = (Invoke-WebRequest -Uri $repo -UseBasicParsing).Content
    if ($xml -match 'commandlinetools-win-(\d+)_latest\.zip') {
        $build = $Matches[1]
        return "https://dl.google.com/android/repository/commandlinetools-win-${build}_latest.zip"
    }
    return "https://dl.google.com/android/repository/commandlinetools-win-13114758_latest.zip"
}

function Install-AndroidSdk {
    param([string]$Root)

    New-Item -ItemType Directory -Force -Path $Root | Out-Null
    $toolsDir = Join-Path $Root "cmdline-tools\latest"
    $sdkmanager = Join-Path $toolsDir "bin\sdkmanager.bat"

    if (-not (Test-Path -LiteralPath $sdkmanager)) {
        $zipUrl = Get-CommandLineToolsUrl
        $zipPath = Join-Path $env:TEMP "android-cmdline-tools.zip"
        Write-Host "Downloading command line tools..." -ForegroundColor Yellow
        Write-Host $zipUrl
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing

        $extractRoot = Join-Path $env:TEMP "android-cmdline-tools"
        if (Test-Path $extractRoot) { Remove-Item $extractRoot -Recurse -Force }
        Expand-Archive -Path $zipPath -DestinationPath $extractRoot -Force

        New-Item -ItemType Directory -Force -Path (Split-Path $toolsDir -Parent) | Out-Null
        if (Test-Path $toolsDir) { Remove-Item $toolsDir -Recurse -Force }
        Move-Item (Join-Path $extractRoot "cmdline-tools") $toolsDir
        Remove-Item $zipPath -Force -ErrorAction SilentlyContinue
    }

    $env:ANDROID_HOME = $Root
    $env:ANDROID_SDK_ROOT = $Root

    Write-Host "Accepting SDK licenses..." -ForegroundColor Yellow
    $yes = ("y`n" * 100)
    $yes | & $sdkmanager --sdk_root=$Root --licenses | Out-Null

    $packages = @(
        "platform-tools",
        "platforms;android-36",
        "build-tools;36.0.0",
        "emulator",
        "system-images;android-36;google_apis_playstore;x86_64"
    )

    Write-Host "Installing SDK packages..." -ForegroundColor Yellow
    & $sdkmanager --sdk_root=$Root @packages
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Retry with build-tools;35.0.0..." -ForegroundColor Yellow
        & $sdkmanager --sdk_root=$Root "platform-tools" "platforms;android-36" "build-tools;35.0.0"
        if ($LASTEXITCODE -ne 0) { throw "sdkmanager failed." }
    }
}

function Set-AndroidEnv {
    param([string]$Root)

    [Environment]::SetEnvironmentVariable("ANDROID_HOME", $Root, "User")
    [Environment]::SetEnvironmentVariable("ANDROID_SDK_ROOT", $Root, "User")

    $platformTools = Join-Path $Root "platform-tools"
    $emulatorDir = Join-Path $Root "emulator"
    $userPath = [Environment]::GetEnvironmentVariable("Path", "User")
    foreach ($entry in @($platformTools, $emulatorDir)) {
        if ((Test-Path $entry) -and ($userPath -notlike "*$entry*")) {
            $userPath = if ([string]::IsNullOrWhiteSpace($userPath)) { $entry } else { "$userPath;$entry" }
        }
    }

    [Environment]::SetEnvironmentVariable("Path", $userPath, "User")

    $env:ANDROID_HOME = $Root
    $env:ANDROID_SDK_ROOT = $Root
    if ($env:Path -notlike "*$platformTools*") {
        $env:Path = "$env:Path;$platformTools"
    }

    if ((Test-Path $emulatorDir) -and ($env:Path -notlike "*$emulatorDir*")) {
        $env:Path = "$env:Path;$emulatorDir"
    }
}

Write-Host "=== Android SDK -> $SdkRoot ===" -ForegroundColor Cyan
Ensure-Jdk
Install-AndroidSdk -Root $SdkRoot
Set-AndroidEnv -Root $SdkRoot

Write-Host ""
Write-Host "Android SDK installed." -ForegroundColor Green
Write-Host "  ANDROID_HOME=$SdkRoot"
Write-Host "  platform-tools: $(Join-Path $SdkRoot 'platform-tools')"
Write-Host ""
Write-Host "Restart terminal or VS, then run:"
Write-Host "  .\scripts\start-android-emulator.ps1"
Write-Host "  .\scripts\publish-android.ps1"
