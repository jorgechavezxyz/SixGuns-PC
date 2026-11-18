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
        Check(File.Exists(Path.Combine(args[2], "mods", "stadium.sgmod")) || File.Exists(Path.Combine(args[2], "mods", "30_stadium.zip")), "Stadium map installed like on Switch");
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
    await PortTest(Path.Combine(scratch, "port"), Check);
    ModTest(Path.Combine(scratch, "mods"), Check);
    Console.WriteLine($"{passed} checks passed.");
}
finally { Directory.Delete(scratch, true); }

// PortUpdater against a local release feed: the desktop runs the newest SixGuns-NX release's sixguns.nro, never an
// older or damaged one. Offline (a small HTTP server on 127.0.0.1).
static async Task PortTest(string dir, Action<bool, string> Check)
{
    Directory.CreateDirectory(dir);
    byte[] Nro(byte fill)
    {
        var b = new byte[0x1000];
        Array.Fill(b, fill);
        BitConverter.GetBytes(0x304f524eu).CopyTo(b, 0x10); // "NRO0"
        BitConverter.GetBytes((uint)b.Length).CopyTo(b, 0x18);
        return b;
    }
    string Sha(byte[] b) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(b)).ToLowerInvariant();
    string builderDir = Path.Combine(dir, "sd-builder");
    Directory.CreateDirectory(builderDir);
    string builder = Path.Combine(builderDir, "sixguns-sd-build");
    string bundled = Path.Combine(dir, "bundled.nro");
    byte[] bundledBytes = Nro(1), newer = Nro(2);
    File.WriteAllBytes(bundled, bundledBytes);
    var bundledDate = DateTimeOffset.Parse("2026-11-17T12:00:00Z");
    File.WriteAllLines(Path.Combine(builderDir, "sixguns-nx.ref"), new[] { "34da270c70dafe14df1c99a5e2182462de5deb59", bundledDate.ToString("o"), "v0.1.3-preview" });
    Check(GameInstaller.BuildStamp(builder) == "sixguns-nx 34da270c70dafe14df1c99a5e2182462de5deb59", "build stamp uses only the commit line");
    Check(PortUpdater.BundledDate(builder) == bundledDate, "bundled port date read from sixguns-nx.ref");

    var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
    listener.Start();
    int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    string feedJson = "[]";
    byte[] served = newer;
    using var stop = new CancellationTokenSource();
    var server = Task.Run(async () =>
    {
        while (!stop.IsCancellationRequested)
        {
            System.Net.Sockets.TcpClient client;
            try { client = await listener.AcceptTcpClientAsync(stop.Token); } catch (OperationCanceledException) { return; }
            using (client)
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream);
                string request = await reader.ReadLineAsync() ?? "";
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                byte[] body = request.Contains("/feed") ? System.Text.Encoding.UTF8.GetBytes(feedJson) : served;
                byte[] head = System.Text.Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(head);
                await stream.WriteAsync(body);
            }
        }
    });
    string Feed(string published, byte[] digestOf, long size) =>
        $"[{{\"draft\":false,\"tag_name\":\"v9.9.9-test\",\"published_at\":\"{published}\",\"assets\":[{{\"name\":\"sixguns.nro\",\"size\":{size}," +
        $"\"digest\":\"sha256:{Sha(digestOf)}\",\"browser_download_url\":\"http://127.0.0.1:{port}/sixguns.nro\"}}]}}]";
    Environment.SetEnvironmentVariable("SIXGUNS_PORT_FEED", $"http://127.0.0.1:{port}/feed");
    string cache = Path.Combine(dir, "cache");
    try
    {
        feedJson = Feed("2026-11-10T00:00:00Z", newer, newer.Length);
        Check(await PortUpdater.RefreshAsync(cache, bundled, builder, _ => { }, default) == null &&
              PortUpdater.Choose(cache, bundled, builder).Path == bundled, "older release never replaces the bundled port");
        feedJson = Feed("2026-11-20T00:00:00Z", bundledBytes, bundledBytes.Length);
        Check(await PortUpdater.RefreshAsync(cache, bundled, builder, _ => { }, default) == null, "release equal to the bundled port: nothing downloaded");

        served = Nro(3); // the server hands out a different file than the release lists
        feedJson = Feed("2026-11-20T00:00:00Z", newer, newer.Length);
        try { await PortUpdater.RefreshAsync(cache, bundled, builder, _ => { }, default); Check(false, "damaged download refused"); }
        catch (InvalidDataException) { Check(true, "damaged download refused"); }
        Check(PortUpdater.Choose(cache, bundled, builder).Path == bundled && !Directory.EnumerateFiles(cache, "*.part").Any(),
            "after a refused download the bundled port runs, no partial file left");

        served = newer;
        var got = await PortUpdater.RefreshAsync(cache, bundled, builder, _ => { }, default);
        var chosen = PortUpdater.Choose(cache, bundled, builder);
        Check(got != null && chosen.Tag == "v9.9.9-test" && File.ReadAllBytes(chosen.Path).SequenceEqual(newer), "newer release downloaded, verified and chosen");
        Check(await PortUpdater.RefreshAsync(cache, bundled, builder, _ => { }, default) is { } again && again.Sha256 == Sha(newer),
            "already cached: not downloaded again");

        File.WriteAllBytes(chosen.Path, Nro(4));
        Check(PortUpdater.Choose(cache, bundled, builder).Path == bundled, "tampered cached port is ignored");

        File.WriteAllLines(Path.Combine(builderDir, "sixguns-nx.ref"), new[] { "f00d", "2026-12-01T00:00:00Z", "" });
        File.WriteAllBytes(Path.Combine(cache, "sixguns.nro"), newer);
        Check(PortUpdater.Choose(cache, bundled, builder).Path == bundled, "launcher bundling a newer port ignores an older cached release");
    }
    finally
    {
        stop.Cancel();
        listener.Stop();
        Environment.SetEnvironmentVariable("SIXGUNS_PORT_FEED", null);
        try { await server; } catch { }
    }
}

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

