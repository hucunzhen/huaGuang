param(
    [Parameter(Mandatory)][string]$SourcePng,
    [Parameter(Mandatory)][string]$ResRoot
)

$ErrorActionPreference = "Stop"

$ResRoot = $ResRoot.Trim().TrimEnd('\', '/')

if (-not (Test-Path $SourcePng)) {
    throw "Source icon not found: $SourcePng"
}

if (-not (Test-Path $ResRoot)) {
    Write-Host "Skip Android icon fix; resizetizer output not found: $ResRoot"
    exit 0
}

Add-Type -AssemblyName System.Drawing

function Save-ScaledIcon {
    param(
        [string]$InputPath,
        [string]$OutputPath,
        [int]$Size
    )

    $source = [System.Drawing.Bitmap]::FromFile($InputPath)
    try {
        $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

        $scale = [Math]::Min($Size / $source.Width, $Size / $source.Height)
        $drawWidth = [int][Math]::Round($source.Width * $scale)
        $drawHeight = [int][Math]::Round($source.Height * $scale)
        $offsetX = [int](($Size - $drawWidth) / 2)
        $offsetY = [int](($Size - $drawHeight) / 2)
        $graphics.DrawImage($source, $offsetX, $offsetY, $drawWidth, $drawHeight)
        $graphics.Dispose()

        $directory = Split-Path -Parent $OutputPath
        if (-not (Test-Path $directory)) {
            New-Item -ItemType Directory -Path $directory -Force | Out-Null
        }

        $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }
    finally {
        $source.Dispose()
    }
}

function Save-TransparentSquare {
    param(
        [string]$OutputPath,
        [int]$Size
    )

    $bitmap = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.Clear([System.Drawing.Color]::Transparent)
    $graphics.Dispose()

    $directory = Split-Path -Parent $OutputPath
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }

    $bitmap.Save($OutputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

$legacySizes = @{
    "mipmap-mdpi"    = 48
    "mipmap-hdpi"    = 72
    "mipmap-xhdpi"   = 96
    "mipmap-xxhdpi"  = 144
    "mipmap-xxxhdpi" = 192
}

$adaptiveSizes = @{
    "mipmap-mdpi"    = 108
    "mipmap-hdpi"    = 162
    "mipmap-xhdpi"   = 216
    "mipmap-xxhdpi"  = 324
    "mipmap-xxxhdpi" = 432
}

foreach ($folder in $legacySizes.Keys) {
    $targetDir = Join-Path $ResRoot $folder
    New-Item -ItemType Directory -Force -Path $targetDir | Out-Null

    $legacySize = $legacySizes[$folder]
    $adaptiveSize = $adaptiveSizes[$folder]

    Save-ScaledIcon -InputPath $SourcePng -OutputPath (Join-Path $targetDir "appicon.png") -Size $legacySize
    Save-ScaledIcon -InputPath $SourcePng -OutputPath (Join-Path $targetDir "appicon_round.png") -Size $legacySize
    Save-ScaledIcon -InputPath $SourcePng -OutputPath (Join-Path $targetDir "appicon_foreground.png") -Size $adaptiveSize
    Save-TransparentSquare -OutputPath (Join-Path $targetDir "appicon_background.png") -Size $adaptiveSize
}

Write-Host "Applied transparent Android launcher icons under $ResRoot"
