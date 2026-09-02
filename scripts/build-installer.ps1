param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"
$framework = "net10.0-windows10.0.19041.0"
$publishDir = Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework\win-x64\publish"
$iss = Join-Path $root "installer\IndustrialMonitor.iss"
$outputDir = Join-Path $root "installer\output"
$resolveScript = Join-Path $PSScriptRoot "Resolve-InnoSetup.ps1"

function Get-IsccPath {
    $output = & $resolveScript -CompilerPath $env:InnoSetupCompiler 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    if (-not $output) { return $null }
    return ($output | Select-Object -Last 1).ToString().Trim()
}

if (-not $SkipPublish) {
    $publishScript = Join-Path $PSScriptRoot "publish-windows.ps1"
    Write-Host "Publishing Windows $Configuration ..." -ForegroundColor Cyan
    & $publishScript -Configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
}

$exe = Join-Path $publishDir "HuaGuang.Monitor.exe"
if (-not (Test-Path $exe)) {
    throw "Publish output not found: $exe`nRun publish-windows.ps1 first or omit -SkipPublish."
}

$serviceExe = Join-Path $publishDir "service\HuaGuang.Monitor.Service.exe"
if (-not (Test-Path $serviceExe)) {
    throw "Service publish output not found: $serviceExe`nRun publish-windows.ps1 first or omit -SkipPublish."
}

$iscc = Get-IsccPath
if (-not $iscc) {
    Write-Host "Inno Setup not found. Installing via winget ..." -ForegroundColor Yellow
    winget install --id JRSoftware.InnoSetup -e --accept-package-agreements --accept-source-agreements
    Start-Sleep -Seconds 2
    $iscc = Get-IsccPath
}

if (-not $iscc) {
    throw @"
Inno Setup (ISCC.exe) not found.
Install from https://jrsoftware.org/isinfo.php
Typical path: $env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe
Or set env INNO_SETUP_ISCC to the full path.
Then re-run: .\scripts\build-installer.ps1
"@
}

Write-Host "Using ISCC: $iscc" -ForegroundColor DarkGray
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$appVer = & (Join-Path $PSScriptRoot "Get-AppVersion.ps1") $project

Write-Host "Building installer ($($appVer.Label)) ..." -ForegroundColor Cyan
& $iscc "/DPublishDir=$publishDir" "/DMyAppVersion=$($appVer.Version)" "/DMyAppRevision=$($appVer.Revision)" $iss
if ($LASTEXITCODE -ne 0) { throw "Installer build failed." }

$setup = Join-Path $outputDir "IndustrialMonitor-$($appVer.FileSuffix)-Setup.exe"
Write-Host ""
Write-Host "Installer ready:" -ForegroundColor Green
Write-Host "  $setup"
Write-Host ""
Write-Host "Copy this file to the industrial PC and double-click to install." -ForegroundColor Cyan
