#!/usr/bin/env bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")" && pwd)"
workspace="$(dirname "$project_root")"
arch="${1:-arm64}"
case "$arch" in arm64|x64) ;; *) echo 'Use arm64 or x64'; exit 1;; esac
export DOTNET_CLI_HOME="$workspace/cache/dotnet-home-macos"
export NUGET_PACKAGES="$workspace/cache/nuget-macos"
export NUGET_HTTP_CACHE_PATH="$workspace/cache/nuget-http-macos"
export TMPDIR="$workspace/temp/macos/"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false
mkdir -p "$TMPDIR" "$workspace/dist" "$DOTNET_CLI_HOME"
dotnet_bin="${DOTNET_ROOT:-$HOME/.dotnet}/dotnet"
publish="$workspace/dist/macos-$arch-publish"
app="$workspace/dist/macos-$arch/Six Guns Remaster-NX.app"
if [[ -e "$publish" || -e "$app" ]]; then echo 'Move the previous publish/app folders aside first.'; exit 1; fi
cd "$project_root"
# sd-builder/: the Six Guns NX SD Builder (command-line) from the commit pinned in distribution/sixguns-nx.ref,
# built on this Mac (PyInstaller output is per-platform):
#   (cd <SixGuns-NX checkout at that commit> && python3 -m PyInstaller --noconfirm tools/packaging/sd_builder_cli.spec)
#   cp -R <checkout>/dist/sixguns-sd-build sd-builder
[ -x sd-builder/sixguns-sd-build ] || { echo 'sd-builder/ missing: build the SD Builder first (see above)'; exit 1; }
"$dotnet_bin" publish src/Ryujinx -c Release -r "osx-$arch" "-p:RuntimeIdentifiers=osx-$arch" -m:1 --self-contained true --disable-build-servers -o "$publish"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$publish/." "$app/Contents/MacOS/"
mv "$app/Contents/MacOS/sixguns.nro" "$app/Contents/Resources/"
mv "$app/Contents/MacOS/THIRDPARTY.md" "$app/Contents/MacOS/LICENSE.txt" "$app/Contents/MacOS/LICENSE-REMASTER.txt" "$app/Contents/Resources/"
cp distribution/macos/Remaster-Info.plist "$app/Contents/Info.plist"
cp distribution/macos/Ryujinx.icns "$app/Contents/Resources/"
cp LICENSE.txt "$app/Contents/Resources/"
cp REMASTER.md "$app/Contents/Resources/START HERE.md"
mv "$app/Contents/MacOS/Ryujinx" "$app/Contents/MacOS/SixGunsRemasterNX"
chmod +x "$app/Contents/MacOS/SixGunsRemasterNX"
# The SD Builder: copied again with ditto so its Python.framework keeps its symlinked bundle layout (the
# .NET publish copy flattens it, and a flattened framework cannot be signed), then signed: the framework
# as a bundle, every other Mach-O file on its own, before the app itself.
rm -rf "$app/Contents/MacOS/sd-builder"
ditto sd-builder "$app/Contents/MacOS/sd-builder"
chmod +x "$app/Contents/MacOS/sd-builder/sixguns-sd-build"
find "$app/Contents/MacOS" -name '*.dylib' -not -path '*/sd-builder/*' -exec codesign --force --sign - {} \;
find "$app/Contents/MacOS/sd-builder" -name '*.framework' -type d -prune -print0 | while IFS= read -r -d '' fw; do
  codesign --force --sign - "$fw"
done
find "$app/Contents/MacOS/sd-builder" -path '*.framework' -prune -o -type f -print0 | while IFS= read -r -d '' f; do
  if file -b "$f" | grep -q Mach-O; then codesign --force --sign - "$f"; fi
done
codesign --force --sign - --entitlements distribution/macos/entitlements.xml "$app"
codesign --verify --deep --strict "$app"
ditto -c -k --keepParent "$app" "$workspace/dist/SixGuns-Remaster-NX-macos-$arch.zip"
echo "Created $workspace/dist/SixGuns-Remaster-NX-macos-$arch.zip"
