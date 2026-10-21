function Invoke-DotNet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
        [string[]]$ArgumentList
    )

    # Persistent MSBuild/VBCSCompiler nodes inherit the console and never exit.
    # Combined with Start-Transcript they can deadlock "dotnet publish".
    $previousNodeReuse = $env:MSBUILDDISABLENODEREUSE
    $env:MSBUILDDISABLENODEREUSE = "1"
    try {
        $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
        Invoke-NativeCommand -FilePath $dotnet -ArgumentList $ArgumentList -Label "dotnet"
    }
    finally {
        if ($null -eq $previousNodeReuse) {
            Remove-Item Env:MSBUILDDISABLENODEREUSE -ErrorAction SilentlyContinue
        }
        else {
            $env:MSBUILDDISABLENODEREUSE = $previousNodeReuse
        }
    }
}

function Invoke-NativeCommand {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$Label = "",
        [string]$WorkingDirectory = ""
    )

    if (-not $Label) { $Label = [System.IO.Path]::GetFileName($FilePath) }

    # Do not use Start-Process -Wait -NoNewWindow here. publish-windows.ps1 wraps
    # this in Start-Transcript; Start-Process then deadlocks: MSBuild fills the
    # console buffer while PowerShell is blocked in WaitForExit, so copy stops
    # after a few DLLs and the build looks hung.
    $previousLocation = Get-Location
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            Set-Location -LiteralPath $WorkingDirectory
        }

        & $FilePath @ArgumentList
        $exitCode = $LASTEXITCODE
    }
    finally {
        Set-Location -LiteralPath $previousLocation.Path
    }

    if ($exitCode -ne 0) {
        $argsText = if ($ArgumentList.Count -gt 0) { " $($ArgumentList -join ' ')" } else { "" }
        throw "$Label 失败，退出码 $exitCode。命令: $FilePath$argsText"
    }
}
