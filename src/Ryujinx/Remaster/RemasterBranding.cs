using System.Text.RegularExpressions;

namespace Ryujinx.Ava.Remaster;

/// <summary>
/// The launcher's own name in everything a player sees (dialogs, tooltips, message boxes) instead of the
/// emulator it is built on. The emulator stays credited in LICENSE.txt and THIRDPARTY.md, as its MIT licence
/// requires. Web addresses (ryujinx.app, .../ryujinx) and technical formats ("Ryujinx-1a2b3c4d") are left as
/// they are.
/// </summary>
internal static partial class RemasterBranding
{
    public const string AppName = "Six Guns Remaster-NX";

    [GeneratedRegex(@"(?<![\w./-])(Ryujinx|Ryubing)(?![\w./-])")]
    private static partial Regex EmulatorName();

    public static string Rebrand(string text) =>
        string.IsNullOrEmpty(text) ? text : EmulatorName().Replace(text, AppName);
}
