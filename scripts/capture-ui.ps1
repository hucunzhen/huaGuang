param(
    [string]$OutputDir = (Join-Path (Split-Path -Parent $PSScriptRoot) "docs\images")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Drawing;
public static class WinCapture {
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static void Capture(IntPtr handle, string path) {
        RECT r;
        if (!GetWindowRect(handle, out r)) throw new InvalidOperationException("GetWindowRect failed");
        int w = Math.Max(1, r.Right - r.Left);
        int h = Math.Max(1, r.Bottom - r.Top);
        using (var bmp = new System.Drawing.Bitmap(w, h)) {
            using (var g = System.Drawing.Graphics.FromImage(bmp))
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h), System.Drawing.CopyPixelOperation.SourceCopy);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
"@

$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\HuaGuang.Monitor\bin\Debug\net10.0-windows10.0.19041.0\win-x64\HuaGuang.Monitor.exe"
if (-not (Test-Path $exe)) {
    throw "Build the app first: dotnet build -f net10.0-windows10.0.19041.0 -c Debug"
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

function Wait-AppWindow([int]$pid, [int]$seconds = 30) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $p = Get-Process -Id $pid -ErrorAction SilentlyContinue
        if (-not $p) { return $null }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { return $p }
        Start-Sleep -Milliseconds 400
    }
    return Get-Process -Id $pid -ErrorAction SilentlyContinue
}

function Capture-Window($process, [string]$name) {
    if ($null -eq $process -or $process.MainWindowHandle -eq [IntPtr]::Zero) {
        Write-Warning "Skip $name - no main window"
        return
    }
    [WinCapture]::SetForegroundWindow($process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 600
    $path = Join-Path $OutputDir "$name.png"
    [WinCapture]::Capture($process.MainWindowHandle, $path)
    Write-Host "Saved $path"
}

$proc = Start-Process -FilePath $exe -PassThru
Start-Sleep -Seconds 3
$app = Wait-AppWindow $proc.Id 40
Capture-Window $app "01-dashboard"

# Tab switching via keyboard: Ctrl+Tab or try SendKeys for tab navigation
# MAUI Shell tabs - try Alt+1, Alt+2, Alt+3 or click simulation via coordinates
# Use SendKeys to switch tabs - on Shell TabBar, Ctrl+Tab might not work
# Try sending keys to cycle - for MAUI on Windows, clicking bottom tabs needs UI automation

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Click-TabByName($process, [string]$tabName) {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
    if ($null -eq $root) { return $false }
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $tabName)
    $tab = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $tab) { return $false }
    $pattern = $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
    if ($pattern) {
        $pattern.Select()
        return $true
    }
    $invoke = $tab.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    if ($invoke) {
        $invoke.Invoke()
        return $true
    }
    return $false
}

Start-Sleep -Seconds 1
if (Click-TabByName $app "点位") {
    Start-Sleep -Seconds 1
    Capture-Window $app "02-tags"
}
if (Click-TabByName $app "设置") {
    Start-Sleep -Seconds 1
    Capture-Window $app "03-settings"
}

Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
