$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$testProject = Join-Path $root "test\HuaGuang.Monitor.Tests\HuaGuang.Monitor.Tests.csproj"

Write-Host "Running core unit tests..." -ForegroundColor Cyan
dotnet test $testProject -c Release --verbosity normal

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "Unit tests passed." -ForegroundColor Green
Write-Host "For UI/integration checks, open the app and use the 诊断 tab." -ForegroundColor Yellow
