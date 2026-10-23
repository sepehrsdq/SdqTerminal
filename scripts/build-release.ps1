$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$publishDirectory = Join-Path $projectRoot "artifacts\publish\win-x64"
$releaseDirectory = Join-Path $projectRoot "artifacts\release"
$version = & (Join-Path $PSScriptRoot "get-version.ps1")

dotnet publish (Join-Path $projectRoot "src\SdqTerminal\SdqTerminal.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -o $publishDirectory
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)." }

$isccPath = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $isccPath) {
    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    $isccPath = $candidates | Select-Object -First 1
}

if (-not $isccPath) {
    Write-Host ""
    Write-Host "Publish completed: $publishDirectory"
    Write-Host "Install Inno Setup and run this script again to create Setup.exe."
    exit 0
}

& $isccPath "/DAppVersion=$version" (Join-Path $projectRoot "installer\SdqTerminal.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer build failed (exit $LASTEXITCODE)." }
Write-Host ""
Write-Host "Installer created in: $releaseDirectory"
