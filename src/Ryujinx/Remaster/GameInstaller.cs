using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Ryujinx.Ava.Remaster;

// Exact hashes pin the complete, independently signature-verified official release.
// This intentionally rejects repacks, including APKs containing the same engine.
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
    public const string ApkHash = "224f2f7d9e4e3f49ffbc40094747cb85cae798ce2dc95c14e7026c23d8840f37";
    public const string EngineHash = "502fd831bcd6fe24d7e79f4ef41c3a36c7b6f02515df507c56618b4fd421d08e";
    public const string MainObb = "main.29431.com.gameloft.android.ANMP.GloftSXHM.obb";
    public const string PatchObb = "patch.29523.com.gameloft.android.ANMP.GloftSXHM.obb";
    public static readonly IReadOnlyDictionary<string, string> ObbHashes = new Dictionary<string, string>
    {
        [MainObb] = "07ea412d5fed53c1050031e7f5b491d36539cf509a0eb29012b05403da502212",
        [PatchObb] = "9480370d7c9caad7500fbc3c3bb5a308e05d6cc89f25fb6929b3fe3aa3cfc7a8",
    };

    public static bool IsInstalled(string root) => File.Exists(Path.Combine(root, "installation.complete")) &&
        new[] { "sixguns.nro", "libAndroid.so", "base.apk", "obb/" + MainObb, "obb/" + PatchObb }
            .All(p => File.Exists(Path.Combine(root, p)));

    public static void Verify(string path, string expected, string label, CancellationToken token)
    {
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
        }
        if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{label} is not the supported official Six Guns 2.9.9a file. Select the original, unmodified file.");
    }

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

    public static void Install(string apk, string obbFolder, string nro, string root, Action<string> report, CancellationToken token)
    {
        ValidatePort(nro);
        // Stage on the same volume. Existing saves, identity, preferences and mods stay in place.
        string parent = Path.GetDirectoryName(root)!;
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".setup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        bool preserveRecovery = false;
        try
        {
            report("Checking the official APK…");
            Copy(apk, Path.Combine(stage, "base.apk"), token);
            Verify(Path.Combine(stage, "base.apk"), ApkHash, "The APK", token);
            Directory.CreateDirectory(Path.Combine(stage, "obb"));
            foreach (var pair in ObbHashes)
            {
                report(pair.Key == MainObb ? "Checking the main game data…" : "Checking the patch data…");
                string source = Directory.EnumerateFiles(obbFolder, pair.Key, SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new FileNotFoundException($"The OBB folder is missing {pair.Key}. Select the folder containing both original OBB files.");
                string destination = Path.Combine(stage, "obb", pair.Key);
                Copy(source, destination, token);
                Verify(destination, pair.Value, pair.Key, token);
            }
            report("Preparing your game…");
            using (var archive = ZipFile.OpenRead(Path.Combine(stage, "base.apk")))
            {
                var engine = archive.GetEntry("lib/arm64-v8a/libAndroid.so") ?? throw new InvalidDataException("The APK has no arm64 engine.");
                Extract(engine, Path.Combine(stage, "libAndroid.so"), token);
                Verify(Path.Combine(stage, "libAndroid.so"), EngineHash, "The game engine", token);
                foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("assets/", StringComparison.Ordinal) && e.Name.Length > 0))
                {
                    string destination = Path.GetFullPath(Path.Combine(stage, entry.FullName));
                    if (!destination.StartsWith(stage + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                        throw new InvalidDataException("Unsafe APK asset path.");
                    Extract(entry, destination, token);
                }
            }
            Copy(nro, Path.Combine(stage, "sixguns.nro"), token);
            File.WriteAllText(Path.Combine(stage, "emulator_mode"), "");
            File.WriteAllText(Path.Combine(stage, "lan"), "");
            File.WriteAllText(Path.Combine(stage, "installation.complete"), ApkHash);
            token.ThrowIfCancellationRequested();
            report("Finishing installation…");
            if (!Directory.Exists(root))
            {
                Directory.Move(stage, root);
                return;
            }
            // Commit only managed paths. Roll back on errors; never touch player data.
            string backup = Path.Combine(stage, "previous");
            Directory.CreateDirectory(backup);
            string[] managed = { "base.apk", "libAndroid.so", "assets", "obb", "sixguns.nro", "emulator_mode", "lan", "installation.complete" };
            var movedOld = new List<string>();
            var movedNew = new List<string>();
            try
            {
                foreach (string name in managed)
                {
                    string target = Path.Combine(root, name);
                    if (File.Exists(target) || Directory.Exists(target))
                    {
                        Move(target, Path.Combine(backup, name));
                        movedOld.Add(name);
                    }
                    Move(Path.Combine(stage, name), target);
                    movedNew.Add(name);
                }
            }
            catch
            {
                try
                {
                    foreach (string name in movedNew.AsEnumerable().Reverse()) Move(Path.Combine(root, name), Path.Combine(stage, name));
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
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static void Copy(string from, string to, CancellationToken token)
    {
        using var input = File.OpenRead(from);
        using var output = File.Create(to);
        CopyStream(input, output, token);
    }

    private static void Extract(ZipArchiveEntry entry, string to, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        using var input = entry.Open();
        using var output = File.Create(to);
        CopyStream(input, output, token);
    }

    private static void CopyStream(Stream input, Stream output, CancellationToken token)
    {
        byte[] buffer = new byte[1024 * 1024];
        int count;
        while ((count = input.Read(buffer)) != 0)
        {
            token.ThrowIfCancellationRequested();
            output.Write(buffer, 0, count);
        }
    }
}
