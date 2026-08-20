# Resolves Inno Setup compiler (ISCC.exe). Writes full path to stdout; exit 0 if found.
param(
    [string]$CompilerPath
)

$ErrorActionPreference = "SilentlyContinue"

function Test-Iscc([string]$Path) {
    return -not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path -LiteralPath $Path)
}

function Get-RegistryIsccPaths {
    $roots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    $paths = @()
    foreach ($root in $roots) {
        Get-ItemProperty $root | Where-Object { $_.DisplayName -like "Inno Setup*" } | ForEach-Object {
            if ($_.InstallLocation) {
                $candidate = Join-Path $_.InstallLocation.TrimEnd('\') "ISCC.exe"
                if (Test-Iscc $candidate) { $paths += $candidate }
            }
        }
    }
    return $paths
}

if (Test-Iscc $CompilerPath) {
    Write-Output $CompilerPath
    exit 0
}

if (Test-Iscc $env:INNO_SETUP_ISCC) {
    Write-Output $env:INNO_SETUP_ISCC
    exit 0
}

$staticCandidates = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "D:\Program Files\Inno Setup 6\ISCC.exe",
    "D:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "D:\Program Files\Inno Setup 7\ISCC.exe",
    "D:\Program Files (x86)\Inno Setup 7\ISCC.exe"
)

foreach ($path in $staticCandidates) {
    if (Test-Iscc $path) {
        Write-Output $path
        exit 0
    }
}

$registryPaths = Get-RegistryIsccPaths | Select-Object -Unique
foreach ($path in ($registryPaths | Sort-Object { $_ -notlike "*Inno Setup 6*" })) {
    if (Test-Iscc $path) {
        Write-Output $path
        exit 0
    }
}

exit 1
