param(
    [Parameter(Mandatory = $true)]
    [string[]]$ExePath
)

$ErrorActionPreference = "Stop"

foreach ($path in $ExePath) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Warning "Skip missing executable: $path"
        continue
    }

    try {
        Set-ProcessMitigation -Name $path -Disable ShadowStacks -ErrorAction Stop
        Write-Host "Disabled ShadowStacks mitigation for: $path"
    }
    catch {
        Write-Warning "Failed to disable ShadowStacks for ${path}: $($_.Exception.Message)"
    }
}
