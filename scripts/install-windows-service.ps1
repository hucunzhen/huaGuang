param(
    [ValidateSet("Install", "Uninstall", "Start", "Stop", "Status")]
    [string]$Action = "Install",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$serviceProject = Join-Path $root "src\HuaGuang.Monitor.Service\HuaGuang.Monitor.Service.csproj"
$framework = "net10.0-windows10.0.19041.0"
$serviceName = "HuaGuangMonitor"
$displayName = "工业监控采集服务"

function Get-ServiceExePath {
    $publishDir = Join-Path $root "src\HuaGuang.Monitor.Service\bin\$Configuration\$framework\win-x64\publish"
    $exe = Join-Path $publishDir "HuaGuang.Monitor.Service.exe"
    if (-not (Test-Path $exe)) {
        throw "未找到服务程序，请先运行 publish-windows-service.ps1"
    }
    return $exe
}

switch ($Action) {
    "Install" {
        & (Join-Path $root "scripts\publish-windows-service.ps1") -Configuration $Configuration
        $exe = Get-ServiceExePath
        $binPath = "`"$exe`""
        sc.exe create $serviceName binPath= $binPath start= auto DisplayName= $displayName | Out-String | Write-Host
        sc.exe description $serviceName "工业监控 PLC 采集与 MQTT 推送后台服务" | Out-Null
        sc.exe start $serviceName | Out-String | Write-Host
        Write-Host "Windows 服务已安装并启动：$serviceName" -ForegroundColor Green
    }
    "Uninstall" {
        sc.exe stop $serviceName 2>$null | Out-Null
        sc.exe delete $serviceName | Out-String | Write-Host
        Write-Host "Windows 服务已卸载：$serviceName" -ForegroundColor Yellow
    }
    "Start" { sc.exe start $serviceName | Out-String | Write-Host }
    "Stop" { sc.exe stop $serviceName | Out-String | Write-Host }
    "Status" { sc.exe query $serviceName | Out-String | Write-Host }
}
