$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) 'src/SdqTerminal/SdqTerminal.csproj') -Raw
$version = [string]($project.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw 'Set Version in src/SdqTerminal/SdqTerminal.csproj to a three-part release version (major.minor.patch).'
}
$version
