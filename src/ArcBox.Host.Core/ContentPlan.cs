namespace ArcBox.Host.Core;

/// <summary>One file as a pack's index lists it: a '/'-separated path, its lowercase-hex SHA-256 and its length.</summary>
public sealed record PackFile(string Path, string Sha256, long Bytes);

/// <summary>An installed version of a pack, as <see cref="ContentPlan.Evict"/> weighs it.</summary>
/// <param name="LastUsedUtc">When it was last installed or found in the cache (its directory's write time).</param>
/// <param name="Bytes">The total size of its files.</param>
public sealed record InstalledVersion(string GameId, string PackId, string Hash, DateTime LastUsedUtc, long Bytes);

/// <summary>The files of one installed version of a pack: path → length.</summary>
public sealed record InstalledFiles(string Hash, IReadOnlyDictionary<string, long> Files);

/// <summary>
/// The pure decisions of the TV's content cache (spec §3.6; the user's decision 7): which files of a new pack version can be
/// taken from a version already on disk instead of downloaded, and which old versions to delete afterwards.
/// <see cref="ContentCache"/> does the disk and network work.
/// </summary>
public static class ContentPlan
{
    /// <summary>
    /// For each file of <paramref name="wanted"/>, in the same order, the installed versions worth copying it from: those
    /// with a file at the same path and of the same length, in <paramref name="installed"/>'s order (newest first). An
    /// empty list means download it. A candidate is only a guess: the copy is used only if its SHA-256 is the one the
    /// index lists, so an edited, truncated or corrupt local file is downloaded instead.
    /// </summary>
    /// <remarks>
    /// The cache is keyed by (path, SHA-256). A file that moved to a new path is downloaded again; a region export
    /// rewrites files in place, so that costs nothing in practice and needs no record kept beside each version.
    /// </remarks>
    public static IReadOnlyList<IReadOnlyList<string>> Sources(IReadOnlyList<PackFile> wanted, IReadOnlyList<InstalledFiles> installed) =>
        [.. wanted.Select(file => (IReadOnlyList<string>)[.. installed
            .Where(v => v.Files.TryGetValue(file.Path, out var bytes) && bytes == file.Bytes)
            .Select(v => v.Hash)])];

    /// <summary>
    /// The versions to delete once <paramref name="current"/> is installed or used, oldest first. In each pack the newest
    /// <paramref name="keepPerPack"/> versions may stay (<paramref name="current"/> counts as the newest of its pack);
    /// then, while the total is over <paramref name="maxBytes"/>, the least recently used go, except that the newest
    /// version of every pack is never deleted, and nor is the one before <paramref name="current"/> (a running game keeps
    /// the content it started with when a new export arrives). So the limit is a target: it can be exceeded by those.
    /// </summary>
    public static IReadOnlyList<InstalledVersion> Evict(
        IReadOnlyList<InstalledVersion> versions, InstalledVersion current, int keepPerPack, long maxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(keepPerPack, 1);

        var evicted = new List<InstalledVersion>();
        var kept = new List<InstalledVersion>();
        var protectedOnes = new HashSet<InstalledVersion>();
        foreach (var pack in versions.Where(v => !SameVersion(v, current)).Append(current)
                     .GroupBy(v => (v.GameId, v.PackId)))
        {
            var newestFirst = pack.OrderByDescending(v => SameVersion(v, current))
                .ThenByDescending(v => v.LastUsedUtc)
                .ThenBy(v => v.Hash, StringComparer.Ordinal)
                .ToList();
            kept.AddRange(newestFirst.Take(keepPerPack));
            evicted.AddRange(newestFirst.Skip(keepPerPack));

            var guarded = pack.Key == (current.GameId, current.PackId) ? 2 : 1;
            protectedOnes.UnionWith(newestFirst.Take(Math.Min(guarded, keepPerPack)));
        }

        var total = kept.Sum(v => v.Bytes);
        foreach (var version in kept.OrderBy(v => v.LastUsedUtc).ThenBy(v => v.Hash, StringComparer.Ordinal))
        {
            if (total <= maxBytes)
            {
                break;
            }
            if (protectedOnes.Contains(version))
            {
                continue;
            }
            evicted.Add(version);
            total -= version.Bytes;
        }

        return [.. evicted.OrderBy(v => v.LastUsedUtc).ThenBy(v => v.Hash, StringComparer.Ordinal)];
    }

    private static bool SameVersion(InstalledVersion a, InstalledVersion b) =>
        a.GameId == b.GameId && a.PackId == b.PackId && a.Hash == b.Hash;
}
