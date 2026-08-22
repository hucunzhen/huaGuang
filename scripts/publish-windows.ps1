param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$RegenerateLines
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"
$framework = "net10.0-windows10.0.19041.0"

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

Write-Host "Publishing Windows $Configuration (self-contained win-x64 via csproj)..." -ForegroundColor Cyan

$generateLines = Join-Path $root "scripts\generate-line-excel.ps1"
if ($RegenerateLines -and (Test-Path $generateLines)) {
    Write-Host "Regenerating config\lines from line catalog ..." -ForegroundColor DarkGray
    & $generateLines
    if ($LASTEXITCODE -ne 0) { throw "generate-line-excel.ps1 failed." }
}

$iconScript = Join-Path $root "scripts\generate-appicon-ico.ps1"
if (Test-Path $iconScript) {
    Write-Host "Generating installer icon..." -ForegroundColor DarkGray
    & $iconScript
}

dotnet publish $project -f $framework -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$publishDir = Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework\win-x64\publish"
Sync-LineExcelToPublish -PublishDir $publishDir
Write-Host ""
Write-Host "Output:" -ForegroundColor Green
Write-Host $publishDir
Write-Host "Run: .\HuaGuang.Monitor.exe"
