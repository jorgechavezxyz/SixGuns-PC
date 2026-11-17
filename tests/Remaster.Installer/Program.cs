using Ryujinx.Ava.Remaster;

// SIXGUNS_SD_BUILD: path of the bundled SD Builder (sixguns-sd-build). With it, the rejection test runs the real
// file checks; without it, a missing installer must still refuse cleanly. Optional arguments for a full install:
// official APK, OBB folder, disposable destination. No keys, firmware or game data are bundled into the test project.
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
