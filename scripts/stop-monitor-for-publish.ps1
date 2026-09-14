# Stop services and UI so publish\service can be replaced.
$ErrorActionPreference = "SilentlyContinue"

foreach ($serviceName in @("HuaGuangMonitor", "HuaGuangMonitorWatchdog")) {
    $query = & sc.exe query $serviceName 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { continue }
    if ($query -match "RUNNING") {
        Write-Host "Stopping Windows service: $serviceName" -ForegroundColor DarkGray
        & sc.exe stop $serviceName | Out-Null
        for ($i = 0; $i -lt 15; $i++) {
            Start-Sleep -Seconds 1
            $q = & sc.exe query $serviceName 2>&1 | Out-String
            if ($q -notmatch "RUNNING") { break }
        }
    }
}

Get-Process -Name "HuaGuang.Monitor" -ErrorAction SilentlyContinue |
    ForEach-Object {
        Write-Host "Closing HuaGuang.Monitor.exe (PID $($_.Id))..." -ForegroundColor DarkGray
        Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
    }

Start-Sleep -Seconds 2
