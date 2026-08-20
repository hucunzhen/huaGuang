param(
    [string]$BrokerHost = "127.0.0.1",
    [int]$Port = 1883,
    [string]$Topic = "monitor/+/telemetry"
)

$ErrorActionPreference = "Stop"

function Find-MosquittoSub {
    $candidates = @(
        (Join-Path $env:ProgramFiles "mosquitto\mosquitto_sub.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "mosquitto\mosquitto_sub.exe"),
        (Join-Path $env:LOCALAPPDATA "Programs\mosquitto\mosquitto_sub.exe")
    )
    foreach ($path in $candidates) {
        if (Test-Path -LiteralPath $path) { return $path }
    }
    $cmd = Get-Command mosquitto_sub -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    return $null
}

$sub = Find-MosquittoSub
if (-not $sub) {
    throw "mosquitto_sub not found. Install Mosquitto and run .\scripts\start-test-mqtt.ps1"
}

Write-Host "Subscribing to $Topic @ ${BrokerHost}:$Port (Ctrl+C to exit)" -ForegroundColor Cyan
& $sub -h $BrokerHost -p $Port -t $Topic -v
