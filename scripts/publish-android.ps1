param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"
$framework = "net10.0-android"

if (-not $env:ANDROID_HOME -and -not $env:ANDROID_SDK_ROOT) {
    throw @"
Android SDK not found. Install Visual Studio workload '.NET Multi-platform App UI development',
or set ANDROID_HOME to your Android SDK path.
"@
}

Write-Host "Publishing Android $Configuration APK..." -ForegroundColor Cyan
dotnet publish $project -f $framework -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$publishDir = Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework\publish"
Write-Host ""
Write-Host "Output:" -ForegroundColor Green
Write-Host $publishDir
Get-ChildItem -Path $publishDir -Filter *.apk -Recurse -ErrorAction SilentlyContinue |
    ForEach-Object { Write-Host "  APK: $($_.FullName)" }
