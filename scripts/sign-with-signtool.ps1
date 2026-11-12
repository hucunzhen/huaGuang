# 用 Windows SDK 的 signtool 给未签名的 exe/dll 和安装包做 Authenticode 签名。
# 证书来自当前用户或本机的“个人”存储，必须带私钥，且增强型密钥用法是代码签名。
# 指定证书：-Thumbprint，或环境变量 HUAGUANG_SIGN_THUMBPRINT。
# 没有代码签名证书时，-SkipIfMissing 会跳过签名并继续打包。
param(
    [string]$PublishDir,
    [string]$File,
    [string]$Thumbprint,
    [string]$TimestampUrl,
    [string]$Iscc,
    [string]$Iss,
    [string]$Version,
    [string]$Revision,
    [string]$SetupPath,
    [switch]$SkipIfMissing
)

$ErrorActionPreference = "Stop"

function Get-SignToolPath {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path -LiteralPath $kitsRoot)) {
        throw "未找到 Windows SDK：$kitsRoot"
    }

    $tool = Get-ChildItem -LiteralPath $kitsRoot -Recurse -Filter "signtool.exe" -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\signtool.exe$' } |
        Sort-Object { try { [version]$_.Directory.Parent.Name } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1
    if (-not $tool) {
        throw "Windows SDK 里没有 x64\signtool.exe。"
    }

    return $tool.FullName
}

function Get-CodeSigningCertificate {
    param([string]$WantedThumbprint)

    $codeSigningOid = "1.3.6.1.5.5.7.3.3"
    $found = @(foreach ($store in @(
            @{ Path = "Cert:\CurrentUser\My"; Machine = $false },
            @{ Path = "Cert:\LocalMachine\My"; Machine = $true }
        )) {
        Get-ChildItem -LiteralPath $store.Path -ErrorAction SilentlyContinue |
            Where-Object {
                $_.HasPrivateKey -and @($_.EnhancedKeyUsageList | ForEach-Object { $_.ObjectId }) -contains $codeSigningOid
            } |
            ForEach-Object {
                [pscustomobject]@{ Cert = $_; Machine = $store.Machine }
            }
    })

    if ($WantedThumbprint) {
        $normalized = $WantedThumbprint.Replace(" ", "").ToUpperInvariant()
        $match = @($found | Where-Object { $_.Cert.Thumbprint.ToUpperInvariant() -eq $normalized })
        if ($match.Count -eq 0) {
            throw "证书存储里找不到带私钥的代码签名证书：$WantedThumbprint。服务器身份验证证书不能用来签安装包。"
        }
        return $match[0]
    }

    if ($found.Count -gt 1) {
        $list = ($found | ForEach-Object { $_.Cert.Thumbprint + " " + $_.Cert.Subject }) -join [Environment]::NewLine
        throw "找到多张代码签名证书，请设置 HUAGUANG_SIGN_THUMBPRINT：`n$list"
    }

    if ($found.Count -eq 1) { return $found[0] }
    return $null
}

function Get-UnsignedBinaries {
    param([string[]]$Paths)

    $files = foreach ($path in $Paths) {
        if (-not $path) { continue }
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            Get-Item -LiteralPath $path
            continue
        }
        if (Test-Path -LiteralPath $path -PathType Container) {
            Get-ChildItem -LiteralPath $path -Recurse -File -Include *.exe, *.dll
        }
    }

    foreach ($item in ($files | Sort-Object -Property FullName -Unique)) {
        if ($item.Extension -notin @(".exe", ".dll")) { continue }
        $signature = Get-AuthenticodeSignature -FilePath $item.FullName
        if ($signature.Status -eq "Valid") { continue }
        $item.FullName
    }
}

