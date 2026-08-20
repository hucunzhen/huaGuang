param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("windows", "android", "all")]
    [string]$Platform = "windows"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"

function Invoke-Build {
    param([string]$Framework)
    Write-Host "dotnet build -f $Framework -c $Configuration" -ForegroundColor Cyan
    dotnet build $project -f $Framework -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $Framework" }
}

switch ($Platform) {
    "windows" { Invoke-Build "net10.0-windows10.0.19041.0" }
    "android" { Invoke-Build "net10.0-android" }
    "all" {
        Invoke-Build "net10.0-windows10.0.19041.0"
        Invoke-Build "net10.0-android"
    }
}

Write-Host ""
Write-Host "Build OK ($Configuration / $Platform)." -ForegroundColor Green
