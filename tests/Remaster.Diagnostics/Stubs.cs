namespace Ryujinx.Common.Configuration
{
    internal static class AppDataManager
    {
        public static string BaseDirPath { get; set; }
        public static string KeysDirPath => System.IO.Path.Combine(BaseDirPath, "system");
        public static string LogsDirPath => System.IO.Path.Combine(BaseDirPath, "Logs");
    }
}
namespace Ryujinx.Ava.Remaster
{
    internal static class RemasterProfile { public static string GameRoot => System.IO.Path.Combine(Ryujinx.Common.Configuration.AppDataManager.BaseDirPath, "game"); }
    internal static class GraphicsChoice
    {
        public static System.TimeSpan ConfirmAfter => System.TimeSpan.FromDays(1);
        public static bool ForcedOpenGl => false;
        public static string Backend() => "Vulkan";
    }
    internal static class GameInstaller { public static bool IsInstalled(string root) => System.IO.File.Exists(System.IO.Path.Combine(root, "libAndroid.so")); }
    internal record TestMod(string File, string Name, string Version, bool Enabled, string State, string Message);
    internal static class ModLibrary
    {
        public static TestMod[] List(string root) => [new("sample.sgmod", "Sample mod", "1.0", true, "ok", "")];
    }
}
