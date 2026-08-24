param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\HuaGuang.Monitor\HuaGuang.Monitor.csproj"
$framework = "net10.0-android"
$distDir = Join-Path $root "dist"
$installerOutputDir = Join-Path $root "installer\output"

if (-not $env:ANDROID_HOME -and -not $env:ANDROID_SDK_ROOT) {
    throw @"
Android SDK not found. Install Visual Studio workload '.NET Multi-platform App UI development',
or set ANDROID_HOME to your Android SDK path.
"@
}

Write-Host "Publishing Android $Configuration APK..." -ForegroundColor Cyan

$linesSrc = Join-Path $root "config\lines"
if (Test-Path $linesSrc) {
    $lineFiles = Get-ChildItem -Path $linesSrc -Filter "*.xlsx" -File |
        Where-Object { $_.Name -notlike "~$*" -and $_.Name -notlike "*.new.xlsx" }
    Write-Host "Bundling $($lineFiles.Count) line Excel file(s) from config\lines." -ForegroundColor DarkGray
}

dotnet publish $project -f $framework -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Publish failed." }

$publishDir = Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework\publish"
$searchRoots = @(
    $publishDir,
    (Join-Path $root "src\HuaGuang.Monitor\bin\$Configuration\$framework")
)

$signedApks = @()
foreach ($dir in $searchRoots) {
    if (Test-Path $dir) {
        $signedApks += Get-ChildItem -Path $dir -Filter *-Signed.apk -File -ErrorAction SilentlyContinue
    }
}
$signedApks = $signedApks | Sort-Object LastWriteTime -Descending

if (-not $signedApks) {
    throw @"
No signed APK (*-Signed.apk) found.
Do NOT install the unsigned .apk — Android will report 'package seems invalid'.
Rebuild Release and look under bin\Release\net10.0-android\publish\
"@
}

$signedApk = $signedApks |
    Where-Object { $_.Name -like "com.industrial.monitor-Signed.apk" } |
    Select-Object -First 1
if (-not $signedApk) {
    $signedApk = $signedApks | Select-Object -First 1
}

$appVer = & (Join-Path $PSScriptRoot "Get-AppVersion.ps1") $project

New-Item -ItemType Directory -Path $distDir -Force | Out-Null
New-Item -ItemType Directory -Path $installerOutputDir -Force | Out-Null
$destName = "IndustrialMonitor-$($appVer.FileSuffix)-android.apk"
$destPath = Join-Path $distDir $destName
$installerDestPath = Join-Path $installerOutputDir $destName
Copy-Item -Path $signedApk.FullName -Destination $destPath -Force
Copy-Item -Path $signedApk.FullName -Destination $installerDestPath -Force

$sizeMb = [math]::Round((Get-Item $destPath).Length / 1MB, 1)

Write-Host ""
Write-Host "Install THIS file on the tablet:" -ForegroundColor Green
Write-Host "  $installerDestPath"
Write-Host "  $destPath"
Write-Host "  ($sizeMb MB, signed, arm + arm64)"
Write-Host ""
Write-Host "Do NOT install:" -ForegroundColor Yellow
Write-Host "  * Unsigned .apk (without -Signed in the name)"
Write-Host "  * APK sent via WeChat/QQ (often corrupt) — use USB, zip, or cloud drive"
Write-Host ""
Write-Host "Source signed APK:" -ForegroundColor DarkGray
Write-Host "  $($signedApk.FullName)"
