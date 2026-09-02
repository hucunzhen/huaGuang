param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$serviceProject = Join-Path $root "src\HuaGuang.Monitor.Service\HuaGuang.Monitor.Service.csproj"
$framework = "net10.0-windows10.0.19041.0"

Write-Host "Publishing Windows background service..." -ForegroundColor Cyan
dotnet publish $serviceProject `
    -c $Configuration `
    -f $framework `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=false

$publishDir = Join-Path $root "src\HuaGuang.Monitor.Service\bin\$Configuration\$framework\win-x64\publish"
$linesSrc = Join-Path $root "config\lines"
$linesDst = Join-Path $publishDir "lines"
New-Item -ItemType Directory -Force -Path $linesDst | Out-Null
Get-ChildItem $linesSrc -Filter "*.xlsx" -File |
    Where-Object { $_.Name -notlike "~$*" -and $_.Name -notlike "*.new.xlsx" } |
    ForEach-Object { Copy-Item $_.FullName (Join-Path $linesDst $_.Name) -Force }

Write-Host "Service publish output: $publishDir" -ForegroundColor Green
