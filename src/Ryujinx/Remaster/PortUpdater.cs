using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Ryujinx.Ava.Remaster;

/// <summary>
/// Keeps the desktop port identical to the Switch one: the launcher runs the sixguns.nro of the newest SixGuns-NX
/// release, the same file a Switch downloads with its own updater, without waiting for a launcher release.
/// The release's sixguns.nro is downloaded into a cache folder and checked against the SHA-256 GitHub lists for it
/// and the NRO header. It is used only when the release is newer than the port bundled with this launcher (the
/// bundled commit's date, second line of sd-builder/sixguns-nx.ref), so a launcher never goes back to an older port.
/// Only the executable changes; game data, saves and mods stay (a Switch update swaps only sixguns.nro too).
/// Offline or on any error the newest port already here is used.
/// </summary>
public static class PortUpdater
{
    private const string Repository = "jorgechavezxyz/SixGuns-NX";
    private const string Asset = "sixguns.nro";
    private const string CachedNro = "sixguns.nro";
    private const string CachedInfo = "release.json";

    public sealed record Release(string Tag, DateTimeOffset Published, string Sha256, string Url, long Size);

    private static readonly HttpClient Http = CreateClient();

    public static Action<string> Log = _ => { };

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SixGunsRemasterNX-Port");
        return client;
    }

    /// <summary>When the bundled port was committed (sixguns-nx.ref: commit, ISO date, tag), or MinValue.</summary>
    public static DateTimeOffset BundledDate(string builder)
    {
        try
        {
            string reference = Path.Combine(Path.GetDirectoryName(builder)!, "sixguns-nx.ref");
            string[] lines = File.Exists(reference) ? File.ReadAllLines(reference) : Array.Empty<string>();
            return lines.Length > 1 && DateTimeOffset.TryParse(lines[1].Trim(), out var date) ? date : DateTimeOffset.MinValue;
        }
        catch (Exception) { return DateTimeOffset.MinValue; }
    }

    /// <summary>The newest non-draft SixGuns-NX release that carries a sixguns.nro with a SHA-256 digest.</summary>
    public static async Task<Release> LatestAsync(CancellationToken token)
    {
        // SIXGUNS_PORT_FEED: a releases list in GitHub's format at another address (tests only).
        string feed = Environment.GetEnvironmentVariable("SIXGUNS_PORT_FEED") ?? $"https://api.github.com/repos/{Repository}/releases?per_page=5";
        using var releases = JsonDocument.Parse(await Http.GetStringAsync(feed, token));
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (!release.TryGetProperty("published_at", out var published) || published.ValueKind != JsonValueKind.String) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != Asset) continue;
                string digest = asset.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String ? d.GetString() : null;
                if (digest == null || !digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 7 + 64) continue;
                return new Release(release.GetProperty("tag_name").GetString(), published.GetDateTimeOffset(), digest[7..].ToLowerInvariant(),
                    asset.GetProperty("browser_download_url").GetString(), asset.GetProperty("size").GetInt64());
            }
        }
        return null;
    }

    /// <summary>Downloads the newest release's port into <paramref name="cache"/> when it is newer than the bundled
    /// one and not there yet. Returns the release now in the cache (null when the bundled port is the newest).</summary>
    public static async Task<Release> RefreshAsync(string cache, string bundledNro, string builder, Action<string> report, CancellationToken token)
    {
        var release = await LatestAsync(token);
        if (release == null || release.Published <= BundledDate(builder) || release.Sha256 == Hash(bundledNro)) return null;
        if (Cached(cache) is { } have && have.Sha256 == release.Sha256) return have;
        if (release.Size <= 0 || release.Size > 64L * 1024 * 1024) throw new InvalidDataException("The port release has an unexpected size.");
        Directory.CreateDirectory(cache);
        string part = Path.Combine(cache, CachedNro + ".part");
        try
        {
            report($"Getting the latest Six Guns port ({release.Tag})…");
            using (var response = await Http.GetAsync(release.Url, HttpCompletionOption.ResponseHeadersRead, token))
            {
                response.EnsureSuccessStatusCode();
                await using var input = await response.Content.ReadAsStreamAsync(token);
                await using var output = File.Create(part);
                await input.CopyToAsync(output, token);
            }
            if (new FileInfo(part).Length != release.Size || Hash(part) != release.Sha256)
                throw new InvalidDataException("The downloaded port is damaged or incomplete.");
            GameInstaller.ValidatePort(part);
            // Info last: a cache without matching info is ignored, so an interrupted swap never selects a bad file.
            File.Delete(Path.Combine(cache, CachedInfo));
            File.Move(part, Path.Combine(cache, CachedNro), true);
            File.WriteAllText(Path.Combine(cache, CachedInfo), JsonSerializer.Serialize(new
            {
                tag = release.Tag, published = release.Published.ToString("o"), sha256 = release.Sha256,
            }));
            Log($"Port {release.Tag} ({release.Sha256[..16]}) downloaded.");
            return release;
        }
        finally
        {
            try { File.Delete(part); } catch (IOException) { }
        }
    }

    /// <summary>The cached release, when its file is intact.</summary>
    public static Release Cached(string cache)
    {
        try
        {
            string info = Path.Combine(cache, CachedInfo), nro = Path.Combine(cache, CachedNro);
            if (!File.Exists(info) || !File.Exists(nro)) return null;
            using var json = JsonDocument.Parse(File.ReadAllText(info));
            var root = json.RootElement;
            string sha = root.GetProperty("sha256").GetString();
            if (Hash(nro) != sha) return null;
            return new Release(root.GetProperty("tag").GetString(), DateTimeOffset.Parse(root.GetProperty("published").GetString()), sha, null,
                new FileInfo(nro).Length);
        }
        catch (Exception) { return null; }
    }

    /// <summary>The port to run: the cached release when it is intact and newer than the bundled port, else the
    /// bundled one.</summary>
    public static (string Path, string Tag) Choose(string cache, string bundledNro, string builder)
    {
        if (Cached(cache) is { } cached && cached.Published > BundledDate(builder))
        {
            try
            {
                string nro = Path.Combine(cache, CachedNro);
                GameInstaller.ValidatePort(nro);
                return (nro, cached.Tag);
            }
            catch (Exception ex) { Log("Cached port rejected: " + ex.Message); }
        }
        return (bundledNro, null);
    }

    private static string Hash(string file)
    {
        if (!File.Exists(file)) return "";
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
