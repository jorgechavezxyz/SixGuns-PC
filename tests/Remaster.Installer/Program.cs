using Ryujinx.Ava.Remaster;

// SIXGUNS_SD_BUILD: path of the bundled SD Builder (sixguns-sd-build). With it, the rejection test runs the real
// file checks; without it, a missing installer must still refuse cleanly. Optional arguments for a full install:
// official APK, OBB folder, disposable destination. No keys, firmware or game data are bundled into the test project.
// update-test <feed URL> <throwaway copy of an installed Windows launcher>: the updater against a release feed
// (SIXGUNS_UPDATE_FEED format), without restarting anything.
if (args.Length == 3 && args[0] == "update-test")
{
    await UpdateTest(args[1], args[2]);
    return;
}
string scratch = Path.Combine(Path.GetTempPath(), "remaster-installer-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(scratch);
int passed = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAIL: " + name);
    Console.WriteLine("PASS: " + name);
    passed++;
}
void Refuses(Action action, string name)
{
    try { action(); }
    catch (Exception ex) when (ex is InvalidDataException or OperationCanceledException or FileNotFoundException)
    { Check(true, name); return; }
    throw new Exception("FAIL: " + name + " was accepted");
}
try
{
    string builder = Environment.GetEnvironmentVariable("SIXGUNS_SD_BUILD") ?? Path.Combine(scratch, "missing-builder");
    string sample = Path.Combine(scratch, "sample");
    File.WriteAllText(sample, "untrusted game data");
    Refuses(() => GameInstaller.ValidateKeys(sample), "non-key input rejected without exposing content");
    string keyFixture = Path.Combine(scratch, "fixture.keys");
    File.WriteAllText(keyFixture, "header_key = 00\n");
    Refuses(() => GameInstaller.ValidateKeys(keyFixture), "incomplete key file rejected");
    string root = Path.Combine(scratch, "game");
    Directory.CreateDirectory(Path.Combine(root, "save"));
    string save = Path.Combine(root, "save", "sentinel");
    File.WriteAllText(save, "preserve me");
    File.WriteAllText(Path.Combine(root, "prefs.txt"), "preferences");
    File.WriteAllText(Path.Combine(root, "device_id.txt"), "test identity");
    Refuses(() => GameInstaller.Install(sample, scratch, builder, root, _ => { }, default),
        File.Exists(builder) ? "SD Builder rejects an unofficial APK" : "missing installer refused");
    Check(File.ReadAllText(save) == "preserve me" && File.ReadAllText(Path.Combine(root, "device_id.txt")) == "test identity", "rejected install preserves save and identity");
    Check(!Directory.EnumerateDirectories(scratch, ".setup-*").Any(), "staging cleaned after failure");
    File.WriteAllText(Path.Combine(root, "installation.complete"), "x");
    Check(!GameInstaller.IsInstalled(root), "completion marker alone cannot enable Play");
    if (args.Length == 3)
    {
        GameInstaller.Install(args[0], args[1], builder, args[2], Console.WriteLine, default);
        Check(GameInstaller.IsInstalled(args[2]), "official fresh install complete");
        Check(File.Exists(Path.Combine(args[2], "mods", "30_stadium.zip")), "Stadium map installed like on Switch");
        Check(File.Exists(Path.Combine(args[2], "emulator_mode")), "emulator marker added by the launcher");
        Directory.CreateDirectory(Path.Combine(args[2], "save"));
        string sentinel = Path.Combine(args[2], "save", "installer-test-sentinel");
        File.WriteAllText(sentinel, "preserve on reinstall");
        string ownMod = Path.Combine(args[2], "mods", "50_players_own.zip");
        File.WriteAllText(ownMod, "player mod");
        GameInstaller.Install(args[0], args[1], builder, args[2], Console.WriteLine, default);
        Check(File.ReadAllText(sentinel) == "preserve on reinstall", "official reinstall preserves save");
        Check(File.Exists(ownMod), "official reinstall keeps the player's own mods");
        File.Delete(sentinel);
        File.Delete(ownMod);
    }
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(scratch, true); }

static async Task UpdateTest(string feed, string app)
{
    int passed = 0;
    void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL: " + name);
        Console.WriteLine("PASS: " + name);
        passed++;
    }
    Dictionary<string, string> Snapshot() => Directory.EnumerateFiles(app, "*", SearchOption.AllDirectories)
        .Where(f => !f.Contains(".sixguns-update"))
        .ToDictionary(f => Path.GetRelativePath(app, f), f => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(f))));
    RemasterUpdater.Log = Console.WriteLine;
    Environment.SetEnvironmentVariable("SIXGUNS_UPDATE_FEED", feed);
    var update = await RemasterUpdater.CheckAsync(default, (new Version(0, 1, 0), "18e6512"));
    Check(update != null && update.Version == new Version(0, 1, 1), "older version is offered the update");
    Check(await RemasterUpdater.CheckAsync(default, (new Version(0, 1, 1), "0000000test")) == null, "same version and commit: no update");
    Check(await RemasterUpdater.CheckAsync(default, (new Version(0, 2, 0), "abc")) == null, "never offers a downgrade");

    var before = Snapshot();
    string locked = Path.Combine(app, "THIRDPARTY.md");
    using (File.Open(locked, FileMode.Open, FileAccess.Read, FileShare.None))
    {
        try { await RemasterUpdater.DownloadAndInstallAsync(update, Console.WriteLine, default, app, restart: false); Check(false, "locked file fails the swap"); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Check(true, "locked file fails the swap: " + ex.GetType().Name); }
    }
    var afterFailure = Snapshot();
    Check(before.Count == afterFailure.Count && before.All(kv => afterFailure.TryGetValue(kv.Key, out var h) && h == kv.Value),
        "failed swap rolls back to the exact previous files");
    Check(!Directory.EnumerateFileSystemEntries(app, "*.sgold").Any(), "no leftovers after rollback");

    await RemasterUpdater.DownloadAndInstallAsync(update, Console.WriteLine, default, app, restart: false);
    var after = Snapshot();
    Check(after["player-note.txt"] == before["player-note.txt"], "files the player added are untouched");
    Check(after.Where(kv => kv.Key.StartsWith("remaster-data")).All(kv => before.TryGetValue(kv.Key, out var h) && h == kv.Value) &&
          before.Keys.Count(k => k.StartsWith("remaster-data")) == after.Keys.Count(k => k.StartsWith("remaster-data")), "portable player data untouched");
    Check(after["Six Guns Remaster-NX.exe"] != before["Six Guns Remaster-NX.exe"], "launcher replaced by the new version");
    Check(Directory.EnumerateFileSystemEntries(app, "*.sgold").Any(), "old files kept until the new version starts");
    RemasterUpdater.CleanupAfterUpdate(app);
    Check(!Directory.EnumerateFileSystemEntries(app, "*.sgold").Any() && !Directory.Exists(Path.Combine(app, ".sixguns-update")), "cleanup after the new version starts");
    Console.WriteLine($"{passed} update checks passed.");
}
