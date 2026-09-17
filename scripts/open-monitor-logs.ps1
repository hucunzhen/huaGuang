# 打开华光工业监控数据目录与当日日志（无需启动界面）
$ErrorActionPreference = 'Stop'
$dataRoot = Join-Path $env:ProgramData 'com.industrial.monitor\Data'
$logDir = Join-Path $dataRoot 'logs'
$linesDir = Join-Path $dataRoot 'lines'

Write-Host "数据目录: $dataRoot"
Write-Host "日志目录: $logDir"

if (-not (Test-Path $logDir)) {
    $fallback = Join-Path $env:LOCALAPPDATA 'com.industrial.monitor\Data\logs'
    if (Test-Path $fallback) {
        $logDir = $fallback
        $dataRoot = Split-Path $logDir -Parent
        Write-Host "使用 LocalAppData: $dataRoot"
    }
}

if (Test-Path $logDir) {
    explorer.exe $logDir
    $today = Get-Date -Format 'yyyyMMdd'
    $crash = Join-Path $logDir "crash-$today.log"
    $runtime = Join-Path $logDir "runtime-ui-$today.log"
    $bootstrap = Join-Path $logDir "bootstrap-$today.log"
    foreach ($f in @($crash, $runtime, $bootstrap)) {
        if (Test-Path $f) {
            Write-Host "打开: $f"
            Start-Process notepad.exe $f
            break
        }
    }
} else {
    Write-Warning "未找到日志目录。若程序从未成功启动，请查看安装目录旁是否有 bootstrap 日志。"
}

if (Test-Path $linesDir) {
    Write-Host "产线 Excel 目录: $linesDir"
}
