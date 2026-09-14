$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$appIconDir = Join-Path $root "src\HuaGuang.Monitor\Resources\AppIcon"
$logoPng = Join-Path $appIconDir "logo.png"
$appIconPng = Join-Path $appIconDir "appicon.png"
$png = $appIconPng
$outputs = @(
    (Join-Path $root "installer\appicon.ico"),
    (Join-Path $appIconDir "appicon.ico")
)

if (-not (Test-Path $logoPng)) {
    throw "Logo not found: $logoPng"
}

if (-not (Test-Path $appIconPng) -or (Get-Item $logoPng).LastWriteTimeUtc -gt (Get-Item $appIconPng).LastWriteTimeUtc) {
    Copy-Item -LiteralPath $logoPng -Destination $appIconPng -Force
    Write-Host "Synced appicon.png from logo.png" -ForegroundColor DarkGray
}

if (-not (Test-Path $png)) {
    throw "App icon not found: $png"
}

Add-Type -AssemblyName System.Drawing

function Convert-PngToIco {
    param(
        [Parameter(Mandatory)][string]$InputPath,
        [string]$OutputPath,
        [int[]]$Sizes = @(256, 48, 32, 16)
    )

    $images = New-Object System.Collections.Generic.List[object]
    foreach ($size in $Sizes) {
        $source = [System.Drawing.Bitmap]::FromFile($InputPath)
        try {
            $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
            $graphics.DrawImage($source, 0, 0, $size, $size)
            $graphics.Dispose()

            $stream = New-Object System.IO.MemoryStream
            $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $bitmap.Dispose()

            $images.Add([PSCustomObject]@{
                Size  = $size
                Bytes = $stream.ToArray()
            })
        }
        finally {
            $source.Dispose()
        }
    }

    $count = $images.Count
    $offset = 6 + (16 * $count)
    $output = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($output)

    $writer.Write([uint16]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]$count)

    foreach ($image in $images) {
        $size = [int]$image.Size
        $width = if ($size -ge 256) { [byte]0 } else { [byte]$size }
        $height = if ($size -ge 256) { [byte]0 } else { [byte]$size }
        $writer.Write($width)
        $writer.Write($height)
        $writer.Write([byte]0)
        $writer.Write([byte]0)
        $writer.Write([uint16]1)
        $writer.Write([uint16]32)
        $writer.Write([uint32]$image.Bytes.Length)
        $writer.Write([uint32]$offset)
        $offset += $image.Bytes.Length
    }

    foreach ($image in $images) {
        $writer.Write($image.Bytes)
    }

    $writer.Flush()
    $bytes = $output.ToArray()
    $writer.Close()
    $output.Close()
    return $bytes
}

function Write-IcoFile {
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$OutputPath,
        [int]$MaxAttempts = 8
    )

    $dir = Split-Path -Parent $OutputPath
    if (-not (Test-Path $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }

    $tempPath = Join-Path $dir (".appicon-" + [Guid]::NewGuid().ToString("N") + ".ico.tmp")
    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        try {
            [System.IO.File]::WriteAllBytes($tempPath, $Bytes)
            if (Test-Path -LiteralPath $OutputPath) {
                Remove-Item -LiteralPath $OutputPath -Force -ErrorAction Stop
            }

            Move-Item -LiteralPath $tempPath -Destination $OutputPath -Force -ErrorAction Stop
            return
        }
        catch {
            if (Test-Path -LiteralPath $tempPath) {
                Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue
            }

            if ($attempt -ge $MaxAttempts) {
                if (Test-Path -LiteralPath $OutputPath) {
                    Write-Host "Warning: cannot overwrite locked icon $OutputPath — keeping existing file. ($($_.Exception.Message))" -ForegroundColor Yellow
                    return
                }

                throw
            }

            Start-Sleep -Milliseconds (250 * $attempt)
        }
    }
}

$pngTime = (Get-Item -LiteralPath $png).LastWriteTimeUtc
$needsBuild = $false
foreach ($ico in $outputs) {
    if (-not (Test-Path -LiteralPath $ico)) {
        $needsBuild = $true
        break
    }

    if ((Get-Item -LiteralPath $ico).LastWriteTimeUtc -lt $pngTime) {
        $needsBuild = $true
        break
    }
}

if (-not $needsBuild) {
    Write-Host "Installer icon up to date, skip regenerate." -ForegroundColor DarkGray
    return
}

Write-Host "Generating ICO from $png" -ForegroundColor DarkGray
$icoBytes = Convert-PngToIco -InputPath $png -OutputPath $outputs[0]
foreach ($ico in $outputs) {
    Write-IcoFile -Bytes $icoBytes -OutputPath $ico
    Write-Host "Generated $ico"
}
