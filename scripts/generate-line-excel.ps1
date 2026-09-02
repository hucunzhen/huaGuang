# 从 LineCatalog 整本重新生成 config/lines（会覆盖 Excel 内全部配置，慎用）。
# 若只需改「运行状态」和「精度」，请用 scripts\patch-line-excel.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $root "config\lines"
$tool = Join-Path $root "tools\GenerateLineExcel\GenerateLineExcel.csproj"

dotnet run --project $tool -- $outputDir
Write-Host "产线 Excel 已生成到: $outputDir" -ForegroundColor Green
