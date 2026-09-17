# 将「先河热熔胶复合机」产线 Excel 从错误的 S7+信捷D地址 改回 Modbus（紧急恢复界面/服务）
$ErrorActionPreference = 'Stop'
$linesDir = Join-Path $env:ProgramData 'com.industrial.monitor\Data\lines'
if (-not (Test-Path $linesDir)) {
    throw "未找到产线目录: $linesDir"
}

$excel = Get-ChildItem -LiteralPath $linesDir -Filter '*.xlsx' |
    Where-Object { $_.Name -like '*先河*' -or $_.Name -like '*热熔*' } |
    Select-Object -First 1
if (-not $excel) {
    $activeFile = Join-Path $linesDir '当前产线.txt'
    if (Test-Path $activeFile) {
        $lineName = (Get-Content -LiteralPath $activeFile -Encoding UTF8 -Raw).Trim()
        $candidate = Join-Path $linesDir ($lineName + '.xlsx')
        if (Test-Path -LiteralPath $candidate) { $excel = Get-Item -LiteralPath $candidate }
    }
}
if (-not $excel) {
    throw "未找到先河产线 Excel，请手动编辑 $linesDir 下对应 xlsx 的「配置」页：PLC协议=Modbus TCP，PLC端口=502"
}

$dll = Join-Path $PSScriptRoot '..\tools\SyncPlanningExcel\bin\Debug\net10.0\ClosedXML.dll'
$core = Join-Path $PSScriptRoot '..\src\HuaGuang.Monitor.Core\bin\Debug\net10.0\HuaGuang.Monitor.Core.dll'
if (-not (Test-Path $dll)) {
    dotnet build (Join-Path $PSScriptRoot '..\src\HuaGuang.Monitor.Core\HuaGuang.Monitor.Core.csproj') -c Debug | Out-Null
}

Add-Type -Path $dll
$path = $excel.FullName
Write-Host "修复: $path"
$wb = New-Object ClosedXML.Excel.XLWorkbook($path)
$sheet = $wb.Worksheet('配置')
$lastRow = $sheet.LastRowUsed().RowNumber()
for ($r = 2; $r -le $lastRow; $r++) {
    $key = $sheet.Cell($r, 1).GetString().Trim()
    switch ($key) {
        'PLC协议' { $sheet.Cell($r, 2).Value = 'Modbus TCP' }
        'PLC端口' { $sheet.Cell($r, 2).Value = '502' }
        'PLC_IP' {
            $ip = $sheet.Cell($r, 2).GetString().Trim()
            if ($ip -eq '127.0.0.1') {
                Write-Host '提示: PLC_IP 仍为 127.0.0.1，请在界面设置中改为现场 PLC 地址'
            }
        }
    }
}
$wb.Save()
$wb.Dispose()
Write-Host '已写回 Modbus TCP / 端口 502。请重新启动 HuaGuang.Monitor 与后台服务。'
