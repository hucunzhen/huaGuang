# 生成 config/lines 下的产线 Excel（提交到仓库，随安装包分发）
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outputDir = Join-Path $root "config\lines"
$tool = Join-Path $root "tools\GenerateLineExcel\GenerateLineExcel.csproj"

dotnet run --project $tool -- $outputDir
Write-Host "产线 Excel 已生成到: $outputDir" -ForegroundColor Green
