using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ArcBox.Host.Core;

/// <summary>A downloaded pack isn't the one the server promised: a hash mismatch, a pack the index doesn't list, or a path/id that isn't safe to write.</summary>
public sealed class ContentIntegrityException(string message) : Exception(message);

/// <summary>What one install of a pack version cost: how much came over the network and how much was copied from disk.</summary>
/// <param name="IndexBytes">The content index's body.</param>
/// <param name="FetchedBytes">Pack files' bodies downloaded; with <paramref name="IndexBytes"/>, all this install fetched.</param>
/// <param name="ReusedFiles">Files copied from an older installed version of the pack, each one's SHA-256 checked.</param>
/// <param name="Evicted">Versions deleted afterwards, as <c>gameId/packId/hash</c>.</param>
public sealed record ContentInstall(
    string GameId, string PackId, string Hash, int Files, long Bytes, long IndexBytes,
    int FetchedFiles, long FetchedBytes, int ReusedFiles, long ReusedBytes, IReadOnlyList<string> Evicted);

/// <summary>
/// The TV's on-disk cache of game content packs (spec §3.6). <see cref="EnsureAsync"/> builds a pack version in
/// <c>&lt;root&gt;/.tmp-&lt;guid&gt;/</c>, verifies every file's SHA-256 and the pack hash, then renames the directory to
/// <c>&lt;root&gt;/&lt;gameId&gt;/&lt;packId&gt;/&lt;hash&gt;/</c>, so a partial or tampered download is never readable.
/// </summary>
/// <remarks>
/// A file an older installed version of the pack already has, at the same path with the SHA-256 the index lists, is
/// copied from disk rather than downloaded (<see cref="ContentPlan.Sources"/>), so a new export fetches only the files it
/// changed, plus the index (the user's decision 7). That needs nothing new from the server: its index has always listed
/// each file's SHA-256, and the URLs are the same. Old versions are evicted by <see cref="ContentPlan.Evict"/>.
/// </remarks>
/// <param name="root">The cache directory; created on demand.</param>
/// <param name="http">Its <see cref="HttpClient.BaseAddress"/> is the server.</param>
/// <param name="maxBytes">The cache's target size; see <see cref="ContentPlan.Evict"/>.</param>
/// <param name="installed">Told about each install, on the thread that did it.</param>
public sealed class ContentCache(
    string root, HttpClient http, long maxBytes = ContentCache.DefaultMaxBytes, Action<ContentInstall>? installed = null)
{
    /// <summary>How many hashes of one pack may stay on disk (the newest by directory write time).</summary>
    public const int KeepHashes = 3;

    /// <summary>1 GiB: several versions of the 127 MB Tactics pack.</summary>
    public const long DefaultMaxBytes = 1L << 30;

    private const string TempPrefix = ".tmp-";

    private static readonly JsonSerializerOptions IndexJson = new(JsonSerializerDefaults.Web);

    /// <summary>True only for a fully installed pack; a <c>.tmp-*</c> download in progress or left by a crash never counts.</summary>
    public bool IsCached(string gameId, string packId, string hash) =>
        IsSafeSegment(gameId) && IsSafeSegment(packId) && IsSafeSegment(hash) && Directory.Exists(PackDirectory(gameId, packId, hash));

    /// <summary>
    /// Local directory of the pack; builds, verifies and atomically installs it when missing, downloading only the files
    /// no installed version of the pack has. A cached pack makes no requests and is marked as just used.
    /// </summary>
    /// <param name="progress">Bytes downloaded or copied / total bytes, rising to exactly 1 once the pack is verified.</param>
    /// <exception cref="ContentIntegrityException">The index doesn't list the pack, an id or path is unsafe, or a hash doesn't match.</exception>
    public async Task<string> EnsureAsync(string gameId, string packId, string hash, IProgress<double>? progress, CancellationToken ct)
    {
        foreach (var (name, value) in new[] { ("game id", gameId), ("pack id", packId), ("hash", hash) })
        {
            if (!IsSafeSegment(value))
            {
                throw new ContentIntegrityException($"Refusing content {name} '{value}': it isn't a plain name.");
            }
        }

        var target = PackDirectory(gameId, packId, hash);
        if (Directory.Exists(target))
        {
            Touch(target);   // the most recently used, for eviction
            return target;
        }

        var (pack, indexBytes) = await FindPackAsync(gameId, packId, hash, ct);
        var temp = Path.Combine(root, TempPrefix + Guid.NewGuid().ToString("N"));
        Tally tally;
        try
        {
            Directory.CreateDirectory(temp);
            tally = await BuildAsync(gameId, pack, temp, Installed(gameId, packId), progress, ct);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                Directory.Move(temp, target);
            }
            catch (IOException) when (Directory.Exists(target))
            {
                // Another EnsureAsync for the same pack installed it first; the content is identical.
            }
        }
        finally
        {
            DeleteQuietly(temp);
        }

        progress?.Report(1);
        Touch(target);
        var evicted = Evict(new InstalledVersion(gameId, packId, hash, DateTime.UtcNow, tally.Bytes));
        installed?.Invoke(new ContentInstall(
            gameId, packId, hash, tally.Files, tally.Bytes, indexBytes,
            tally.FetchedFiles, tally.FetchedBytes, tally.ReusedFiles, tally.ReusedBytes, evicted));
        return target;
    }

    private string PackDirectory(string gameId, string packId, string hash) => Path.Combine(root, gameId, packId, hash);

    private async Task<(IndexPack Pack, long Bytes)> FindPackAsync(string gameId, string packId, string hash, CancellationToken ct)
    {
        var url = new Uri($"content/{Uri.EscapeDataString(gameId)}/index.json", UriKind.Relative);
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        var packs = JsonSerializer.Deserialize<List<IndexPack>>(body, IndexJson) ?? [];

        var pack = packs.FirstOrDefault(p => p.PackId == packId && p.Hash == hash)
            ?? throw new ContentIntegrityException(
                $"The server's content index for '{gameId}' doesn't list pack '{packId}' with hash {hash}; it may have changed since the host was told.");
        return (pack, body.LongLength);
    }

    /// <summary>The pack's installed versions with their files, newest first; one that can't be listed is left out.</summary>
    private List<InstalledFiles> Installed(string gameId, string packId)
    {
        var packDir = new DirectoryInfo(Path.Combine(root, gameId, packId));
        if (!packDir.Exists)
        {
            return [];
        }

        var found = new List<(DateTime Used, InstalledFiles Files)>();
        try
        {
            foreach (var version in packDir.EnumerateDirectories())
            {
                try
                {
                    var files = version.EnumerateFiles("*", SearchOption.AllDirectories).ToDictionary(
                        f => Path.GetRelativePath(version.FullName, f.FullName).Replace('\\', '/'), f => f.Length, StringComparer.Ordinal);
                    found.Add((version.LastWriteTimeUtc, new InstalledFiles(version.Name, files)));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Being deleted by another install's eviction, say: its files are downloaded instead.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
        return [.. found.OrderByDescending(f => f.Used).ThenBy(f => f.Files.Hash, StringComparer.Ordinal).Select(f => f.Files)];
    }

    private sealed class Tally
    {
        public int Files;
        public long Bytes;
        public int FetchedFiles;
        public long FetchedBytes;
        public int ReusedFiles;
        public long ReusedBytes;
    }

    /// <summary>Fills <paramref name="temp"/> with the pack's files, each copied from an installed version or downloaded, and verifies them all.</summary>
    private async Task<Tally> BuildAsync(
        string gameId, IndexPack pack, string temp, IReadOnlyList<InstalledFiles> installedVersions, IProgress<double>? progress, CancellationToken ct)
    {
        var files = (pack.Files ?? []).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        if (files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != files.Count)
        {
            throw new ContentIntegrityException($"Pack '{pack.PackId}' lists a path twice.");
        }

        var sources = ContentPlan.Sources([.. files.Select(f => new PackFile(f.Path, f.Sha256, f.Bytes))], installedVersions);
        var tally = new Tally { Files = files.Count, Bytes = files.Sum(f => f.Bytes) };
        long done = 0;
        var packText = new StringBuilder();
        var buffer = new byte[81920];

        // The last 1 is reported once the pack hash has checked out, so 1 means "verified".
        void Advance(long bytes)
        {
            done += bytes;
            progress?.Report(tally.Bytes > 0 ? Math.Min((double)done / tally.Bytes, 0.999999) : 0);
        }

        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var local = LocalPath(temp, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);

            string? actual = null;
            foreach (var hash in sources[i])
            {
                var copied = await TryCopyAsync(LocalPath(PackDirectory(gameId, pack.PackId, hash), file.Path), local, buffer, ct);
                if (string.Equals(copied, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    actual = copied;
                    tally.ReusedFiles++;
                    tally.ReusedBytes += file.Bytes;
                    Advance(file.Bytes);
                    break;
                }
                DeleteFileQuietly(local);   // edited or damaged on disk: try the next version, then the server
            }

            if (actual is null)
            {
                actual = await DownloadAsync(gameId, pack, file, local, buffer, read =>
                {
                    tally.FetchedBytes += read;
                    Advance(read);
                }, ct);
                tally.FetchedFiles++;
                if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ContentIntegrityException($"File '{file.Path}' of pack '{pack.PackId}' doesn't match its SHA-256 (expected {file.Sha256}, got {actual}).");
                }
            }
            packText.Append(file.Path).Append('\n').Append(actual).Append('\n');
        }

        var packHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packText.ToString())));
        if (packHash != pack.Hash)
        {
            throw new ContentIntegrityException($"Pack '{pack.PackId}' doesn't match its hash (expected {pack.Hash}, computed {packHash} from its files).");
        }
        return tally;
    }

    /// <summary>Downloads one file of the pack to <paramref name="local"/>; returns its SHA-256.</summary>
    private async Task<string> DownloadAsync(
        string gameId, IndexPack pack, IndexFile file, string local, byte[] buffer, Action<int> read, CancellationToken ct)
    {
        var url = new Uri(
            $"content/{Uri.EscapeDataString(gameId)}/{Uri.EscapeDataString(pack.PackId)}/{Uri.EscapeDataString(pack.Hash)}/"
            + string.Join('/', file.Path.Split('/').Select(Uri.EscapeDataString)),
            UriKind.Relative);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
        {
            int count;
            while ((count = await source.ReadAsync(buffer, ct)) > 0)
            {
                sha.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), ct);
                read(count);
            }
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }

    /// <summary>Copies an installed file to <paramref name="local"/> and returns the copy's SHA-256; null if it couldn't be read.</summary>
    private static async Task<string?> TryCopyAsync(string installedFile, string local, byte[] buffer, CancellationToken ct)
    {
        try
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = new FileStream(installedFile, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, buffer.Length, useAsync: true))
            await using (var output = new FileStream(local, FileMode.CreateNew, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true))
            {
                int count;
                while ((count = await source.ReadAsync(buffer, ct)) > 0)
                {
                    sha.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            return Convert.ToHexStringLower(sha.GetHashAndReset());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;   // gone (evicted meanwhile) or unreadable: it's downloaded instead
        }
    }

    /// <summary>The file's path under <paramref name="directory"/>; refuses anything that isn't a plain relative path inside it.</summary>
    private static string LocalPath(string directory, string relative)
    {
        var segments = relative.Split('/');
        if (segments.Any(s => s is "" or "." or ".." || s.Contains('\\') || s.Contains('\0') || s.Contains(':')))
        {
            throw new ContentIntegrityException($"Refusing file path '{relative}': it isn't a plain relative path.");
        }

        var full = Path.GetFullPath(Path.Combine([directory, .. segments]));
        var prefix = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.Ordinal)
            ? full
            : throw new ContentIntegrityException($"Refusing file path '{relative}': it leaves the pack directory.");
    }

    /// <summary>A single, ordinary directory name: no separators, no dot names, nothing that could look like a temp directory.</summary>
    private static bool IsSafeSegment(string value) =>
        value.Length > 0
        && value[0] != '.'
        && value.IndexOfAny(['/', '\\', ':', '\0']) < 0
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Marks the version as just installed or used, so eviction keeps it longest, whatever its write time was.</summary>
    private static void Touch(string directory)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Eviction then just sees an older time; the install itself is done.
        }
    }

    /// <summary>Deletes what <see cref="ContentPlan.Evict"/> picks, across every game and pack; returns what it deleted.</summary>
    private List<string> Evict(InstalledVersion current)
    {
        var versions = new List<InstalledVersion>();
        try
        {
            foreach (var game in new DirectoryInfo(root).EnumerateDirectories().Where(d => IsSafeSegment(d.Name)))
            {
                foreach (var pack in game.EnumerateDirectories())
                {
                    foreach (var version in pack.EnumerateDirectories())
                    {
                        try
                        {
                            var bytes = version.EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
                            versions.Add(new InstalledVersion(game.Name, pack.Name, version.Name, version.LastWriteTimeUtc, bytes));
                        }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                        {
                            // Already being deleted.
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];   // the install is done; eviction waits for the next one
        }

        var deleted = new List<string>();
        foreach (var old in ContentPlan.Evict(versions, current, KeepHashes, maxBytes))
        {
            DeleteQuietly(PackDirectory(old.GameId, old.PackId, old.Hash));
            deleted.Add($"{old.GameId}/{old.PackId}/{old.Hash}");
        }
        return deleted;
    }

    private static void DeleteFileQuietly(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Then the next copy's or the download's CreateNew fails, and the install with it; the temp directory goes anyway.
        }
    }

    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover directory is harmless: .tmp-* never counts as installed, and an old hash is evicted next time.
        }
    }

    private sealed record IndexPack(string PackId, string Hash, List<IndexFile>? Files);

    private sealed record IndexFile(string Path, string Sha256, long Bytes);
}
