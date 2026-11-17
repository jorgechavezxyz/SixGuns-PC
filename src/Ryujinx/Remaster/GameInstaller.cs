using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Ryujinx.Ava.Remaster;

// Game files are checked and prepared by the Six Guns NX SD Builder (signature-verified official APK,
// byte-identical OBBs; repacks are refused). This class runs it and commits the result safely.
public static class GameInstaller
{
    public static void ValidatePort(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        if (stream.Length < 0x80) throw new InvalidDataException("The bundled port is incomplete. Extract a fresh launcher copy.");
        stream.Position = 0x10;
        if (reader.ReadUInt32() != 0x304f524e) throw new InvalidDataException("The bundled port is not an NRO executable.");
        stream.Position = 0x18;
        uint size = reader.ReadUInt32();
        if (size < 0x80 || size > stream.Length) throw new InvalidDataException("The bundled port is truncated. Extract a fresh launcher copy.");
    }
    public const string MainObb = "main.29431.com.gameloft.android.ANMP.GloftSXHM.obb";
    public const string PatchObb = "patch.29523.com.gameloft.android.ANMP.GloftSXHM.obb";
    private const string StadiumMod = "mods/30_stadium.zip";

    /// <summary>Which SD Builder (SixGuns-NX commit, distribution/sixguns-nx.ref, copied next to the builder) made
    /// an installation. A launcher update with a newer builder rebuilds the game from the player's kept files.</summary>
    public static string BuildStamp(string builder)
    {
        string reference = Path.Combine(Path.GetDirectoryName(builder)!, "sixguns-nx.ref");
        return File.Exists(reference) ? "sixguns-nx " + File.ReadAllText(reference).Trim() : "sixguns-sd-build";
    }

    /// <summary>Installed, but by an older SD Builder than the one this launcher carries.</summary>
    public static bool NeedsRebuild(string root, string builder) =>
        IsInstalled(root) && File.Exists(builder) &&
        File.ReadAllText(Path.Combine(root, "installation.complete")).Trim() != BuildStamp(builder);

    /// <summary>Rebuilds the game from the official files kept in the game folder (base.apk, obb/): the player does
    /// not have to choose them again after an update. Saves and the player's own mods stay.</summary>
    public static void Rebuild(string root, string builder, Action<string> report, CancellationToken token)
    {
        string apk = Path.Combine(root, "base.apk"), obb = Path.Combine(root, "obb");
        if (!File.Exists(apk) || !Directory.Exists(obb))
            throw new FileNotFoundException("Your game files are missing from the game folder. Choose \"Set up files again\".");
        // The builder reads the kept files while it builds into a staging folder; only the commit replaces them.
        Install(apk, obb, builder, root, report, token);
    }

    public static bool IsInstalled(string root) => File.Exists(Path.Combine(root, "installation.complete")) &&
        new[] { "sixguns.nro", "libAndroid.so", "base.apk", "obb/" + MainObb, "obb/" + PatchObb }
            .All(p => File.Exists(Path.Combine(root, p)));

