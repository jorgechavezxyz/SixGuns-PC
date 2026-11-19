# Six Guns Remaster-NX

A dedicated desktop launcher for the Six Guns Switch homebrew port, built on Ryubing/Ryujinx.

## Play

Windows: extract the entire release ZIP into a folder, then open **Six Guns Remaster-NX.exe**. Keep the accompanying files together. You do not need to install .NET or Python.

macOS: extract the ZIP for your Mac (arm64 for Apple Silicon; x64 for Intel), then copy **Six Guns Remaster-NX.app** to Applications. Developer builds are ad-hoc signed, not notarized.

The first launch guides you through three steps:

1. Choose **prod.keys** dumped from your own Switch.
2. Choose matching **Switch firmware**, as a ZIP or folder.
3. Choose the original **Six Guns 2.9.9a Android APK**, and the folder containing both original OBB files. Subfolders are searched automatically. Allow about 2 GB of free disk space during installation.

The game is installed by the Six Guns NX SD Builder, the same tool that builds a Switch SD card: it checks Gameloft's signature on the APK (every file), the 2.9.9a arm64 engine and both original OBBs, and refuses re-signed APKs, installer repacks and modified files. XAPK/APKS bundles holding the official APK work too. It also adds the Stadium multiplayer map, so desktop and Switch players have identical builds for LAN matches.

Choose **Play**. Controllers supported by SDL connect automatically to Player 1, including when connected during play. Press a controller button or a keyboard key to switch input. Open **Controls** to customize bindings.

- **WASD** moves, **IJKL** turns the camera, and the **arrow keys** navigate menus.
- **E** interacts or confirms, **Space** rolls or goes back, **R** reloads, and **X** changes weapons.
- **Q** aims, **Z** fires, **F** toggles cover, **G** throws a grenade, **M** opens the map, and **P** pauses.
- Hold **right mouse** to aim, then drag to turn the camera. While aiming, **left mouse** fires. With right mouse released, left-click still acts as a touchscreen tap.
- **F11** toggles full screen. **Escape** leaves full screen first, then offers to stop the game. Returning from the game returns to the launcher.

For LAN play, use the game's Multiplayer menu. Each player needs the same port and map mods. The launcher installs the same port and Stadium map as the Switch SD Builder; your own mods can be placed in the game's mods folder.

## Updates

The launcher checks this project's GitHub releases when it starts and shows **Update** when a newer version is out (or use **Check for updates**). The update is downloaded, checked against the release's SHA256SUMS.txt and installed in place; the launcher then restarts and rebuilds the game from the official files it kept in the game folder (`base.apk`, `obb/`), so you don't choose them again. Saves, settings, keys, firmware, your own mods and a portable `remaster-data` folder are kept. On macOS, keep the app in Applications so it can update itself.

## Problems

If the game won't start or crashes, press **Save log to Desktop** at the bottom of the launcher. It saves **SixGuns-log.txt** to your Desktop with the game's and emulator's newest logs, the launcher and port versions and your installed mods; send that file when you ask for help. Your user and computer names, home folder path, public IP addresses and anything key-like are removed first, and keys, firmware, game files and saves are never included. After a crash the launcher reminds you of the button.

## Your data

The launcher uses a separate **SixGunsRemasterNX** application-data folder. It does not use the normal Ryujinx profile or shared `.switch` keys. Game files and saves are under `sdcard/switch/sixguns/` inside that folder. **Set up files again** replaces managed game files while preserving saves, preferences, identity and mods.

For a portable installation, create an empty **remaster-data** folder beside the executable (or beside the `.app` on macOS) before the first launch. Keep it when updating. Safely eject portable storage only after closing the launcher.

No keys, firmware, APKs, OBBs or game assets are included. This is an unofficial fan project. Six Guns belongs to Gameloft. The MIT upstream license and third-party notices accompany the application.

## Build from the SSD

All paths are relative to this repository or its parent, `remaster-nx`. Build products, package caches and temporary files remain there.

- Windows: install the .NET SDK version from `global.json` into `../toolchain/dotnet`, then run `./build-remaster.ps1`. Use `-Package` for a self-contained release ZIP.
- macOS: use the SDK from `global.json` and run `bash build-remaster-macos.sh arm64` (or `x64`). Set `DOTNET_ROOT` if needed. The existing Mac SDK may live outside the SSD; project outputs and caches stay on it. Building and signing the Mac app requires macOS.
- The port and its installer come from SixGuns-NX, so desktop and Switch players always run the same port: CI bundles the newest SixGuns-NX release (`distribution/sixguns-nx.ref` says `latest-release`; put a commit there only to hold back or roll back a port) — its `tools/packaging/sixguns.nro`, checked against the release's own `sixguns.nro`, and its SD Builder (`tools/packaging/sd_builder_cli.spec`) go into `sd-builder/`. `build-remaster.ps1` builds from the local checkout as it is. At startup the launcher also fetches the newest release's `sixguns.nro` itself (`PortUpdater.cs`: SHA-256 from GitHub and the NRO header checked, only releases newer than the bundled port, cached in `remaster-port/`), so a new Switch release reaches desktop players without a launcher release. Only the executable changes; game data and saves stay, as on a Switch.
- Publishing an update: raise `<Version>` in `src/Ryujinx/Ryujinx.csproj`, then attach the three launcher ZIPs, `SHA256SUMS.txt` and the CI's `update.json` (artifact `update-manifest`) to the new release. Launchers offer a release only when it carries `update.json` and the package for their platform.
- Validation status and remaining platform checks are recorded in the SSD's `remaster-nx/PLAN.md` and logs folder.

Do not package the portable data directory, development test profiles, keys, firmware, game files or private research. Never push or publish without the owner's review.