// ModLibrary: the launcher's mod list over the game's mods folder (what the port reads on a Switch too).
static void ModTest(string dir, Action<bool, string> Check)
{
    string root = Path.Combine(dir, "game"), src = Path.Combine(dir, "downloads");
    Directory.CreateDirectory(Path.Combine(root, "save"));
    Directory.CreateDirectory(src);
    File.WriteAllText(Path.Combine(root, "save", "sentinel"), "keep");
    string Package(string file, string json, params (string Name, string Data)[] extra)
    {
        string p = Path.Combine(src, file);
        using var z = System.IO.Compression.ZipFile.Open(p, System.IO.Compression.ZipArchiveMode.Create);
        if (json != null) using (var w = new StreamWriter(z.CreateEntry("mod.json").Open())) w.Write(json);
        foreach (var (n, d) in extra) using (var w = new StreamWriter(z.CreateEntry(n).Open())) w.Write(d);
        return p;
    }
    void Refused(Func<string> make, string name)
    {
        try { ModLibrary.Add(root, make()); Check(false, name); }
        catch (InvalidDataException) { Check(true, name); }
    }
    string good = Package("Cool Map v1.sgmod", "{\"format\":1,\"id\":\"coolmap\",\"name\":\"Cool Map\",\"version\":\"1.0\",\"author\":\"someone\"," +
        "\"description\":\"A \\\"cool\\\" map\",\"map\":{\"name\":\"Cool\",\"base\":1,\"level\":\"custommaps/cool/cool.bclara\"}}",
        ("files/textures/cool_df.tga", "x"));
    var added = ModLibrary.Add(root, good);
    Check(added.Id == "coolmap" && added.Map == "Cool" && added.Description == "A \"cool\" map" && File.Exists(Path.Combine(root, "mods", "coolmap.sgmod")),
        "package added under its id, manifest read");
    Check(added.State == "new", "new package: set up at the next Play");
    string v2 = Package("coolmap-2.sgmod", "{\"id\":\"coolmap\",\"name\":\"Cool Map\",\"version\":\"2.0\"}");
    ModLibrary.Add(root, v2);
    var list = ModLibrary.List(root);
    Check(list.Count == 1 && list[0].Version == "2.0", "same id replaces the installed package (update)");
    ModLibrary.SetEnabled(root, list[0], false);
    list = ModLibrary.List(root);
    Check(list.Count == 1 && !list[0].Enabled && list[0].State == "off" && File.Exists(Path.Combine(root, "mods", "disabled", "coolmap.sgmod")),
        "turned off: moved to mods/disabled/, still listed");
    ModLibrary.Add(root, v2);
    list = ModLibrary.List(root);
    Check(list.Count == 1 && list[0].Enabled, "adding a turned-off package turns it back on (one copy)");
    string content = Package("guns.sgmod", "{\"id\":\"guns\",\"name\":\"Guns\",\"content\":{\"entities\":[{\"clone\":\"A\",\"as\":\"B\"},{\"clone\":\"C\",\"as\":\"D\"}]}}");
    var guns = ModLibrary.Add(root, content);
    Check(guns.Adds == 2 && guns.Map == null, "content mod: number of new things read");
    ModLibrary.Remove(guns);
    Refused(() => Package("bad1.sgmod", null, ("files/a", "x")), "package without mod.json refused");
    Refused(() => Package("bad2.sgmod", "{\"id\":\"Bad Id\",\"name\":\"x\"}"), "invalid id refused");
    Refused(() => Package("bad3.sgmod", "{\"format\":2,\"id\":\"future\",\"name\":\"x\"}"), "newer format refused (update first)");
    Refused(() => Package("bad4.sgmod", "{\"id\":\"evil\",\"name\":\"x\"}", ("../../save/sentinel", "gone")), "path outside the package refused");
    Refused(() => { string p = Path.Combine(src, "notzip.sgmod"); File.WriteAllText(p, "hello"); return p; }, "not a zip refused");
    Refused(() => { string p = Path.Combine(src, "readme.txt"); File.WriteAllText(p, "hello"); return p; }, "other file types refused");
    string zipped = Path.Combine(src, "20_hats.zip");
    using (var z = System.IO.Compression.ZipFile.Open(zipped, System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("textures/hat.tga", System.IO.Compression.CompressionLevel.Optimal).Open())) w.Write(new string('a', 4000));
    Refused(() => zipped, "compressed mod zip refused (the game needs stored zips)");
    string stored = Path.Combine(src, "20_stored.zip");
    using (var z = System.IO.Compression.ZipFile.Open(stored, System.IO.Compression.ZipArchiveMode.Create))
        using (var w = new StreamWriter(z.CreateEntry("custom_map.txt", System.IO.Compression.CompressionLevel.NoCompression).Open())) w.Write("name=Old Map\nbase=2\nlevel=x.bclara\n");
    var plain = ModLibrary.Add(root, stored);
    Check(!plain.Package && plain.Map == "Old Map", "plain stored zip added, its map name read");
    // the port's report of the last start
    Directory.CreateDirectory(Path.Combine(root, "mods", ".built"));
    File.WriteAllText(Path.Combine(root, "mods", ".built", "status.txt"), "coolmap.sgmod\tcoolmap\terror\tCool Map\t2.0\tyour main:x is not the one this mod was made for\n");
    File.SetLastWriteTimeUtc(Path.Combine(root, "mods", "coolmap.sgmod"), DateTime.UtcNow.AddMinutes(-5));
    File.SetCreationTimeUtc(Path.Combine(root, "mods", "coolmap.sgmod"), DateTime.UtcNow.AddMinutes(-5));
    var cool = ModLibrary.List(root).First(m => m.Id == "coolmap");
    Check(cool.State == "error" && cool.Message.Contains("not the one"), "state and message from the game's last start");
    foreach (var m in ModLibrary.List(root)) ModLibrary.Remove(m);
    Check(ModLibrary.List(root).Count == 0 && File.ReadAllText(Path.Combine(root, "save", "sentinel")) == "keep", "removed; saves untouched");
}
