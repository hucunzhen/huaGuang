param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath
)

[xml]$csproj = Get-Content -LiteralPath $ProjectPath -Encoding UTF8
$groups = @($csproj.Project.PropertyGroup)
$version = "1.0.0"
$revision = "1"

foreach ($group in $groups) {
    if ($group.ApplicationDisplayVersion) {
        $version = [string]$group.ApplicationDisplayVersion
    }
    if ($group.ApplicationVersion) {
        $revision = [string]$group.ApplicationVersion
    }
}

[pscustomobject]@{
    Version    = $version
    Revision   = $revision
    Label      = "$version（修订 $revision）"
    FileSuffix = "$version-r$revision"
    InnoVersion = "$version.$revision"
}
