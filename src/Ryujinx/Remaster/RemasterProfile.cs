using Ryujinx.Ava.Systems.Configuration;
using Ryujinx.Ava.Systems.Configuration.System;
using Ryujinx.Ava.Systems.Configuration.UI;
using Ryujinx.Common.Configuration;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Common.Configuration.Hid.Keyboard;
using Ryujinx.Input;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Ryujinx.Ava.Remaster;

internal static class RemasterProfile
{
    public static string GameRoot => Path.Combine(AppDataManager.BaseDirPath, "sdcard", "switch", "sixguns");
    public static string GamePath => Path.Combine(GameRoot, "sixguns.nro");
    /// <summary>The newest SixGuns-NX release's port, downloaded by PortUpdater.</summary>
    public static string PortCache => Path.Combine(AppDataManager.BaseDirPath, "remaster-port");
    public static string BundledNro => OperatingSystem.IsMacOS() && AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).EndsWith("MacOS", StringComparison.Ordinal)
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "sixguns.nro"))
        : Path.Combine(AppContext.BaseDirectory, "sixguns.nro");
    /// <summary>The Six Guns NX SD Builder (command-line build) that installs the game; see GameInstaller.</summary>
    public static string SdBuilder => Path.Combine(
        OperatingSystem.IsMacOS() && AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar).EndsWith("MacOS", StringComparison.Ordinal)
            ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "sd-builder")) // signed Mac bundle
            : Path.Combine(AppContext.BaseDirectory, "sd-builder"),
        OperatingSystem.IsWindows() ? "sixguns-sd-build.exe" : "sixguns-sd-build");

    public static void Apply()
    {
        var config = ConfigurationState.Instance;
        foreach (var input in config.Hid.InputConfig.Value)
        {
            if (input is not StandardKeyboardInputConfig keyboard ||
                keyboard.PlayerIndex != PlayerIndex.Player1 ||
                keyboard.LeftJoycon is not { ButtonMinus: PhysicalKey.Minus, ButtonL: PhysicalKey.E } ||
                keyboard.RightJoycon is not
                {
                    ButtonA: PhysicalKey.Z, ButtonB: PhysicalKey.X, ButtonX: PhysicalKey.C,
                    ButtonY: PhysicalKey.V, ButtonPlus: PhysicalKey.Plus, ButtonR: PhysicalKey.U, ButtonZr: PhysicalKey.O,
                })
            {
                continue;
            }

            var defaults = InputConfigDefaults.CreateDefaultKeyboardConfiguration(
                keyboard.Id, keyboard.Name, keyboard.ControllerType, keyboard.PlayerIndex);
            keyboard.LeftJoycon.ButtonMinus = defaults.LeftJoycon.ButtonMinus;
            keyboard.LeftJoycon.ButtonL = defaults.LeftJoycon.ButtonL;
            keyboard.RightJoycon.ButtonA = defaults.RightJoycon.ButtonA;
            keyboard.RightJoycon.ButtonB = defaults.RightJoycon.ButtonB;
            keyboard.RightJoycon.ButtonX = defaults.RightJoycon.ButtonX;
            keyboard.RightJoycon.ButtonY = defaults.RightJoycon.ButtonY;
            keyboard.RightJoycon.ButtonPlus = defaults.RightJoycon.ButtonPlus;
            keyboard.RightJoycon.ButtonR = defaults.RightJoycon.ButtonR;
            keyboard.RightJoycon.ButtonZr = defaults.RightJoycon.ButtonZr;
        }

        config.System.EnableDockedMode.Value = false;
        config.System.MemoryManagerMode.Value = MemoryManagerMode.HostMappedUnsafe;
        config.System.UseHypervisor.Value = OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
        config.System.EnableInternetAccess.Value = true; // The port uses native sockets for LAN play.
        config.System.UseInputGlobalConfig.Value = false;
        config.Graphics.GraphicsBackend.Value = GraphicsChoice.Backend(); // Vulkan, or OpenGL where it fails
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
