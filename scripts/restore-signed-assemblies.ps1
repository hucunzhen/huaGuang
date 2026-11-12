# ReadyToRun rewrites third-party assemblies and strips Authenticode.
# Smart App Control then blocks the load (0x800711C7 / "无法确认谁发布了").
# Copy the signed NuGet file listed in deps.json back over each rewritten DLL.
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $PublishDir)) {
    throw "发布目录不存在：$PublishDir"
}

$nugetRoot = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE ".nuget\packages" }
$depsFiles = @(Get-ChildItem -LiteralPath $PublishDir -Filter "*.deps.json" -File)
if ($depsFiles.Count -eq 0) {
    throw "发布目录没有 deps.json：$PublishDir"
}

$restored = New-Object System.Collections.Generic.List[string]
$alreadySigned = New-Object System.Collections.Generic.List[string]
$unsignedUpstream = New-Object System.Collections.Generic.List[string]
$failed = New-Object System.Collections.Generic.List[string]

function Get-TfmRank([string]$path) {
    $match = [regex]::Match($path, 'net(\d+)\.(\d+)')
    if (-not $match.Success) { return 0 }
    return ([int]$match.Groups[1].Value * 100) + [int]$match.Groups[2].Value
}

function Find-SignedNuGetDll([string]$leaf, [string]$installedPath) {
    $wanted = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installedPath).FileVersion
    $matches = @(Get-ChildItem -Path $nugetRoot -Recurse -Filter $leaf -File -ErrorAction SilentlyContinue | Where-Object {
        $_.FullName -notmatch '\\ref\\' -and
        [System.Diagnostics.FileVersionInfo]::GetVersionInfo($_.FullName).FileVersion -eq $wanted -and
        (Get-AuthenticodeSignature -FilePath $_.FullName).Status -eq 'Valid'
    })
    if ($matches.Count -eq 0) { return $null }
    return $matches | Sort-Object { Get-TfmRank $_.FullName } -Descending | Select-Object -First 1
}

foreach ($depsFile in $depsFiles) {
    $deps = Get-Content -LiteralPath $depsFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    $libraries = $deps.libraries
    foreach ($targetProp in $deps.targets.PSObject.Properties) {
        foreach ($packageProp in $targetProp.Value.PSObject.Properties) {
            $package = $packageProp.Value
            $library = $libraries.PSObject.Properties[$packageProp.Name]
            if (-not $library) { continue }
            $packageDir = $null
            if ($library.Value.path) {
                $packageDir = Join-Path $nugetRoot ($library.Value.path -replace '/', '\')
            }
            $groups = @($package.runtime, $package.native) | Where-Object { $_ }
            foreach ($group in $groups) {
                foreach ($fileProp in $group.PSObject.Properties) {
                    $leaf = Split-Path $fileProp.Name -Leaf
                    $installed = Join-Path $PublishDir $leaf
                    if (-not (Test-Path -LiteralPath $installed)) { continue }

                    $installedSignature = Get-AuthenticodeSignature -FilePath $installed
                    if ($installedSignature.Status -eq "Valid") {
                        if (-not $alreadySigned.Contains($leaf)) { $alreadySigned.Add($leaf) }
                        continue
                    }

                    $source = $null
                    if ($packageDir) {
                        $candidate = Join-Path $packageDir ($fileProp.Name -replace '/', '\')
                        if (Test-Path -LiteralPath $candidate) { $source = $candidate }
                    }
                    if (-not $source -and $leaf -notlike "HuaGuang.*") {
                        $fallback = Find-SignedNuGetDll -leaf $leaf -installedPath $installed
                        if ($fallback) { $source = $fallback.FullName }
                    }
                    if (-not $source) {
                        if ($packageDir) { $failed.Add("$leaf 缺少 NuGet 原件。") }
                        elseif ($leaf -notlike "HuaGuang.*") { if (-not $unsignedUpstream.Contains($leaf)) { $unsignedUpstream.Add($leaf) } }
                        continue
                    }

                    $sourceSignature = Get-AuthenticodeSignature -FilePath $source
                    if ($sourceSignature.Status -ne "Valid") {
                        if (-not $unsignedUpstream.Contains($leaf)) { $unsignedUpstream.Add($leaf) }
                        continue
                    }

                    try {
                        Copy-Item -LiteralPath $source -Destination $installed -Force
                    }
                    catch {
                        $failed.Add("$leaf 无法覆盖（程序可能仍占用该文件）：$($_.Exception.Message)")
                        continue
                    }

                    $copied = Get-AuthenticodeSignature -FilePath $installed
                    if ($copied.Status -ne "Valid") {
                        $failed.Add("$leaf 覆盖后签名仍是 $($copied.Status)")
                        continue
                    }

                    if (-not $restored.Contains($leaf)) { $restored.Add($leaf) }
                }
            }
        }
    }
}

Write-Host ("Restored {0} signed assembly(ies)." -f $restored.Count) -ForegroundColor DarkGray
foreach ($name in ($restored | Sort-Object)) {
    Write-Host "  signed  $name"
}
if ($unsignedUpstream.Count -gt 0) {
    Write-Host "Upstream package is unsigned (cannot restore a signature):" -ForegroundColor Yellow
    foreach ($name in ($unsignedUpstream | Sort-Object)) {
        Write-Host "  unsigned  $name"
    }
}

if ($failed.Count -gt 0) {
    throw ($failed -join [Environment]::NewLine)
}
