# 按「华光数据地址规划.xlsx」同步 config/lines（默认仅更新先河/华迪；加 --create-lines 可重建其余产线）。
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$planning = Join-Path $root "华光数据地址规划.xlsx"
$linesDir = Join-Path $root "config\lines"
if (-not (Test-Path $planning)) {
    Write-Error "找不到规划文件: $planning"
}
dotnet run --project (Join-Path $root "tools\SyncPlanningExcel\SyncPlanningExcel.csproj") -- $planning $linesDir
