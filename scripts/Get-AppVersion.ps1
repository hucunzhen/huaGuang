param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

function Read-VersionFromMsBuild {
    param([string]$Path)
    $output = & dotnet msbuild $Path `
        -getProperty:ApplicationDisplayVersion `
        -getProperty:ApplicationVersion `
        -nologo 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $output) {
        return $null
    }

    try {
        $json = $output | ConvertFrom-Json
        $version = [string]$json.Properties.ApplicationDisplayVersion
        $revision = [string]$json.Properties.ApplicationVersion
        if ([string]::IsNullOrWhiteSpace($version) -or [string]::IsNullOrWhiteSpace($revision)) {
            return $null
        }

        return [pscustomobject]@{
            Version = $version.Trim()
            Revision = $revision.Trim()
        }
    }
    catch {
        return $null
    }
}

function Read-VersionFromProjectFile {
    param([string]$Path)
    $text = Get-Content -LiteralPath $Path -Raw -Encoding UTF8
    $versionMatch = [regex]::Match($text, '<ApplicationDisplayVersion>([^<]+)</ApplicationDisplayVersion>')
    $revisionMatch = [regex]::Match($text, '<ApplicationVersion>([^<]+)</ApplicationVersion>')
    if (-not $versionMatch.Success -or -not $revisionMatch.Success) {
        return $null
    }

    return [pscustomobject]@{
        Version = $versionMatch.Groups[1].Value.Trim()
        Revision = $revisionMatch.Groups[1].Value.Trim()
    }
}

$resolved = Read-VersionFromMsBuild -Path $ProjectPath
if (-not $resolved) {
    $resolved = Read-VersionFromProjectFile -Path $ProjectPath
}

if (-not $resolved) {
    throw "Unable to read ApplicationDisplayVersion/ApplicationVersion from $ProjectPath"
}

$version = $resolved.Version
$revision = $resolved.Revision
$label = [string]::Concat($version, [char]0xFF08, [char]0x4FEE, [char]0x8BA2, ' ', $revision, [char]0xFF09)
$fileSuffix = [string]::Concat($version, '-r', $revision)
$innoVersion = [string]::Concat($version, '.', $revision)

[pscustomobject]@{
    Version     = $version
    Revision    = $revision
    Label       = $label
    FileSuffix  = $fileSuffix
    InnoVersion = $innoVersion
}
