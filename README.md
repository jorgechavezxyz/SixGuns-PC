# Six Guns Remaster-NX

A dedicated desktop launcher for the Six Guns Switch homebrew port, built on Ryubing/Ryujinx.

## Play

Windows: extract the entire release ZIP into a folder, then open **Six Guns Remaster-NX.exe**. Keep the accompanying files together. You do not need to install .NET or Python.

macOS: extract the ZIP for your Mac (arm64 for Apple Silicon; x64 for Intel), then copy **Six Guns Remaster-NX.app** to Applications. Developer builds are ad-hoc signed, not notarized.

The first launch guides you through three steps:

1. Choose **prod.keys** dumped from your own Switch.
2. Choose matching **Switch firmware**, as a ZIP or folder.
3. Choose the original **Six Guns 2.9.9a Android APK**, and the folder containing both original OBB files. Subfolders are searched automatically. Allow about 2 GB of free disk space during installation.

The installer verifies the entire APK against the previously signature-verified official release, then verifies both OBBs and the arm64 engine. Re-signed APKs, installer repacks and modified files are rejected. This version accepts plain APK files, not XAPK bundles.

Then choose **Play**. Use **Controls** to choose a connected controller or change keyboard bindings. Default keyboard: WASD move; IJKL camera; arrows navigate; Z confirm; X back; Q/O triggers; minus/plus game menu buttons. F11 toggles full screen. Escape leaves full screen first, then offers to stop the game. Returning from the game returns to the launcher.

For LAN play, use the game's Multiplayer menu. Each player needs the same port and map mods. Stadium generation is not included in this launcher version; existing user-created mods can be placed in the game's mods folder.

## Your data

The launcher uses a separate **SixGunsRemasterNX** application-data folder. It does not use the normal Ryujinx profile or shared `.switch` keys. Game files and saves are under `sdcard/switch/sixguns/` inside that folder. **Set up files again** replaces managed game files while preserving saves, preferences, identity and mods.

For a portable installation, create an empty **remaster-data** folder beside the executable (or beside the `.app` on macOS) before the first launch. Keep it when updating. Safely eject portable storage only after closing the launcher.

No keys, firmware, APKs, OBBs or game assets are included. This is an unofficial fan project. Six Guns belongs to Gameloft. The MIT upstream license and third-party notices accompany the application.

## Build from the SSD

All paths are relative to this repository or its parent, `remaster-nx`. Build products, package caches and temporary files remain there.

- Windows: install the .NET SDK version from `global.json` into `../toolchain/dotnet`, then run `./build-remaster.ps1`. Use `-Package` for a self-contained release ZIP.
- macOS: use the SDK from `global.json` and run `bash build-remaster-macos.sh arm64` (or `x64`). Set `DOTNET_ROOT` if needed. The existing Mac SDK may live outside the SSD; project outputs and caches stay on it. Building and signing the Mac app requires macOS.
- The bundled port is `src/Ryujinx/Assets/Remaster/sixguns.nro`. Update it from the verified clean-source build when updating the port.
- Validation status and remaining platform checks are recorded in the SSD's `remaster-nx/PLAN.md` and logs folder.

Do not package the portable data directory, development test profiles, keys, firmware, game files or private research. Never push or publish without the owner's review.

The original Switch loader and SD Builder are at https://github.com/jorgechavezxyz/SixGuns-NX. The port loads the original Android arm64 engine, replaces its Android platform functions, and adds controller navigation and adapted game menus. This launcher runs that same port with Ryubing. Original launcher changes are MIT-licensed (LICENSE-REMASTER.txt); the upstream license is preserved in LICENSE.txt.

