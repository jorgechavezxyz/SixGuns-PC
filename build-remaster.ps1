param([switch]$Package)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$workspace = Split-Path $projectRoot -Parent
$env:DOTNET_ROOT = Join-Path $workspace 'toolchain\dotnet'
$env:DOTNET_CLI_HOME = Join-Path $workspace 'cache\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $workspace 'cache\nuget'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $workspace 'cache\nuget-http'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:TEMP = Join-Path $workspace 'temp'
$env:TMP = $env:TEMP
foreach ($path in @($env:TEMP, $env:NUGET_PACKAGES, $env:DOTNET_CLI_HOME, (Join-Path $workspace 'logs'))) {
    New-Item -ItemType Directory -Force -Path $path | Out-Null
}
$sdk = Join-Path $env:DOTNET_ROOT 'dotnet.exe'
if (!(Test-Path $sdk)) { throw 'Install the .NET 10 SDK in ../toolchain/dotnet first (see REMASTER.md).' }
$nro = Join-Path $projectRoot 'src\Ryujinx\Assets\Remaster\sixguns.nro'
if (!(Test-Path $nro)) { throw 'Missing bundled sixguns.nro. See REMASTER.md.' }
Push-Location $projectRoot
try {
    if ($Package) {
        $output = Join-Path $workspace 'dist\Six Guns Remaster-NX Windows'
        if (Test-Path $output) { throw 'Output folder already exists. Move it aside before creating a clean release package.' }
        & $sdk publish src/Ryujinx -c Release -r win-x64 -p:RuntimeIdentifiers=win-x64 -m:1 --self-contained true --disable-build-servers -o $output
        if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
        Move-Item -LiteralPath (Join-Path $output 'Ryujinx.exe') -Destination (Join-Path $output 'Six Guns Remaster-NX.exe')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'REMASTER.md') -Destination (Join-Path $output 'START HERE.md')
        Compress-Archive -LiteralPath $output -DestinationPath (Join-Path $workspace 'dist\SixGuns-Remaster-NX-win-x64.zip')
    } else {
        & $sdk build src/Ryujinx -c Release -r win-x64 -p:RuntimeIdentifiers=win-x64 -m:1 --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
    }
} finally { Pop-Location }
