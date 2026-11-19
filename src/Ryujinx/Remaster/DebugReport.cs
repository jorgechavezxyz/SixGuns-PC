using Ryujinx.Common.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Ryujinx.Ava.Remaster;

/// <summary>
/// "Save log to Desktop": one text file (<see cref="FileName"/>) a player can send for debugging. It holds the
/// system, launcher, port and mod details and the newest logs: the game's own (switch/sixguns/log.txt and
/// log.prev.txt, port/source/util.c) and the emulator's (Logs/*.log). Personal details are removed before saving:
/// the home folder and the user and computer names, public IP addresses, and anything shaped like a key (32 or more
/// hex digits, except 40: git commit ids). Keys, firmware, game files and saves are never included.
///
/// It also notices when a game run went wrong, so the launcher can point at the button: a marker file is written
/// when the game starts and removed when it returns to the launcher, so a marker left at startup means the launcher
/// itself closed while the game ran; the port writes "=== CRASH:" (port/source/crash.c) or "FATAL:" to log.txt.
/// </summary>
internal static partial class DebugReport
{
    public const string FileName = "SixGuns-log.txt";
    private const int GameLogBytes = 1536 * 1024, PreviousGameLogBytes = 512 * 1024, EmulatorLogBytes = 768 * 1024;

    private static string Marker => Path.Combine(AppDataManager.BaseDirPath, "remaster-game-running");
    private static string GameLog => Path.Combine(RemasterProfile.GameRoot, "log.txt");
    private static string PreviousGameLog => Path.Combine(RemasterProfile.GameRoot, "log.prev.txt");

    /// <summary>Raised on the UI thread when the game has returned to the launcher.</summary>
    public static event Action GameExited;

    public static void GameStarting()
    {
        try { File.WriteAllText(Marker, DateTime.Now.ToString("O")); } catch { /* only a hint for the next start */ }
    }

    public static void GameStopped()
    {
        try { File.Delete(Marker); } catch { }
        GameExited?.Invoke();
    }

    /// <summary>At launcher start: whether the launcher closed while the game was running last time (then forgets it).</summary>
    public static bool ClosedWhileRunning()
    {
        try
        {
            if (!File.Exists(Marker)) return false;
            File.Delete(Marker);
            return true;
        }
        catch { return false; }
    }

    /// <summary>After the game returned: what went wrong in that run according to its log.txt, or null.</summary>
    public static string GameProblem()
    {
        try
        {
            string tail = Tail(GameLog, 256 * 1024);
            if (tail.Contains("=== CRASH:")) return "The game crashed.";
            int fatal = tail.LastIndexOf("FATAL:", StringComparison.Ordinal);
            if (fatal >= 0) return "The game stopped with an error: " + Redact(tail[(fatal + 6)..].Split('\n')[0].Trim());
        }
        catch { }
        return null;
    }

    /// <summary>Writes the report to the Desktop (or the home folder when there is none) and returns its path.</summary>
    public static string Save(string launcherVersion, string portTag, string firmware)
    {
        var text = new StringBuilder();
        text.AppendLine("Six Guns Remaster-NX debug log");
        text.AppendLine($"Saved:     {DateTime.Now:yyyy-MM-dd HH:mm:ss zzz}");
        text.AppendLine($"Launcher:  {launcherVersion}" + (portTag == null ? "" : $" · port {portTag}"));
        text.AppendLine($"System:    {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture}), " +
                        $"{Environment.ProcessorCount} CPU threads, {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024 * 1024.0):0.#} GB memory");
        text.AppendLine($"Firmware:  {firmware ?? "not installed"}");
        text.AppendLine($"Keys:      {(File.Exists(Path.Combine(AppDataManager.KeysDirPath, "prod.keys")) ? "present" : "missing")} (never included)");
        text.AppendLine($"Game:      {(GameInstaller.IsInstalled(RemasterProfile.GameRoot) ? "installed" : "not installed")}");
        AppendSection(text, "Game files", () => GameFiles());
        AppendSection(text, "Mods", () => Mods());
        AppendSection(text, "Game log (log.txt, this or the last run)", () => Tail(GameLog, GameLogBytes));
        AppendSection(text, "Game log of the run before (log.prev.txt)", () => Tail(PreviousGameLog, PreviousGameLogBytes));
        var emulatorLogs = EmulatorLogs();
        for (int i = 0; i < emulatorLogs.Count; i++)
        {
            var log = emulatorLogs[i];
            AppendSection(text, $"Emulator log {log.Name}" + (i == 0 ? " (newest)" : ""), () => Tail(log.FullName, EmulatorLogBytes));
        }
        if (emulatorLogs.Count == 0) AppendSection(text, "Emulator log", () => "");

        string folder = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
            folder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string path = Path.Combine(folder, FileName);
        File.WriteAllText(path, Redact(text.ToString()), new UTF8Encoding(false));
        return path;
    }

