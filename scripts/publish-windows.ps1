param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$RegenerateLines
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\DotNet-Helpers.ps1"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"
$framework = "net10.0-windows10.0.19041.0"
$logFile = Join-Path $root "publish-windows.last.log"

function Sync-LineExcelToPublish {
    param([string]$PublishDir)

    $linesSrc = Join-Path $root "config\lines"
    if (-not (Test-Path $linesSrc)) {
        Write-Host "Warning: config\lines not found, skip line Excel sync." -ForegroundColor Yellow
        return
    }

    $linesDst = Join-Path $PublishDir "lines"
    New-Item -ItemType Directory -Force -Path $linesDst | Out-Null
    $files = Get-ChildItem -Path $linesSrc -Filter "*.xlsx" -File |
        Where-Object { $_.Name -notlike "~$*" -and $_.Extension -eq ".xlsx" -and $_.Name -notlike "*.new.xlsx" }
    if ($files.Count -eq 0) {
        Write-Host "Warning: no line Excel files under config\lines." -ForegroundColor Yellow
        return
    }

    foreach ($file in $files) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $linesDst $file.Name) -Force
    }

    Write-Host "Synced $($files.Count) line Excel file(s) from config\lines to publish\lines." -ForegroundColor DarkGray
}

function Sync-WindowIconToPublish {
    param([string]$PublishDir)

    $iconSrc = Join-Path $root "src\HuaGuang.Monitor\Resources\AppIcon\appicon.ico"
    if (-not (Test-Path $iconSrc)) {
        Write-Host "Warning: appicon.ico not found, skip title bar icon sync." -ForegroundColor Yellow
        return
    }

    Copy-Item -LiteralPath $iconSrc -Destination (Join-Path $PublishDir "logo.ico") -Force
    Write-Host "Synced title bar icon to publish\logo.ico." -ForegroundColor DarkGray
}

try {
    Start-Transcript -LiteralPath $logFile -Force | Out-Null
}
catch {
    Write-Host "Warning: could not write log $logFile" -ForegroundColor Yellow
}

try {
    Write-Host "Publishing Windows $Configuration (self-contained win-x64 via csproj)..." -ForegroundColor Cyan

    $generateLines = Join-Path $root "scripts\generate-line-excel.ps1"
    if ($RegenerateLines -and (Test-Path $generateLines)) {
        Write-Host "Regenerating config\lines from line catalog ..." -ForegroundColor DarkGray
        & $generateLines
        if (-not $?) { throw "generate-line-excel.ps1 failed." }
    }

    $iconScript = Join-Path $root "scripts\generate-appicon-ico.ps1"
    if (Test-Path $iconScript) {
        Write-Host "Generating installer icon..." -ForegroundColor DarkGray
        try {
            & $iconScript
        }
        catch {
            Write-Host "Warning: generate-appicon-ico.ps1 failed: $($_.Exception.Message)" -ForegroundColor Yellow
        }
    }

    # MSBuild bundle is disabled here; bundle script runs after publish.
    Invoke-DotNet publish $project -f $framework -c $Configuration "-p:BuildInstallerOnPublish=false" "-p:BundleBackgroundExesOnPublish=false"

    $publishDir = Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework\win-x64\publish"
    $mainExe = Join-Path $publishDir "HuaGuang.Monitor.exe"
    if (-not (Test-Path -LiteralPath $mainExe)) {
        throw ('MAUI publish did not produce: {0}' -f $mainExe)
    }

    Sync-LineExcelToPublish -PublishDir $publishDir
    Sync-WindowIconToPublish -PublishDir $publishDir

    $fixScript = Join-Path $root "scripts\fix-old-windows.ps1"
    $compatScript = Join-Path $root "scripts\configure-old-windows-compat.ps1"
    foreach ($helper in @($fixScript, $compatScript)) {
        if (Test-Path $helper) {
            Copy-Item -LiteralPath $helper -Destination (Join-Path $publishDir (Split-Path -Leaf $helper)) -Force
        }
    }
    $fixBat = @"
@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0fix-old-windows.ps1" -InstallDir "%~dp0"
pause
"@
    Set-Content -LiteralPath (Join-Path $publishDir "fix-old-windows.bat") -Value $fixBat -Encoding UTF8

    $bundleScript = Join-Path $PSScriptRoot "bundle-windows-service-to-publish.ps1"
    Write-Host "Publishing and bundling background + watchdog services..." -ForegroundColor Cyan
    & $bundleScript -PublishDir $publishDir -Configuration $Configuration
    if (-not $?) {
        throw "bundle-windows-service-to-publish.ps1 failed."
    }

    $bundledWatchdog = Join-Path $publishDir "service\HuaGuang.Monitor.Watchdog.Service.exe"
    if (-not (Test-Path -LiteralPath $bundledWatchdog)) {
        throw ('Publish finished but watchdog exe is missing: {0}' -f $bundledWatchdog)
    }

    Write-Host ""
    Write-Host "Output:" -ForegroundColor Green
    Write-Host $publishDir
    Write-Host "Run: .\HuaGuang.Monitor.exe"
    Write-Host "Service: .\service\HuaGuang.Monitor.Service.exe"
    Write-Host "Watchdog: .\service\HuaGuang.Monitor.Watchdog.Service.exe"
    Write-Host ""
    Write-Host "[OK] publish-windows succeeded. Log: $logFile" -ForegroundColor Green
    Write-Host "     publish-windows.bat also runs Inno Setup; or: build-installer.bat -SkipPublish" -ForegroundColor DarkGray
    exit 0
}
catch {
    Write-Host ""
    Write-Host "[FAILED] $($_.Exception.Message)" -ForegroundColor Red
    if (Test-Path -LiteralPath $logFile) {
        Write-Host "See log: $logFile" -ForegroundColor Yellow
    }
    exit 1
}
finally {
    try { Stop-Transcript | Out-Null } catch { }
}
