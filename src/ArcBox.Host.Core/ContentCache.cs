using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Json;
using System.Text.Json;

namespace ArcBox.Host.Core;

/// <summary>A downloaded pack isn't the one the server promised: a hash mismatch, a pack the index doesn't list, or a path/id that isn't safe to write.</summary>
public sealed class ContentIntegrityException(string message) : Exception(message);

/// <summary>
/// The TV's on-disk cache of game content packs (spec §3.6). <see cref="EnsureAsync"/> downloads a pack into
/// <c>&lt;root&gt;/.tmp-&lt;guid&gt;/</c>, verifies every file's SHA-256 and the pack hash, then renames the directory to
/// <c>&lt;root&gt;/&lt;gameId&gt;/&lt;packId&gt;/&lt;hash&gt;/</c>, so a partial or tampered download is never readable.
/// </summary>
/// <param name="root">The cache directory; created on demand.</param>
/// <param name="http">Its <see cref="HttpClient.BaseAddress"/> is the server.</param>
public sealed class ContentCache(string root, HttpClient http)
{
    /// <summary>How many hashes of one pack stay on disk (the newest by directory write time).</summary>
    public const int KeepHashes = 3;

    private const string TempPrefix = ".tmp-";

    private static readonly JsonSerializerOptions IndexJson = new(JsonSerializerDefaults.Web);

    /// <summary>True only for a fully installed pack; a <c>.tmp-*</c> download in progress or left by a crash never counts.</summary>
    public bool IsCached(string gameId, string packId, string hash) =>
        IsSafeSegment(gameId) && IsSafeSegment(packId) && IsSafeSegment(hash) && Directory.Exists(PackDirectory(gameId, packId, hash));

    /// <summary>Local directory of the pack; downloads, verifies and atomically installs it when missing.</summary>
    /// <param name="progress">Bytes downloaded / total bytes, rising to exactly 1 once the pack is verified.</param>
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
            return target;
        }

        var pack = await FindPackAsync(gameId, packId, hash, ct);
        var temp = Path.Combine(root, TempPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            await DownloadAsync(gameId, pack, temp, progress, ct);

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
        Prune(gameId, packId, hash);
        return target;
    }

    private string PackDirectory(string gameId, string packId, string hash) => Path.Combine(root, gameId, packId, hash);

    private async Task<IndexPack> FindPackAsync(string gameId, string packId, string hash, CancellationToken ct)
    {
        var url = new Uri($"content/{Uri.EscapeDataString(gameId)}/index.json", UriKind.Relative);
        using var response = await http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        var packs = await response.Content.ReadFromJsonAsync<List<IndexPack>>(IndexJson, ct) ?? [];

        return packs.FirstOrDefault(p => p.PackId == packId && p.Hash == hash)
            ?? throw new ContentIntegrityException(
                $"The server's content index for '{gameId}' doesn't list pack '{packId}' with hash {hash}; it may have changed since the host was told.");
    }

    private async Task DownloadAsync(string gameId, IndexPack pack, string temp, IProgress<double>? progress, CancellationToken ct)
    {
        var files = (pack.Files ?? []).OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        if (files.Select(f => f.Path).Distinct(StringComparer.Ordinal).Count() != files.Count)
        {
            throw new ContentIntegrityException($"Pack '{pack.PackId}' lists a path twice.");
        }

        var total = files.Sum(f => f.Bytes);
        long done = 0;
        var packText = new StringBuilder();
        var buffer = new byte[81920];

        foreach (var file in files)
        {
            var local = LocalPath(temp, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(local)!);

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
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    sha.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    // The last 1 is reported once the pack hash has checked out, so 1 means "verified".
                    progress?.Report(total > 0 ? Math.Min((double)done / total, 0.999999) : 0);
                }
            }

            var actual = Convert.ToHexStringLower(sha.GetHashAndReset());
            if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ContentIntegrityException($"File '{file.Path}' of pack '{pack.PackId}' doesn't match its SHA-256 (expected {file.Sha256}, got {actual}).");
            }
            packText.Append(file.Path).Append('\n').Append(actual).Append('\n');
        }

        var packHash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(packText.ToString())));
        if (packHash != pack.Hash)
        {
            throw new ContentIntegrityException($"Pack '{pack.PackId}' doesn't match its hash (expected {pack.Hash}, computed {packHash} from its files).");
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

    /// <summary>The install just happened, so make it the newest hash whatever the temp directory's own write time was.</summary>
    private static void Touch(string directory)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(directory, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pruning then just sees an older time; the install itself is done.
        }
    }

    /// <summary>Deletes this pack's older hashes beyond <see cref="KeepHashes"/>, newest by write time; <paramref name="keep"/> always stays.</summary>
    private void Prune(string gameId, string packId, string keep)
    {
        var old = new DirectoryInfo(Path.Combine(root, gameId, packId))
            .EnumerateDirectories()
            .Where(d => d.Name != keep)
            .OrderByDescending(d => d.LastWriteTimeUtc)
            .Skip(KeepHashes - 1);
        foreach (var dir in old)
        {
            DeleteQuietly(dir.FullName);
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
            // A leftover directory is harmless: .tmp-* never counts as installed, and an old hash is pruned next time.
        }
    }

    private sealed record IndexPack(string PackId, string Hash, List<IndexFile>? Files);

    private sealed record IndexFile(string Path, string Sha256, long Bytes);
}
