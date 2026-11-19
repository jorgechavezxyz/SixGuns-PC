using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Ryujinx.Ava.Remaster;

/// <summary>
/// The game's mods folder (switch/sixguns/mods/), the same one a Switch uses:
/// <list type="bullet">
/// <item><c>*.sgmod</c>: mod packages (SixGuns-NX tools/modding/sgmod.py). They hold no game data, so they can be
/// shared; the port sets each one up from the player's own game files when the game starts (port/source/modmgr.c)
/// and writes what happened to <c>mods/.built/status.txt</c>.</item>
/// <item><c>*.zip</c>: plain mods (stored zips in the game's file layout), for the player's own use.</item>
/// <item><c>mods/disabled/</c>: mods that are turned off. The port does not read this folder.</item>
/// </list>
/// Adding checks a mod before copying it in; nothing here touches saves or the game's own files.
/// </summary>
public static partial class ModLibrary
{
    public sealed record Mod(string Path, bool Enabled, bool Package, string Id, string Name, string Version, string Author,
        string Description, string Map, string State, string Message)
    {
        public string File => System.IO.Path.GetFileName(Path);
        /// <summary>New things a content mod adds to the game (mod.json content.entities: weapons, outfits, props).</summary>
        public int Adds { get; init; }
    }

    public const int Format = 1;

    [GeneratedRegex("^[a-z0-9][a-z0-9_.-]{0,31}$")]
    private static partial Regex IdPattern();

    public static string Folder(string root) => System.IO.Path.Combine(root, "mods");
    public static string DisabledFolder(string root) => System.IO.Path.Combine(root, "mods", "disabled");

