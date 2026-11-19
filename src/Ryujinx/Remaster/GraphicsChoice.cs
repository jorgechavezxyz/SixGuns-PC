using Ryujinx.Common.Configuration;
using Ryujinx.Graphics.Vulkan;
using System;
using System.IO;
using System.Linq;

namespace Ryujinx.Ava.Remaster;

/// <summary>
/// Vulkan, or OpenGL when Vulkan does not work on this PC (old or broken drivers, mostly laptop graphics).
/// The game uses Vulkan unless:
/// <list type="bullet">
/// <item>Windows/Linux report no Vulkan device at all (checked before every start);</item>
/// <item>a start with Vulkan failed: it threw while loading (the launcher retries at once with OpenGL), or the
/// launcher closed within the first <see cref="ConfirmAfter"/> of a Vulkan run (a driver crash takes the whole
/// process with it, so this is noticed at the next start: DebugReport's run marker says "vulkan" without "ok").
/// The choice is then kept in <c>remaster-graphics.txt</c> until the player picks Vulkan again.</item>
/// </list>
/// macOS always uses Vulkan (MoltenVK); its OpenGL is too old for the emulator.
/// </summary>
internal static class GraphicsChoice
{
    public static readonly TimeSpan ConfirmAfter = TimeSpan.FromSeconds(45);
    private static string ForcedFile => Path.Combine(AppDataManager.BaseDirPath, "remaster-graphics.txt");

    /// <summary>The player's PC was switched to OpenGL after Vulkan failed (shown with a way back).</summary>
    public static bool ForcedOpenGl
    {
        get
        {
            // first line "opengl"; then when and why (for the debug log)
            try { return !OperatingSystem.IsMacOS() && File.Exists(ForcedFile) && File.ReadLines(ForcedFile).FirstOrDefault()?.Trim() == "opengl"; }
            catch { return false; }
        }
    }

    public static GraphicsBackend Backend()
    {
        if (OperatingSystem.IsMacOS()) return GraphicsBackend.Vulkan;
        if (ForcedOpenGl) return GraphicsBackend.OpenGl;
        try
        {
            return VulkanRenderer.GetPhysicalDevices().Length > 0 ? GraphicsBackend.Vulkan : GraphicsBackend.OpenGl;
        }
        catch (Exception ex)
        {
            RemasterUpdater.Log?.Invoke("Vulkan check failed, using OpenGL: " + ex.Message);
            return GraphicsBackend.OpenGl;
        }
    }

    /// <summary>Switches this PC to OpenGL (false on macOS, where there is no fallback).</summary>
    public static bool UseOpenGl(string why)
    {
        if (OperatingSystem.IsMacOS()) return false;
        try
        {
            File.WriteAllText(ForcedFile, "opengl\n" + DateTime.Now.ToString("O") + "\n" + why + "\n");
            RemasterUpdater.Log?.Invoke("Graphics: switched to OpenGL. " + why);
            return true;
        }
        catch { return false; }
    }

    public static void UseVulkanAgain()
    {
        try { File.Delete(ForcedFile); } catch { }
    }
}
