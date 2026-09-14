function Invoke-DotNet {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
        [string[]]$ArgumentList
    )

    $dotnet = (Get-Command dotnet -ErrorAction Stop).Source
    Invoke-NativeCommand -FilePath $dotnet -ArgumentList $ArgumentList -Label "dotnet"
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
    $startParams = @{
        FilePath         = $FilePath
        ArgumentList     = $ArgumentList
        Wait             = $true
        PassThru         = $true
        NoNewWindow      = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $startParams.WorkingDirectory = $WorkingDirectory
    }

    $process = Start-Process @startParams
    if ($process.ExitCode -ne 0) {
        $argsText = if ($ArgumentList.Count -gt 0) { " $($ArgumentList -join ' ')" } else { "" }
        throw "$Label 失败，退出码 $($process.ExitCode)。命令: $FilePath$argsText"
    }
}
