using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Remaster;

/// <summary>
/// Updates from the project's GitHub releases (jorgechavezxyz/SixGuns-PC). A release that offers an update carries
/// <c>update.json</c> ({"version": "0.1.1", "commit": "&lt;sha&gt;"}), one package per platform and SHA256SUMS.txt.
/// The package is downloaded, checked against SHA256SUMS.txt, and swapped in next to the running copy (the old files
/// are kept until the new version has started, so a failed swap rolls back). Player data is never touched: the
/// portable remaster-data folder and the user data folder stay where they are. After the restart, the launcher sees
/// that its SD Builder is newer than the installed game and rebuilds the game from the player's own files kept in the
/// game folder (base.apk, obb/), the same way a Switch SD card is rebuilt.
/// </summary>
internal static class RemasterUpdater
{
    private const string Repository = "jorgechavezxyz/SixGuns-PC";
    private const string Manifest = "update.json";
    private const string Checksums = "SHA256SUMS.txt";
    private const string OldSuffix = ".sgold";
    private const string StartupMarker = "startup.ok";
    private const string SwapPending = "swap.pending";
    private const string StartupMarkerVariable = "SIXGUNS_UPDATE_STARTUP_MARKER";
    private const string WindowsExe = "Six Guns Remaster-NX.exe";

    public sealed record Update(Version Version, string Commit, string Tag, string PackageUrl, string ChecksumsUrl, string PackageName);

    private static readonly HttpClient Http = CreateClient();

