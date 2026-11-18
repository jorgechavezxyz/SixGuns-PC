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
# The game is installed by the Six Guns NX SD Builder (command-line build), the same tool and sixguns.nro a Switch
# uses. Built from the SixGuns-NX checkout next to this workspace (D:\publish), which must be at the commit pinned in
# distribution/sixguns-nx.ref (CI builds exactly that commit).
$sixgunsNx = if ($env:SIXGUNS_NX_SOURCE) { $env:SIXGUNS_NX_SOURCE } else { Join-Path (Split-Path $workspace -Parent) 'publish' }
$pinned = (Get-Content (Join-Path $projectRoot 'distribution\sixguns-nx.ref') -TotalCount 1).Trim()
$actual = (& git -c safe.directory=* -C $sixgunsNx rev-parse HEAD).Trim()
if ($actual -ne $pinned) { throw "SixGuns-NX at $sixgunsNx is at $actual, the launcher pins $pinned (distribution/sixguns-nx.ref)." }
$dirty = & git -c safe.directory=* -C $sixgunsNx status --porcelain -- tools port/source
if ($dirty) { throw "SixGuns-NX at $sixgunsNx has uncommitted changes in tools/ or port/source; commit them and update distribution/sixguns-nx.ref." }
$python = Join-Path $workspace 'toolchain\builder-python\Scripts\python.exe'
if (!(Test-Path $python)) { throw 'Python with PyInstaller and numpy is missing in ../toolchain/builder-python.' }
$builderWork = Join-Path $workspace 'cache\sd-builder-work'
$builderDist = Join-Path $workspace 'cache\sd-builder-dist'
Push-Location $sixgunsNx
try {
    & $python -m PyInstaller --noconfirm --workpath $builderWork --distpath $builderDist tools/packaging/sd_builder_cli.spec
    if ($LASTEXITCODE -ne 0) { throw 'SD Builder packaging failed.' }
} finally { Pop-Location }
$builder = Join-Path $projectRoot 'sd-builder'
if (Test-Path $builder) { Remove-Item -Recurse -Force $builder }
Copy-Item -Recurse (Join-Path $builderDist 'sixguns-sd-build') $builder
Copy-Item (Join-Path $projectRoot 'distribution\sixguns-nx.ref') $builder
& (Join-Path $builder 'sixguns-sd-build.exe') --self-test
if ($LASTEXITCODE -ne 0) { throw 'SD Builder self-test failed.' }
Push-Location $projectRoot
try {
    if ($Package) {
        $output = Join-Path $workspace 'dist\Six Guns Remaster-NX Windows'
        if (Test-Path $output) { throw 'Output folder already exists. Move it aside before creating a clean release package.' }
        & $sdk publish src/Ryujinx -c Release -r win-x64 -p:RuntimeIdentifiers=win-x64 -m:1 --self-contained true --disable-build-servers -o $output
        if ($LASTEXITCODE -ne 0) { throw 'Windows publish failed.' }
        Move-Item -LiteralPath (Join-Path $output 'Ryujinx.exe') -Destination (Join-Path $output 'Six Guns Remaster-NX.exe')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'REMASTER.md') -Destination (Join-Path $output 'START HERE.md')
        # Files at the zip's top level, like the CI release package (the launcher's updater expects that layout).
        Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $workspace 'dist\SixGuns-Remaster-NX-win-x64.zip')
    } else {
        & $sdk build src/Ryujinx -c Release -r win-x64 -p:RuntimeIdentifiers=win-x64 -m:1 --disable-build-servers
        if ($LASTEXITCODE -ne 0) { throw 'Windows build failed.' }
    }
} finally { Pop-Location }
