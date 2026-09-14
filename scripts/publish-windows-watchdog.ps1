param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
. "$PSScriptRoot\DotNet-Helpers.ps1"
$root = Split-Path -Parent $PSScriptRoot
$framework = "net10.0-windows10.0.19041.0"
$project = Join-Path $root "src\HuaGuang.Monitor.Watchdog.Service\HuaGuang.Monitor.Watchdog.Service.csproj"

Write-Host "Publishing watchdog service $Configuration ..." -ForegroundColor Cyan
Invoke-DotNet publish $project `
    -c $Configuration `
    -f $framework `
    -r win-x64 `
    --self-contained true `
    "-p:PublishSingleFile=false"

$publishDir = Join-Path $root "src\HuaGuang.Monitor.Watchdog.Service\bin\$Configuration\$framework\win-x64\publish"
Write-Host $publishDir -ForegroundColor Green
