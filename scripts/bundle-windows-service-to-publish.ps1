param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$PublishDir = $PublishDir.Trim().TrimEnd('\', '/')
$stopScript = Join-Path $PSScriptRoot "stop-monitor-for-publish.ps1"
if (Test-Path -LiteralPath $stopScript) {
    & $stopScript
}
$root = Split-Path -Parent $PSScriptRoot
$framework = "net10.0-windows10.0.19041.0"

if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

$serviceScript = Join-Path $PSScriptRoot "publish-windows-service.ps1"
$watchdogScript = Join-Path $PSScriptRoot "publish-windows-watchdog.ps1"

& $serviceScript -Configuration $Configuration

& $watchdogScript -Configuration $Configuration

$servicePublishDir = Join-Path $root "src\HuaGuang.Monitor.Service\bin\$Configuration\$framework\win-x64\publish"
$watchdogPublishDir = Join-Path $root "src\HuaGuang.Monitor.Watchdog.Service\bin\$Configuration\$framework\win-x64\publish"
$serviceDestDir = Join-Path $PublishDir "service"

if (-not (Test-Path -LiteralPath $servicePublishDir)) {
    throw "Service publish output missing: $servicePublishDir"
}

if (-not (Test-Path -LiteralPath $watchdogPublishDir)) {
    throw "Watchdog publish output missing: $watchdogPublishDir"
}

$watchdogExe = Join-Path $watchdogPublishDir "HuaGuang.Monitor.Watchdog.Service.exe"
if (-not (Test-Path -LiteralPath $watchdogExe)) {
    throw "Watchdog executable missing: $watchdogExe"
}

if (Test-Path -LiteralPath $serviceDestDir) {
    try {
        Remove-Item -LiteralPath $serviceDestDir -Recurse -Force -ErrorAction Stop
    }
    catch {
        $backup = Join-Path $PublishDir ("service.old." + [Guid]::NewGuid().ToString("N"))
        Write-Host "Warning: publish\service locked; moving aside to: $backup" -ForegroundColor Yellow
        Rename-Item -LiteralPath $serviceDestDir -NewName (Split-Path -Leaf $backup) -ErrorAction Stop
    }
}

New-Item -ItemType Directory -Force -Path $serviceDestDir | Out-Null
Copy-Item -Path (Join-Path $servicePublishDir "*") -Destination $serviceDestDir -Recurse -Force
Copy-Item -Path (Join-Path $watchdogPublishDir "*") -Destination $serviceDestDir -Recurse -Force

Write-Host "Bundled service + watchdog into: $serviceDestDir" -ForegroundColor Green
