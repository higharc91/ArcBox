using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Content;

/// <param name="Path">Relative to the pack directory, with '/' separators.</param>
/// <param name="Sha256">Lowercase hex.</param>
public sealed record ContentFile(string Path, string Sha256, long Bytes);

/// <param name="Hash">
/// Lowercase-hex SHA-256 of the UTF-8 text <c>{path}\n{fileSha}\n</c> concatenated over the files in ordinal path order,
/// so any change to a file's name or bytes makes a new hash (and a new, immutable URL).
/// </param>
/// <param name="Directory">The pack's full path on disk.</param>
public sealed record ContentPack(string GameId, string PackId, string Hash, string Directory, IReadOnlyList<ContentFile> Files);

/// <summary>
/// The content packs under <c>&lt;Root&gt;/&lt;gameId&gt;/&lt;packId&gt;/</c>, scanned and hashed once at construction
/// (spec §3.6). Nothing here touches the disk after that, and requests only ever name packs and files this registry
/// found, so a URL can't reach anything else.
/// </summary>
public sealed class ContentPackRegistry
{
    private readonly Dictionary<string, IReadOnlyList<ContentPack>> _byGame;

    /// <summary>Scans <paramref name="root"/>, resolved against the content root when relative.</summary>
    public ContentPackRegistry(IOptions<ContentOptions> options, IHostEnvironment environment)
        : this(System.IO.Path.GetFullPath(options.Value.Root, environment.ContentRootPath))
    {
    }

    /// <summary>Scans the absolute directory <paramref name="root"/>; a missing one gives an empty registry.</summary>
    public ContentPackRegistry(string root) : this(Scan(root))
    {
    }

    /// <summary>A registry of exactly these packs.</summary>
    public ContentPackRegistry(IEnumerable<ContentPack> packs) =>
        _byGame = packs
            .GroupBy(p => p.GameId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<ContentPack>)[.. g.OrderBy(p => p.PackId, StringComparer.Ordinal)],
                StringComparer.Ordinal);

    /// <summary>A registry with no packs.</summary>
    public static ContentPackRegistry Empty => new([]);

    /// <summary>The game's packs in pack id order; empty if it has none.</summary>
    public IReadOnlyList<ContentPack> For(string gameId) =>
        _byGame.TryGetValue(gameId, out var packs) ? packs : [];

    /// <summary>The pack, only if <paramref name="hash"/> is its current hash.</summary>
    public ContentPack? Find(string gameId, string packId, string hash) =>
        For(gameId).FirstOrDefault(p => p.PackId == packId && p.Hash == hash);

    private static IEnumerable<ContentPack> Scan(string root)
    {
        if (!Directory.Exists(root)) yield break;

        foreach (var gameDir in Directory.EnumerateDirectories(root))
        {
            foreach (var packDir in Directory.EnumerateDirectories(gameDir))
            {
                yield return Load(
                    System.IO.Path.GetFileName(gameDir), System.IO.Path.GetFileName(packDir), packDir);
            }
        }
    }

    private static ContentPack Load(string gameId, string packId, string directory)
    {
        var files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(full =>
            {
                var relative = System.IO.Path.GetRelativePath(directory, full).Replace('\\', '/');
                using var stream = File.OpenRead(full);
                return new ContentFile(relative, Convert.ToHexStringLower(SHA256.HashData(stream)), new FileInfo(full).Length);
            })
            .OrderBy(f => f.Path, StringComparer.Ordinal)
            .ToList();

        var text = new StringBuilder();
        foreach (var file in files)
        {
            text.Append(file.Path).Append('\n').Append(file.Sha256).Append('\n');
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
        return new ContentPack(gameId, packId, hash, directory, files);
    }
}