    private static void AppendSection(StringBuilder text, string title, Func<string> body)
    {
        text.AppendLine().AppendLine($"===== {title} =====");
        string content;
        try { content = body(); }
        catch (Exception ex) { content = $"(could not be read: {ex.Message})"; }
        text.AppendLine(string.IsNullOrEmpty(content) ? "(none)" : content.TrimEnd());
    }

    private static string GameFiles()
    {
        string root = RemasterProfile.GameRoot;
        if (!Directory.Exists(root)) return "";
        var lines = new List<string>();
        foreach (var file in new DirectoryInfo(root).GetFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
            lines.Add($"{file.Name,-32} {file.Length,12:N0} bytes  {file.LastWriteTime:yyyy-MM-dd HH:mm}");
        foreach (var dir in new DirectoryInfo(root).GetDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            lines.Add($"{dir.Name + "/",-32} {dir.EnumerateFiles("*", SearchOption.AllDirectories).Count(),12:N0} files");
        return string.Join(Environment.NewLine, lines);
    }

    private static string Mods()
    {
        var mods = ModLibrary.List(RemasterProfile.GameRoot);
        return string.Join(Environment.NewLine, mods.Select(m =>
            $"{m.File}: {m.Name} {m.Version} [{(m.Enabled ? "on" : "off")}, {m.State}]" + (string.IsNullOrEmpty(m.Message) ? "" : " " + m.Message)));
    }

    private static List<FileInfo> EmulatorLogs()
    {
        string dir = AppDataManager.LogsDirPath;
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return new List<FileInfo>();
        return new DirectoryInfo(dir).GetFiles("*.log").OrderByDescending(f => f.LastWriteTimeUtc).Take(2).ToList();
    }

    /// <summary>The end of a text file (the whole file when it is short), readable while another program writes it.</summary>
    private static string Tail(string path, int bytes)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long start = Math.Max(0, stream.Length - bytes);
        stream.Seek(start, SeekOrigin.Begin);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        string text = reader.ReadToEnd();
        if (start > 0)
        {
            int newline = text.IndexOf('\n');
            text = $"(first {start:N0} bytes left out)" + Environment.NewLine + (newline >= 0 ? text[(newline + 1)..] : text);
        }
        return text.Replace("\0", "");
    }

    [GeneratedRegex("(?<![0-9A-Fa-f])[0-9A-Fa-f]{32,}(?![0-9A-Fa-f])")]
    private static partial Regex LongHex();

    [GeneratedRegex(@"(?<![\d.])(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})(?![\d.])")]
    private static partial Regex Ipv4();

    /// <summary>Removes personal details: home folder, user and computer names, public IPv4 addresses, key-like hex.</summary>
    internal static string Redact(string text)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(home) && home.Length > 3)
        {
            text = text.Replace(home, "~", StringComparison.OrdinalIgnoreCase);
            text = text.Replace(home.Replace('\\', '/'), "~", StringComparison.OrdinalIgnoreCase);
        }
        foreach (string name in new[] { Environment.UserName, Environment.MachineName })
        {
            if (!string.IsNullOrEmpty(name) && name.Length >= 3)
                text = Regex.Replace(text, $@"(?<![A-Za-z0-9]){Regex.Escape(name)}(?![A-Za-z0-9])", "<name>", RegexOptions.IgnoreCase);
        }
        // Keys are 32 or 64 hex digits (or longer); exactly 40 is a git commit id (build versions), kept.
        text = LongHex().Replace(text, m => m.Length == 40 ? m.Value : "<hex removed>");
        text = Ipv4().Replace(text, m =>
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
            if (a > 255 || b > 255 || int.Parse(m.Groups[3].Value) > 255 || int.Parse(m.Groups[4].Value) > 255) return m.Value; // a version number
            bool local = a is 10 or 127 or 0 || (a == 192 && b == 168) || (a == 172 && b is >= 16 and <= 31) || (a == 169 && b == 254) || a >= 224;
            return local ? m.Value : "<ip removed>";
        });
        return text;
    }
}
