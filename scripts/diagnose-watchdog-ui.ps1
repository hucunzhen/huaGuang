# 诊断 UI 守护：服务、心跳文件、注册表、最近守护日志
$ErrorActionPreference = "Continue"

function Get-DataRoots {
    $pkg = "com.industrial.monitor"
    $roots = @(
        "$env:ProgramData\$pkg\Data",
        "$env:LOCALAPPDATA\$pkg\Data"
    ) | Select-Object -Unique
    return $roots
}

Write-Host "=== Windows 服务 ===" -ForegroundColor Cyan
foreach ($name in @("HuaGuangMonitorWatchdog", "HuaGuangMonitor")) {
    cmd /c "sc query $name" 2>&1
    Write-Host ""
}
$wd = sc.exe query HuaGuangMonitorWatchdog 2>&1 | Out-String
$ac = sc.exe query HuaGuangMonitor 2>&1 | Out-String
if ($wd -notmatch "RUNNING" -and $ac -match "RUNNING") {
    Write-Host "未安装独立守护服务时，UI 拉起由 HuaGuangMonitor 采集服务内嵌负责（需新版 Service.exe）。" -ForegroundColor Yellow
}

Write-Host "=== 开机自启（守护是否监视 UI） ===" -ForegroundColor Cyan
$run = Get-ItemProperty -Path "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run" -Name "IndustrialMonitor" -ErrorAction SilentlyContinue
if ($run.IndustrialMonitor) {
    Write-Host "IndustrialMonitor Run 项: $($run.IndustrialMonitor)" -ForegroundColor Green
}
else {
    Write-Host "未找到 IndustrialMonitor 开机自启（仍可依赖 24h 内 UI 心跳）" -ForegroundColor Yellow
}

Write-Host "`n=== UI 心跳 / 正常退出标记 ===" -ForegroundColor Cyan
foreach ($root in Get-DataRoots) {
    $uiJson = Join-Path $root "watchdog\ui.json"
    $origin = Join-Path $root "path-origin.txt"
    if (Test-Path $origin) {
        Write-Host "path-origin: $origin"
        Get-Content $origin | ForEach-Object { Write-Host "  $_" }
    }

    if (Test-Path $uiJson) {
        Write-Host "ui.json: $uiJson" -ForegroundColor Green
        Get-Content $uiJson -Raw
    }
    else {
        Write-Host "无 ui.json: $uiJson" -ForegroundColor DarkGray
    }
}

Write-Host "`n=== 守护巡检日志（最近 30 行） ===" -ForegroundColor Cyan
foreach ($root in Get-DataRoots) {
    $log = Join-Path $root "logs\watchdog-supervisor.log"
    if (Test-Path $log) {
        Write-Host "--- $log ---" -ForegroundColor Green
        Get-Content $log -Tail 30
    }
}

Write-Host "`n=== 当前 UI 进程 ===" -ForegroundColor Cyan
Get-Process -Name "HuaGuang.Monitor" -ErrorAction SilentlyContinue | Format-Table Id, StartTime, Path -AutoSize
if (-not (Get-Process -Name "HuaGuang.Monitor" -ErrorAction SilentlyContinue)) {
    Write-Host "HuaGuang.Monitor 未在运行" -ForegroundColor Yellow
}

Write-Host "`n验证步骤: 打开 UI 等 30s -> 任务管理器结束 HuaGuang.Monitor -> 等 90s -> 再运行本脚本看进程与 watchdog-supervisor.log" -ForegroundColor Cyan
