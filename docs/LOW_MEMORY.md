# Six Guns: constrained host memory

Resource policies introduced in desktop launcher 0.1.3 preview.
The previous 0.1.2 release remains available for comparison/rollback.

## Changes

- The Six Guns launcher uses the normal 4 GiB emulated console configuration even
  when an imported emulator configuration selected expanded 6/8/12 GiB DRAM.
- At up to 6 GiB of runtime-reported host memory, sixguns.nro gets a 2 GiB process
  memory resource limit before its main thread starts. libnx then sizes its heap
  from the smaller reported capacity. SetHeapSize allocation checks use that same
  limit; no successful allocation is reported for memory that was not mapped.
- This is scoped to the Six Guns NRO in this dedicated launcher. Other executable
  names and hosts above 6 GiB retain the original application memory budget.
- Up to 8 GiB hosts use a 256 MiB texture-cache retention target. GPU backends
  reporting unified memory use at most 512 MiB on larger hosts. Live textures and
  the cache's minimum-entry/flush rules remain in force: these are retention
  targets, not hard caps on all GPU allocations.
- Original game graphics settings, native 720p and controller bindings are retained.
  Vulkan/OpenGL startup fallback remains available as before.

## Validation (Windows, 2026-11-19)

14 resource policy checks, 18 diagnostic checks and 32 installer/mod checks pass.
Self-contained win-x64 publish succeeds. Real AMD Radeon integrated graphics
(device 0x1002_0x164E) reached a saved 3D world at 1280x720 and exited through the
replay with no recorded crash/GL errors. Logs confirm 2048 MiB application budget,
a 2022 MiB libnx heap (previously 3260 MiB), and the 256 MiB texture retention target.

The test used DOTNET_GCHeapHardLimit=180000000 (hexadecimal 6 GiB) to exercise the
runtime-reported capacity policy on a 32 GiB host. That restricts managed GC heap
capacity; it does NOT turn the machine into a physical 6 GiB system or restrict all
native/driver allocations. Actual Ryzen 5500U/6 GiB compatibility remains unverified.

Same-scene observations, not a controlled benchmark:

| Process measurement | Cache-only test | Heap + cache patch |
| --- | ---: | ---: |
| Peak working set | 5765.5 MiB | 4499.3 MiB |
| Peak private committed bytes | 3601.6 MiB | 3537.3 MiB |

Shared/mirrored mappings affect working-set accounting. These are process figures,
not total system RAM including Windows and GPU reservations. No FPS uplift claim.
The scene used about 656 MiB of engine allocations (800 MiB allocator arena); large
mods, long sessions, map transitions and other missions need separate testing.
macOS runtime verification has not been performed for this patch.

## Other optimization findings

- Discord RPC is already disabled, but Program still initializes the Discord/play
  report analysis module. Skipping that initialization is a small startup cleanup
  candidate; it is not expected to save gigabytes and has not been patched here.
- Game-library discovery and the generic emulator updater are already disabled in
  the dedicated launcher. Removing their UI/code would mostly simplify the build.
- NRO loading explicitly disables the disk CPU/shader caches. An eight-worker
  shader-cache-loading adjustment would not help this launch path and was discarded.
  A correctly keyed homebrew cache (including executable/engine/driver identity)
  could improve repeated-launch compilation time, but needs a separate correctness
  investigation because this port loads and patches engine code dynamically.
- Filesystem, input, graphics, audio, clock and BSD socket services are required;
  BSD is used by the port's native LAN networking. LDN/NFC/other unused service
  groups could be traced for lazy initialization, but idle service/thread removal
  is a smaller target than the guest heap and GPU resource handling. None stripped.
- Shipping fewer decoder/backend libraries can reduce download size. Libraries
  that are never loaded do not consume their file size in resident RAM. Do not
  remove present/texture-conversion dependencies merely because video is unused.
- Further GPU work should measure upload/copy/synchronization costs in representative
  gameplay before changing renderer correctness or draw submission rules.

Useful affected-user evidence: failure stage (before launcher / after Play / after
world entry), SixGuns-log.txt, and whether Windows reports 6 GiB installed or usable
RAM after the integrated GPU's reserved allocation. RAM pressure is a plausible
cause here, not a confirmed diagnosis of that laptop without its logs.
