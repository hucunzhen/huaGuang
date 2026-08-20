# 备用：命令行安装。现场请使用 installer/output/IndustrialMonitor-Setup.exe
param(
    [string]$InstallDir = "C:\Program Files\IndustrialMonitor",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipPublish,
    [switch]$NoStartup
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"
$framework = "net10.0-windows10.0.19041.0"
$registryName = "IndustrialMonitor"

if (-not $SkipPublish) {
    Write-Host "Publishing Windows $Configuration..." -ForegroundColor Cyan
    dotnet publish $project -f $framework -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Publish failed." }
}

$publishDir = Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework\win-x64\publish"
if (-not (Test-Path (Join-Path $publishDir "HuaGuang.Monitor.exe"))) {
    throw "Publish output not found: $publishDir"
}

Write-Host "Installing to $InstallDir ..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $publishDir "*") -Destination $InstallDir -Recurse -Force

$exe = Join-Path $InstallDir "HuaGuang.Monitor.exe"
$runKey = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"

if ($NoStartup) {
    Remove-ItemProperty -Path $runKey -Name $registryName -ErrorAction SilentlyContinue
    Write-Host "Startup registration skipped." -ForegroundColor Yellow
}
else {
    Set-ItemProperty -Path $runKey -Name $registryName -Value "`"$exe`""
    Write-Host "Registered startup: $registryName" -ForegroundColor Green
}

$desktop = [Environment]::GetFolderPath("Desktop")
$shortcut = Join-Path $desktop "工业监控.lnk"
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut($shortcut)
$link.TargetPath = $exe
$link.WorkingDirectory = $InstallDir
$link.Description = "工业监控"
$link.Save()

Write-Host ""
Write-Host "Installed:" -ForegroundColor Green
Write-Host "  $exe"
Write-Host "  Desktop shortcut: $shortcut"
if (-not $NoStartup) {
    Write-Host "  Startup: enabled (disable with -NoStartup or in app Settings)"
}
