using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.Configuration.System;
using Ryujinx.Ava.Systems.Configuration.UI;
using Ryujinx.Common.Configuration;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ryujinx.Ava.Remaster;

internal static class RemasterProfile
{
    public static string GameRoot => Path.Combine(AppDataManager.BaseDirPath, "sdcard", "switch", "sixguns");
    public static string GamePath => Path.Combine(GameRoot, "sixguns.nro");
    public static string BundledNro => OperatingSystem.IsMacOS() && AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).EndsWith("MacOS", StringComparison.Ordinal)
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "sixguns.nro"))
        : Path.Combine(AppContext.BaseDirectory, "sixguns.nro");

    public static void Apply()
    {
        var config = ConfigurationState.Instance;
        config.System.EnableDockedMode.Value = false;
        config.System.MemoryManagerMode.Value = MemoryManagerMode.HostMappedUnsafe;
        config.System.UseHypervisor.Value = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        config.System.EnableInternetAccess.Value = true; // The port uses native sockets for LAN play.
        config.System.UseInputGlobalConfig.Value = false;
        config.Graphics.GraphicsBackend.Value = GraphicsBackend.Vulkan;
        config.Graphics.ResScale.Value = 1;
        config.Graphics.VSyncMode.Value = VSyncMode.Switch;
        config.Graphics.EnableCustomVSyncInterval.Value = false;
        config.Debug.EnableGdbStub.Value = false;
        config.Debug.DebuggerSuspendOnStart.Value = false;
        config.EnableDiscordIntegration.Value = false;
        config.UpdateCheckerType.Value = UpdaterType.Off;
        config.UI.BaseStyle.Value = "Dark";
        config.UI.ShowConsole.Value = false;
        config.Hid.Hotkeys.Value.ToggleVSyncMode = Ryujinx.Common.Configuration.Hid.Key.Unbound;
        config.Hid.Hotkeys.Value.ShowUI = Ryujinx.Common.Configuration.Hid.Key.Unbound;
        config.Logger.EnableFileLog.Value = true;
        config.Logger.EnableInfo.Value = false;
        config.Logger.EnableDebug.Value = false;
        config.Logger.EnableStub.Value = false;
        config.Logger.EnableGuest.Value = false;
        config.Logger.EnableTrace.Value = false;
        config.Logger.EnableError.Value = true;
    }
}
