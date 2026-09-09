param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Path)) {
    throw "File not found: $Path"
}

$bytes = [System.IO.File]::ReadAllBytes($Path)
$peOffset = [BitConverter]::ToInt32($bytes, 0x3C)
$optionalHeaderOffset = $peOffset + 24
$magic = [BitConverter]::ToUInt16($bytes, $optionalHeaderOffset)
if ($magic -ne 0x20B) {
    throw "Only PE32+ images are supported: $Path"
}

$dllCharacteristics = [BitConverter]::ToUInt16($bytes, $optionalHeaderOffset + 0x46)
[PSCustomObject]@{
    Path = (Resolve-Path -LiteralPath $Path).Path
    DllCharacteristics = ('0x{0:X4}' -f $dllCharacteristics)
    DynamicBase = (($dllCharacteristics -band 0x0040) -ne 0)
    GuardCf = (($dllCharacteristics -band 0x4000) -ne 0)
}