    /// <summary>Where warnings go (the launcher sets the app log; tests print them).</summary>
    public static Action<string> Log = _ => { };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SixGunsRemasterNX-Updater");
        return client;
    }

    /// <summary>"0.1.0+&lt;commit&gt;" from the build (the .NET SDK adds the source commit).</summary>
    public static (Version Version, string Commit) Current
    {
        get
        {
            string informational = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
            string[] parts = informational.Split('+', 2);
            string numeric = new(parts[0].TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
            return (Version.TryParse(numeric, out var v) ? v : new Version(0, 0), parts.Length > 1 ? parts[1] : "");
        }
    }

    private static string PackageName => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && OperatingSystem.IsMacOS()
        ? "SixGuns-Remaster-NX-osx-arm64.zip"
        : OperatingSystem.IsMacOS() ? "SixGuns-Remaster-NX-osx-x64.zip" : "SixGuns-Remaster-NX-win-x64.zip";

    /// <summary>The installed copy this process runs from: the .app bundle on Mac, the folder on Windows.</summary>
    private static string InstalledLocation
    {
        get
        {
            string baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (OperatingSystem.IsMacOS() && baseDir.EndsWith(Path.Combine("Contents", "MacOS"), StringComparison.Ordinal))
                return Path.GetFullPath(Path.Combine(baseDir, "..", ".."));
            return baseDir;
        }
    }

    /// <summary>Why this copy cannot update itself, or null when it can.</summary>
    public static string CannotUpdateReason()
    {
        if (OperatingSystem.IsWindows())
            return Path.GetFileName(Environment.ProcessPath ?? "").Equals(WindowsExe, StringComparison.OrdinalIgnoreCase)
                ? null : "Updates are available for release downloads only.";
        if (OperatingSystem.IsMacOS())
        {
            string app = InstalledLocation;
            if (!app.EndsWith(".app", StringComparison.Ordinal)) return "Updates are available for release downloads only.";
            if (app.Contains("/AppTranslocation/", StringComparison.Ordinal))
                return "Move Six Guns Remaster-NX to your Applications folder to receive updates.";
            return null;
        }
        return "Updates are not available on this system.";
    }

    /// <summary>The newest release with an update for this platform, or null when this copy is current.</summary>
    public static async Task<Update> CheckAsync(CancellationToken token, (Version Version, string Commit)? running = null)
    {
        // SIXGUNS_UPDATE_FEED: a releases list in GitHub's format at another address (update tests only).
        string feed = Environment.GetEnvironmentVariable("SIXGUNS_UPDATE_FEED") ?? $"https://api.github.com/repos/{Repository}/releases?per_page=10";
        using var releases = JsonDocument.Parse(await Http.GetStringAsync(feed, token));
        var (current, commit) = running ?? Current;
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            var assets = release.GetProperty("assets").EnumerateArray()
                .ToDictionary(a => a.GetProperty("name").GetString() ?? "", a => a.GetProperty("browser_download_url").GetString() ?? "");
            if (!assets.TryGetValue(Manifest, out string manifestUrl) || !assets.TryGetValue(PackageName, out string packageUrl) ||
                !assets.TryGetValue(Checksums, out string checksumsUrl))
                continue;
            using var manifest = JsonDocument.Parse(await Http.GetStringAsync(manifestUrl, token));
            if (!Version.TryParse(manifest.RootElement.GetProperty("version").GetString(), out var version)) continue;
            string releaseCommit = manifest.RootElement.TryGetProperty("commit", out var c) ? c.GetString() ?? "" : "";
            bool newer = version > current || (version == current && releaseCommit.Length > 0 && commit.Length > 0 &&
                                                !releaseCommit.StartsWith(commit, StringComparison.OrdinalIgnoreCase) &&
                                                !commit.StartsWith(releaseCommit, StringComparison.OrdinalIgnoreCase));
            // The first release that carries an update for this platform is the newest one; never go backwards.
            return newer ? new Update(version, releaseCommit, release.GetProperty("tag_name").GetString(), packageUrl, checksumsUrl, PackageName) : null;
        }
        return null;
    }

    /// <summary>Downloads, verifies and installs the update, then starts the new version. Returns only on failure
    /// (the caller then exits on success: <see cref="Install"/> starts the new copy and the caller closes this one).</summary>
    public static async Task DownloadAndInstallAsync(Update update, Action<string> report, CancellationToken token,
        string location = null, bool restart = true)
    {
        if (location == null)
        {
            string reason = CannotUpdateReason();
            if (reason != null) throw new InvalidOperationException(reason);
            location = InstalledLocation;
        }
        string parent = Path.GetDirectoryName(location)!;
        // Staging next to the installed copy: same volume, so the swap is a set of renames.
        string staging = Path.Combine(OperatingSystem.IsWindows() ? location : parent, ".sixguns-update");
        if (File.Exists(Path.Combine(staging, SwapPending)) && !File.Exists(Path.Combine(staging, StartupMarker)))
            throw new IOException("A previous launcher update needs recovery. Keep the backup in " + staging);
        if (Directory.Exists(staging)) Directory.Delete(staging, true);
        Directory.CreateDirectory(staging);
        try
        {
            string package = Path.Combine(staging, update.PackageName);
            report($"Downloading update {update.Version}…");
            await DownloadAsync(update.PackageUrl, package, percent => report($"Downloading update {update.Version}… {percent}%"), token);
            report("Checking the download…");
            string sums = await Http.GetStringAsync(update.ChecksumsUrl, token);
            string expected = sums.Split('\n').Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
                .FirstOrDefault(p => p.Length == 2 && p[1].TrimStart('*', ' ') == update.PackageName)?[0];
            await using (var stream = File.OpenRead(package))
            {
                string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (expected == null || !actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("The downloaded update is damaged or incomplete. Try again later.");
            }
            token.ThrowIfCancellationRequested();
            report("Installing the update…");
            string extracted = Path.Combine(staging, "new");
            await Task.Run(() => Extract(package, extracted), token);
            token.ThrowIfCancellationRequested();
            await Task.Run(() => Install(extracted, location, restart));
        }
        catch
        {
            // A failed rollback can leave the only working copy in staging or in .sgold files.
            if (!File.Exists(Path.Combine(staging, SwapPending)))
                DeletePath(staging);
            throw;
        }
    }

    private static async Task DownloadAsync(string url, string file, Action<int> progress, CancellationToken token)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        await using var input = await response.Content.ReadAsStreamAsync(token);
        await using var output = File.Create(file);
        byte[] buffer = new byte[1 << 20];
        long done = 0;
        int last = -1, read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), token);
            done += read;
            int percent = total > 0 ? (int)(done * 100 / total.Value) : -1;
            if (percent != last && percent >= 0) { last = percent; progress(percent); }
        }
    }

    private static void Extract(string package, string destination)
    {
        Directory.CreateDirectory(destination);
        if (OperatingSystem.IsMacOS())
        {
            // ditto keeps the bundle's symlinks, permissions and signatures (ZipFile would not).
            using var ditto = Process.Start(new ProcessStartInfo("/usr/bin/ditto") { ArgumentList = { "-x", "-k", package, destination } });
            ditto!.WaitForExit();
            if (ditto.ExitCode != 0) throw new IOException("The update could not be unpacked.");
            return;
        }
        ZipFile.ExtractToDirectory(package, destination);
    }

    internal static void Install(string extracted, string location, bool restart)
    {
        string staging = Path.GetDirectoryName(extracted)!;
        string pending = Path.Combine(staging, SwapPending);
        string marker = Path.Combine(staging, StartupMarker);
        if (OperatingSystem.IsMacOS())
        {
            string app = Directory.EnumerateDirectories(extracted, "*.app").SingleOrDefault()
                ?? throw new InvalidDataException("The update does not contain the app.");
            string old = Path.Combine(staging, "previous.app");
            File.WriteAllText(pending, "");
            bool backedUp = false, installed = false;
            try
            {
                Directory.Move(location, old);
                backedUp = true;
                Directory.Move(app, location);
                installed = true;
                if (restart)
                {
                    StartAndWaitForLauncher(Path.Combine(location, "Contents", "MacOS", "SixGunsRemasterNX"), marker);
                }
            }
            catch
            {
                if (installed) Directory.Move(location, app);
                if (backedUp) Directory.Move(old, location);
                File.Delete(pending);
                throw;
            }
            if (!restart) File.WriteAllText(marker, "");
            return;
        }
        // Windows: every packaged file and folder replaces its old copy, which is renamed to *.sgold (a running exe or
        // DLL can be renamed, not overwritten). Anything that is not part of the package stays: remaster-data (portable
        // player data), the staging folder, files the player added.
        if (!File.Exists(Path.Combine(extracted, WindowsExe))) throw new InvalidDataException("The update does not contain the launcher.");
        var moved = new List<(string Source, string Target, bool HadOld)>();
        File.WriteAllText(pending, "");
        try
        {
            foreach (string source in Directory.EnumerateFileSystemEntries(extracted))
            {
                string name = Path.GetFileName(source);
                if (name.Equals("remaster-data", StringComparison.OrdinalIgnoreCase)) continue;
                string target = Path.Combine(location, name);
                bool hadOld = File.Exists(target) || Directory.Exists(target);
                if (hadOld)
                {
                    DeletePath(target + OldSuffix);
                    MovePath(target, target + OldSuffix);
                }
                moved.Add((source, target, hadOld));
                MovePath(source, target);
            }
            if (restart)
                StartAndWaitForLauncher(Path.Combine(location, WindowsExe), marker);
            else
                File.WriteAllText(marker, "");
        }
        catch
        {
            bool restored = true;
            foreach (var (source, target, hadOld) in Enumerable.Reverse(moved))
            {
                try
                {
                    if (!File.Exists(source) && !Directory.Exists(source)) MovePath(target, source);
                    if (hadOld) MovePath(target + OldSuffix, target);
                }
                catch (Exception ex)
                {
                    restored = false;
                    Log($"Update rollback: {ex.Message}");
                }
            }
            if (restored) File.Delete(pending);
            throw;
        }
    }

    private static void StartAndWaitForLauncher(string executable, string marker)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        start.Environment[StartupMarkerVariable] = marker;
        using var process = Process.Start(start) ?? throw new IOException("The updated launcher could not be started.");
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < TimeSpan.FromSeconds(30))
        {
            if (process.WaitForExit(100))
                throw new IOException($"The updated launcher exited during startup ({process.ExitCode}).");
            if (File.Exists(marker))
                return;
        }
        try { process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) when (process.HasExited) { }
        process.WaitForExit();
        throw new IOException("The updated launcher did not finish startup in time.");
    }

    /// <summary>At startup: removes what a finished update left behind (old files, the staging folder).</summary>
    public static void CleanupAfterUpdate(string location = null)
    {
        try
        {
            string marker = Environment.GetEnvironmentVariable(StartupMarkerVariable);
            if (marker != null)
            {
                File.WriteAllText(marker, "");
                Environment.SetEnvironmentVariable(StartupMarkerVariable, null);
                return;
            }
            location ??= InstalledLocation;
            bool windows = OperatingSystem.IsWindows();
            if (!windows && !(OperatingSystem.IsMacOS() && location.EndsWith(".app", StringComparison.Ordinal))) return;
            string staging = Path.Combine(windows ? location : Path.GetDirectoryName(location)!, ".sixguns-update");
            if (File.Exists(Path.Combine(staging, SwapPending)) && !File.Exists(Path.Combine(staging, StartupMarker))) return;
            if (windows)
                foreach (string old in Directory.EnumerateFileSystemEntries(location, "*" + OldSuffix)) DeletePath(old);
            DeletePath(staging);
        }
        catch (Exception ex)
        {
            Log($"Update cleanup: {ex.Message}");
        }
    }

    private static void MovePath(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to);
        else File.Move(from, to);
    }

    private static void DeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException) { /* still in use (old exe of the previous run); removed next time */ }
        catch (UnauthorizedAccessException) { }
    }
}
