param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"
$framework = "net10.0-windows10.0.19041.0"

Write-Host "Publishing Windows $Configuration (self-contained win-x64 via csproj)..." -ForegroundColor Cyan
dotnet publish $project -f $framework -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$publishDir = Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework\win-x64\publish"
Write-Host ""
Write-Host "Output:" -ForegroundColor Green
Write-Host $publishDir
Write-Host "Run: .\HuaGuang.Monitor.exe"