function Invoke-SignTool {
    param(
        [string]$SignTool,
        $Certificate,
        [string]$Timestamp,
        [string[]]$Files
    )

    $base = @("sign", "/fd", "SHA256", "/td", "SHA256", "/tr", $Timestamp, "/sha1", $Certificate.Cert.Thumbprint)
    if ($Certificate.Machine) { $base = @("sign", "/sm") + $base[1..($base.Length - 1)] }

    for ($offset = 0; $offset -lt $Files.Count; $offset += 16) {
        $end = [Math]::Min($offset + 15, $Files.Count - 1)
        $batch = @($Files[$offset..$end])
        & $SignTool @base @batch
        if ($LASTEXITCODE -ne 0) {
            throw "signtool 签名失败，退出码 $LASTEXITCODE。"
        }
    }
}

try {
    if (-not $Thumbprint) { $Thumbprint = $env:HUAGUANG_SIGN_THUMBPRINT }
    if (-not $TimestampUrl) {
        $TimestampUrl = if ($env:HUAGUANG_TIMESTAMP_URL) { $env:HUAGUANG_TIMESTAMP_URL } else { "http://timestamp.digicert.com" }
    }

    $certificate = Get-CodeSigningCertificate -WantedThumbprint $Thumbprint
    if (-not $certificate) {
        $message = "本机没有代码签名证书，signtool 无法签名。请先申请 OV 代码签名证书并插入 U 盾，或设置 HUAGUANG_SIGN_THUMBPRINT。localhost 那张证书只能做服务器身份验证，不能用来签名。"
        if ($SkipIfMissing) {
            Write-Host $message -ForegroundColor Yellow
            $certificate = $null
        }
        else {
            throw $message
        }
    }

    $signTool = $null
    if ($certificate) {
        $signTool = Get-SignToolPath
        Write-Host "signtool: $signTool" -ForegroundColor DarkGray
        Write-Host "证书: $($certificate.Cert.Thumbprint) $($certificate.Cert.Subject)" -ForegroundColor DarkGray
    }

    $pending = @(Get-UnsignedBinaries -Paths @($PublishDir, $File) | Where-Object { $_ })
    if ($certificate -and $pending.Count -gt 0) {
        Write-Host "签名 $($pending.Count) 个未签名文件..." -ForegroundColor Cyan
        Invoke-SignTool -SignTool $signTool -Certificate $certificate -Timestamp $TimestampUrl -Files $pending
    }

    if ($Iscc) {
        if (-not $Iss) { throw "编译安装包需要 -Iss。" }
        $isccArgs = @(
            ('/DPublishDir="{0}"' -f $PublishDir.Trim().TrimEnd('\', '/')),
            "/DMyAppVersion=$Version",
            "/DMyAppRevision=$Revision"
        )
        if ($certificate) {
            $quotedTool = '$q' + $signTool + '$q'
            $signCommand = "$quotedTool sign /fd SHA256 /td SHA256 /tr $TimestampUrl /sha1 $($certificate.Cert.Thumbprint) `$f"
            if ($certificate.Machine) {
                $signCommand = "$quotedTool sign /sm /fd SHA256 /td SHA256 /tr $TimestampUrl /sha1 $($certificate.Cert.Thumbprint) `$f"
            }
            $isccArgs += "/DSignToolConfigured=1"
            $isccArgs += ('/Shuaguang=' + $signCommand)
        }
        $isccArgs += $Iss
        & $Iscc @isccArgs
        if ($LASTEXITCODE -ne 0) { throw "ISCC 失败，退出码 $LASTEXITCODE。" }
    }

    if ($certificate -and $SetupPath -and (Test-Path -LiteralPath $SetupPath)) {
        $setupSignature = Get-AuthenticodeSignature -FilePath $SetupPath
        if ($setupSignature.Status -ne "Valid") {
            Write-Host "签名安装包: $SetupPath" -ForegroundColor Cyan
            Invoke-SignTool -SignTool $signTool -Certificate $certificate -Timestamp $TimestampUrl -Files @($SetupPath)
        }
    }
    elseif ($SetupPath -and -not $certificate) {
        Write-Host "安装包未签名: $SetupPath" -ForegroundColor Yellow
    }

    exit 0
}
catch {
    Write-Host "[FAILED] $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
