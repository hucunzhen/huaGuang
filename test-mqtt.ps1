# 测试 MQTT Broker 连接（账号/密码/地址来自 LineMqttDefaults.cs）
param(
    [ValidateSet("xianhe", "huadi")]
    [string]$Line = "xianhe",
    [switch]$NoPublish,
    [int]$TimeoutSeconds = 5,
    [switch]$Help
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$project = Join-Path $root "tools\MqttConnectionTest\MqttConnectionTest.csproj"

if ($Help) {
    Write-Host @"
MQTT 连接测试（默认配置见 src/HuaGuang.Monitor.Core/Services/LineMqttDefaults.cs）

用法:
  .\test-mqtt.ps1 [-Line xianhe|huadi] [-NoPublish] [-TimeoutSeconds 5]

示例:
  .\test-mqtt.ps1
  .\test-mqtt.ps1 -Line huadi
  .\test-mqtt.ps1 -NoPublish -TimeoutSeconds 10
"@
    exit 0
}

if (-not (Test-Path -LiteralPath $project)) {
    throw "未找到测试工具: $project"
}

$defaultsFile = Join-Path $root "src\HuaGuang.Monitor.Core\Services\LineMqttDefaults.cs"
if (Test-Path -LiteralPath $defaultsFile) {
    $text = Get-Content -LiteralPath $defaultsFile -Raw -Encoding UTF8
    $hostMatch = [regex]::Match($text, 'Host\s*=\s*"([^"]+)"')
    $portMatch = [regex]::Match($text, 'Port\s*=\s*(\d+)')
    $userMatch = [regex]::Match($text, 'Username\s*=\s*"([^"]+)"')
    if ($hostMatch.Success -and $portMatch.Success -and $userMatch.Success) {
        Write-Host "配置来源: LineMqttDefaults.cs" -ForegroundColor DarkGray
        Write-Host "  Broker: $($hostMatch.Groups[1].Value):$($portMatch.Groups[1].Value)" -ForegroundColor DarkGray
        Write-Host "  账号  : $($userMatch.Groups[1].Value)" -ForegroundColor DarkGray
        Write-Host ""
    }
}

$dotnetArgs = @(
    "run",
    "--project", $project,
    "-c", "Release",
    "--",
    "--line", $Line,
    "--timeout", $TimeoutSeconds
)
if ($NoPublish) {
    $dotnetArgs += "--no-publish"
}

& dotnet @dotnetArgs
exit $LASTEXITCODE