    public static void ValidateKeys(string path)
    {
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("Choose a prod.keys text file.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool validHeader = false;
        bool validMaster = false;
        foreach (string line in File.ReadLines(path))
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            string[] parts = trimmed.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[1].Length == 0 || parts[1].Length % 2 != 0 || !parts[1].All(Uri.IsHexDigit))
                throw new InvalidDataException("This file is not a valid prod.keys text file. Choose your original Switch key dump.");
            names.Add(parts[0]);
            if (parts[0].Equals("header_key", StringComparison.OrdinalIgnoreCase)) validHeader = parts[1].Length == 64 && parts[1].Any(c => c != '0');
            if (parts[0].StartsWith("master_key_", StringComparison.OrdinalIgnoreCase)) validMaster |= parts[1].Length == 32 && parts[1].Any(c => c != '0');
        }
        if (!validHeader || !validMaster)
            throw new InvalidDataException("This key dump is incomplete. It needs header_key and master keys. Firmware setup will check whether the keys match your firmware.");
    }

    /// <summary>
    /// Installs the game with the Six Guns NX SD Builder (sixguns-sd-build, bundled with the launcher), the same
    /// tool that builds a Switch SD card: the same official-file checks, the same sixguns.nro and the same Stadium
    /// map, so desktop and Switch players run identical builds in LAN matches. The launcher only adds the
    /// emulator marker and commits the result next to the player's saves.
    /// </summary>
    public static void Install(string apk, string obbFolder, string builder, string root, Action<string> report, CancellationToken token)
    {
        if (!File.Exists(builder))
            throw new FileNotFoundException("The game installer (sd-builder) is missing. Extract a fresh copy of the launcher.", builder);
        // Stage on the same volume. Existing saves, identity, preferences and mods stay in place.
        string parent = Path.GetDirectoryName(root)!;
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        bool preserveRecovery = false;
        try
        {
            RunBuilder(builder, apk, obbFolder, Path.Combine(stage, "out"), report, token);
            string built = Path.Combine(stage, "out", "switch", "sixguns");
            foreach (string required in new[] { "sixguns.nro", "libAndroid.so", "base.apk", "obb/" + MainObb, "obb/" + PatchObb, StadiumMod })
                if (!File.Exists(Path.Combine(built, required)))
                    throw new InvalidDataException($"The game installer did not produce {required}. Extract a fresh copy of the launcher.");
            ValidatePort(Path.Combine(built, "sixguns.nro"));
            // Emulator only (never on a real SD card): tells the port it runs under the emulator.
            File.WriteAllText(Path.Combine(built, "emulator_mode"), "");
            File.WriteAllText(Path.Combine(built, "installation.complete"), BuildStamp(builder));
            token.ThrowIfCancellationRequested();
            report("Finishing installation…");
            if (!Directory.Exists(root))
            {
                Directory.Move(built, root);
                return;
            }
            // Commit only managed paths. Roll back on errors; never touch player data (save/, prefs.txt,
            // device_id.txt, save_backups/, the player's own mods).
            string backup = Path.Combine(stage, "previous");
            Directory.CreateDirectory(backup);
            string[] managed = { "base.apk", "libAndroid.so", "assets", "obb", "sixguns.nro", StadiumMod, "emulator_mode", "lan", "installation.complete" };
            var movedOld = new List<string>();
            var movedNew = new List<string>();
            try
            {
                foreach (string name in managed)
                {
                    string source = Path.Combine(built, name);
                    if (!File.Exists(source) && !Directory.Exists(source)) continue;
                    string target = Path.Combine(root, name);
                    if (File.Exists(target) || Directory.Exists(target))
                    {
                        Move(target, Path.Combine(backup, name));
                        movedOld.Add(name);
                    }
                    Move(source, target);
                    movedNew.Add(name);
                }
            }
            catch
            {
                try
                {
                    foreach (string name in movedNew.AsEnumerable().Reverse()) Move(Path.Combine(root, name), Path.Combine(built, name));
                    foreach (string name in movedOld.AsEnumerable().Reverse()) Move(Path.Combine(backup, name), Path.Combine(root, name));
                }
                catch (Exception rollbackError)
                {
                    preserveRecovery = true;
                    throw new IOException($"Installation was interrupted. Recovery files were kept at {stage}. Your save folder was not changed.", rollbackError);
                }
                throw;
            }
        }
        finally
        {
            if (!preserveRecovery && Directory.Exists(stage)) Directory.Delete(stage, true);
        }
    }

    private static void Move(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static void RunBuilder(string builder, string apk, string obbFolder, string output, Action<string> report, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(builder);
            if (!mode.HasFlag(UnixFileMode.UserExecute)) File.SetUnixFileMode(builder, mode | UnixFileMode.UserExecute);
        }
        var start = new ProcessStartInfo(builder)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetDirectoryName(builder)!,
        };
        foreach (string arg in new[] { "--apk", apk, "--obb-dir", obbFolder, "--out", output, "--stadium" }) start.ArgumentList.Add(arg);
        start.Environment.Remove("PYTHONHOME");
        start.Environment.Remove("PYTHONPATH");
        report("Checking your game files…");
        using var process = Process.Start(start) ?? throw new IOException("The game installer could not be started.");
        var tail = new Queue<string>();
        string refused = null;
        void Line(string line)
        {
            if (line == null) return;
            lock (tail)
            {
                tail.Enqueue(line);
                if (tail.Count > 8) tail.Dequeue();
            }
            if (line.StartsWith("REFUSED: ", StringComparison.Ordinal)) refused = line["REFUSED: ".Length..];
            else if (line.StartsWith("== Checking", StringComparison.Ordinal)) report("Checking your game files…");
            else if (line.StartsWith("== Writing", StringComparison.Ordinal)) report("Installing the game…");
            else if (line.StartsWith("== Stadium", StringComparison.Ordinal)) report("Building the Stadium multiplayer map…");
        }
        process.OutputDataReceived += (_, e) => Line(e.Data);
        process.ErrorDataReceived += (_, e) => Line(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using (token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } }))
            process.WaitForExit();
        process.WaitForExit(); // flush the redirected output
        token.ThrowIfCancellationRequested();
        if (process.ExitCode == 2 && refused != null)
            throw new InvalidDataException(refused);
        if (process.ExitCode != 0)
        {
            string detail;
            lock (tail) detail = string.Join(" / ", tail);
            throw new IOException($"The game files could not be prepared (installer exit code {process.ExitCode}). {detail}");
        }
    }
}
