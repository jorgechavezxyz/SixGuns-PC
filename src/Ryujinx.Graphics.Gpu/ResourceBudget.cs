using System;

namespace Ryujinx.Graphics.Gpu
{
    /// <summary>Host-side cache limits. These are retention targets, not a limit on live game resources.</summary>
    public static class ResourceBudget
    {
        private const ulong GiB = 1024UL * 1024 * 1024;
        private const ulong MiB = 1024UL * 1024;

        public static bool ConserveMemory(ulong availableMemory) => availableMemory > 0 && availableMemory <= 8 * GiB;

        public static ulong HomebrewMemoryBytes(ulong availableMemory) =>
            availableMemory > 0 && availableMemory <= 6 * GiB ? 2 * GiB : 0;

        public static ulong TextureCacheBytes(ulong cpuMemory, ulong gpuMemory, bool unifiedMemory, bool conserveMemory)
        {
            // Preserve the upstream dedicated-GPU policy, without unsigned underflow on tiny/unknown hosts.
            ulong maximum = gpuMemory == 0 || (cpuMemory / GiB < 6 && gpuMemory / GiB < 6)
                ? GiB
                : cpuMemory / GiB < 12
                    ? (cpuMemory > 2 * GiB ? cpuMemory - 2 * GiB : GiB)
                    : cpuMemory;
            ulong budget = Math.Clamp(gpuMemory / 2, 512 * MiB, Math.Max(512 * MiB, maximum));
            // Unified graphics shares the OS/game's RAM; reported GPU heap size is not dedicated VRAM.
            if (unifiedMemory) budget = Math.Min(budget, 512 * MiB);
            if (conserveMemory) budget = Math.Min(budget, 256 * MiB);
            return budget;
        }
    }
}