    /// <summary>Every mod, turned on or off, sorted by name.</summary>
    public static List<Mod> List(string root)
    {
        var status = ReadStatus(root);
        var mods = new List<Mod>();
        foreach (var (dir, enabled) in new[] { (Folder(root), true), (DisabledFolder(root), false) })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (string path in Directory.EnumerateFiles(dir))
            {
                string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                if (ext != ".sgmod" && ext != ".zip") continue;
                try { mods.Add(Read(path, enabled, status)); }
                catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
                {
                    mods.Add(new Mod(path, enabled, ext == ".sgmod", "", System.IO.Path.GetFileNameWithoutExtension(path), "", "", "",
                        null, "error", ex.Message));
                }
            }
        }
        return mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Enabled ? 0 : 1).ToList();
    }

    /// <summary>Checks a mod file and copies it into the mods folder (turned on). A package replaces an installed
    /// package with the same id (an update); a plain zip replaces one with the same file name.</summary>
    public static Mod Add(string root, string source)
    {
        string ext = System.IO.Path.GetExtension(source).ToLowerInvariant();
        Mod mod;
        string target;
        if (ext == ".sgmod")
        {
            mod = ReadPackage(source, true, null);
            target = System.IO.Path.Combine(Folder(root), mod.Id + ".sgmod");
        }
        else if (ext == ".zip")
        {
            mod = ReadZip(source, true, null);
            target = System.IO.Path.Combine(Folder(root), System.IO.Path.GetFileName(source));
        }
        else throw new InvalidDataException("Choose a Six Guns mod: a .sgmod package or a mod .zip.");
        if (!SamePath(source, target))
        {
            // the new copy is in place before anything old is removed: a failed copy (full disk, file in use)
            // leaves the installed mod as it was
            Directory.CreateDirectory(Folder(root));
            string part = target + ".part";
            try
            {
                System.IO.File.Copy(source, part, true);
                System.IO.File.Move(part, target, true);
            }
            finally { if (System.IO.File.Exists(part)) System.IO.File.Delete(part); }
        }
        // then the older copies: the same package id under another file name (or turned off), or the same zip turned off
        foreach (var old in List(root))
        {
            bool same = ext == ".sgmod" ? old.Package && old.Id == mod.Id : !old.Package && !old.Enabled && old.File.Equals(System.IO.Path.GetFileName(source), StringComparison.OrdinalIgnoreCase);
            if (same && !SamePath(old.Path, target)) System.IO.File.Delete(old.Path);
        }
        return Read(target, true, ReadStatus(root));
    }

    public static void SetEnabled(string root, Mod mod, bool enabled)
    {
        if (mod.Enabled == enabled) return;
        string dir = enabled ? Folder(root) : DisabledFolder(root);
        Directory.CreateDirectory(dir);
        System.IO.File.Move(mod.Path, System.IO.Path.Combine(dir, mod.File), true);
    }

    /// <summary>Deletes the mod file. What the port built from a package is removed when the game next starts.</summary>
    public static void Remove(Mod mod) => System.IO.File.Delete(mod.Path);

    // ------------------------------------------------------------------ reading

    private static Mod Read(string path, bool enabled, Dictionary<string, (string State, string Message)> status) =>
        System.IO.Path.GetExtension(path).Equals(".sgmod", StringComparison.OrdinalIgnoreCase)
            ? ReadPackage(path, enabled, status) : ReadZip(path, enabled, status);

    private static Mod ReadPackage(string path, bool enabled, Dictionary<string, (string State, string Message)> status)
    {
        using var zip = OpenZip(path, "This is not a Six Guns mod package (it is damaged or not a zip).");
        var manifest = zip.GetEntry("mod.json") ?? throw new InvalidDataException("This is not a Six Guns mod package (it has no mod.json).");
        if (manifest.Length > 65536) throw new InvalidDataException("This mod's mod.json is too large.");
        JsonElement m;
        try
        {
            using var stream = manifest.Open();
            using var doc = JsonDocument.Parse(stream);
            m = doc.RootElement.Clone();
        }
        catch (JsonException) { throw new InvalidDataException("This mod's mod.json is not valid."); }
        if (m.ValueKind != JsonValueKind.Object) throw new InvalidDataException("This mod's mod.json is not valid.");
        if (m.TryGetProperty("format", out var f) && (f.ValueKind != JsonValueKind.Number || !f.TryGetInt32(out int format) || format != Format))
            throw new InvalidDataException("This mod needs a newer Six Guns. Check for updates first.");
        string id = Str(m, "id"), name = Str(m, "name");
        if (!IdPattern().IsMatch(id)) throw new InvalidDataException("This mod's mod.json has no valid \"id\".");
        if (name.Trim().Length == 0) throw new InvalidDataException("This mod's mod.json has no \"name\".");
        foreach (var entry in zip.Entries)
        {
            string n = entry.FullName;
            if (n == "mod.json" || n.EndsWith('/')) continue;
            if (!(n.StartsWith("files/", StringComparison.Ordinal) || n.StartsWith("patches/", StringComparison.Ordinal)) ||
                n.Contains("..") || n.Contains('\\') || n.Contains(':'))
                throw new InvalidDataException($"This mod package contains an unexpected file ({n}).");
        }
        string map = m.TryGetProperty("map", out var mp) && mp.ValueKind == JsonValueKind.Object ? Str(mp, "name") : null;
        var (state, message) = PackageState(path, enabled, status);
        int adds = m.TryGetProperty("content", out var ct) && ct.ValueKind == JsonValueKind.Object &&
                   ct.TryGetProperty("entities", out var en) && en.ValueKind == JsonValueKind.Array ? en.GetArrayLength() : 0;
        return new Mod(path, enabled, true, id, name.Trim(), Str(m, "version"), Str(m, "author"), Str(m, "description"),
            string.IsNullOrEmpty(map) ? null : map, state, message) { Adds = adds };
    }

    private static Mod ReadZip(string path, bool enabled, Dictionary<string, (string State, string Message)> status)
    {
        using var zip = OpenZip(path, "This is not a mod zip (it is damaged or not a zip).");
        if (zip.Entries.Any(e => e.Length > 0 && e.CompressedLength != e.Length))
            throw new InvalidDataException("This mod zip is compressed. The game needs stored zips: pack it with tools/scripts/pack_mod.py.");
        string map = null;
        var cm = zip.GetEntry("custom_map.txt");
        if (cm != null)
        {
            using var reader = new StreamReader(cm.Open());
            map = reader.ReadToEnd().Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("name=", StringComparison.Ordinal))?[5..];
        }
        string file = System.IO.Path.GetFileName(path);
        // a plain zip with the same file name as a built package is not loaded (the package replaces it)
        bool hidden = enabled && status != null && status.Keys.Any(k => k.StartsWith("built:", StringComparison.Ordinal) &&
                                                                   k[6..].Equals(file, StringComparison.OrdinalIgnoreCase));
        return new Mod(path, enabled, false, "", System.IO.Path.GetFileNameWithoutExtension(path), "", "",
            "Mod zip for your own use (it can contain game files, so do not share it).", map,
            !enabled ? "off" : hidden ? "replaced" : "ok", hidden ? "An installed mod package replaces it." : "");
    }

    private static (string, string) PackageState(string path, bool enabled,
        Dictionary<string, (string State, string Message)> status)
    {
        if (!enabled) return ("off", "");
        // the status is from the game's last start: a package added or changed since then is set up at the next one
        // (copying keeps no old time stamp: Add copies through a new file)
        if (status != null && status.TryGetValue(System.IO.Path.GetFileName(path), out var s) &&
            System.IO.File.GetLastWriteTimeUtc(path) <= StatusTime(path) && System.IO.File.GetCreationTimeUtc(path) <= StatusTime(path))
            return (s.State, s.Message);
        return ("new", "Set up when you next press Play.");
    }

    private static DateTime StatusTime(string modPath)
    {
        string p = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(modPath)!, ".built", "status.txt");
        return System.IO.File.Exists(p) ? System.IO.File.GetLastWriteTimeUtc(p) : DateTime.MinValue;
    }

    /// <summary>mods/.built/status.txt, written by the port at every start: file, id, ok|error|needs, name, version,
    /// message. Built zip names are added as "built:&lt;name&gt;".</summary>
    private static Dictionary<string, (string State, string Message)> ReadStatus(string root)
    {
        var result = new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase);
        string built = System.IO.Path.Combine(Folder(root), ".built");
        try
        {
            string p = System.IO.Path.Combine(built, "status.txt");
            if (System.IO.File.Exists(p))
                foreach (string line in System.IO.File.ReadAllLines(p))
                {
                    string[] f = line.Split('\t');
                    if (f.Length >= 3) result[f[0]] = (f[2], f.Length > 5 ? f[5] : "");
                }
            if (Directory.Exists(built))
                foreach (string z in Directory.EnumerateFiles(built, "*.zip")) result["built:" + System.IO.Path.GetFileName(z)] = ("ok", "");
        }
        catch (IOException) { }
        return result;
    }

    private static ZipArchive OpenZip(string path, string error)
    {
        try { return ZipFile.OpenRead(path); }
        catch (InvalidDataException) { throw new InvalidDataException(error); }
    }

    private static string Str(JsonElement o, string key) =>
        o.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
