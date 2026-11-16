using Ryujinx.Ava.Remaster;
using System.Security.Cryptography;

// Run with optional official APK, OBB folder, NRO, and disposable destination arguments.
// No keys, firmware or game data are bundled into the test project.
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
    string sample = Path.Combine(scratch, "sample");
    File.WriteAllText(sample, "untrusted game data");
    string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sample)));
    GameInstaller.Verify(sample, digest, "sample", default);
    Check(true, "hash verification accepts exact bytes");
    Refuses(() => GameInstaller.Verify(sample, GameInstaller.ApkHash, "APK", default), "modified/repacked APK rejected");
    Refuses(() => GameInstaller.Verify(sample, digest, "sample", new CancellationToken(true)), "cancelled verification stops");
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
    Refuses(() => GameInstaller.Install(sample, scratch, sample, root, _ => { }, default), "unverified install rejected");
    Check(File.ReadAllText(save) == "preserve me" && File.ReadAllText(Path.Combine(root, "device_id.txt")) == "test identity", "rejected install preserves save and identity");
    Check(!Directory.EnumerateDirectories(scratch, ".setup-*").Any(), "staging cleaned after failure");
    File.WriteAllText(Path.Combine(root, "installation.complete"), "x");
    Check(!GameInstaller.IsInstalled(root), "completion marker alone cannot enable Play");
    if (args.Length == 4)
    {
        GameInstaller.Install(args[0], args[1], args[2], args[3], Console.WriteLine, default);
        Check(GameInstaller.IsInstalled(args[3]), "official fresh install complete");
        Directory.CreateDirectory(Path.Combine(args[3], "save"));
        string sentinel = Path.Combine(args[3], "save", "installer-test-sentinel");
        File.WriteAllText(sentinel, "preserve on reinstall");
        GameInstaller.Install(args[0], args[1], args[2], args[3], Console.WriteLine, default);
        Check(File.ReadAllText(sentinel) == "preserve on reinstall", "official reinstall preserves save");
        File.Delete(sentinel);
    }
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(scratch, true); }
