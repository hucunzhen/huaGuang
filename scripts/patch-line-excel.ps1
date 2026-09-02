# 仅更新产线 Excel 中的「运行状态」类型与「精度」配置项，不重写点表。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $root "config\lines"
$tool = Join-Path $root "tools\GenerateLineExcel\GenerateLineExcel.csproj"

dotnet run --project $tool -- --patch $outputDir
if ($LASTEXITCODE -ne 0) { throw "patch-line-excel failed." }

Write-Host "产线 Excel 已按项修补: $outputDir" -ForegroundColor Green
