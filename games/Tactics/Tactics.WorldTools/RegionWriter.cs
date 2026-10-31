using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>What <see cref="RegionWriter.Write"/> made: a region with no problems, or null and everything that stopped it.</summary>
public sealed record WriteResult(RegionDef? Region, ImmutableArray<string> Problems);

/// <summary>Turns a <see cref="RegionSource"/> into a <see cref="RegionDef"/>.</summary>
public static class RegionWriter
{
    /// <summary>A tile's material is one byte.</summary>
    private const int MaxMaterials = byte.MaxValue + 1;

    private static readonly TileFlags KnownFlags = Enum.GetValues<TileFlags>().Aggregate((all, flag) => all | flag);

    /// <summary>
    /// Writes the region, or says why it can't: a tile outside the region, a height outside 0 to 9, a material that isn't in
    /// <paramref name="knownMaterials"/>, undefined flag bits or too many materials. The first tile of each kind of problem is
    /// enough to find it, and each unknown material is named once. The same grid always gives the same region, however it was
    /// painted: chunks go by row and materials by first use in the bytes as they're read, chunk by chunk. A chunk with nothing
    /// painted is left out, and an unpainted tile in a listed chunk is written <see cref="TileFlags.Impassable"/>, since a blank
    /// tile would be walkable ground. Whether the region's markers can be reached, and whether its stamps still match their maps,
    /// are the pack loader's checks.
    /// </summary>
    public static WriteResult Write(RegionSource source, IReadOnlySet<string> knownMaterials)
    {
        bool Inside(Tile t) => t.X >= 0 && t.Y >= 0 && t.X < source.Width && t.Y < source.Height;

        var tiles = source.Tiles
            .Where(t => Inside(t.Key))
            .OrderBy(t => t.Key.Y / WorldRegion.ChunkSize).ThenBy(t => t.Key.X / WorldRegion.ChunkSize)
            .ThenBy(t => t.Key.Y).ThenBy(t => t.Key.X)
            .ToList();

        var problems = new List<string>();
        if (source.Tiles.Keys.Where(t => !Inside(t)).OrderBy(t => t.Y).ThenBy(t => t.X).Cast<Tile?>().FirstOrDefault() is { } outside)
            problems.Add($"The tile {outside} is outside the {source.Width}x{source.Height} region.");

        var materials = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknown = new HashSet<string>(StringComparer.Ordinal);
        string? height = null, flags = null;
        foreach (var (tile, info) in tiles)
        {
            materials.TryAdd(info.Material, materials.Count);
            if (!knownMaterials.Contains(info.Material) && unknown.Add(info.Material))
                problems.Add($"The tile {tile} uses the material '{info.Material}', which isn't in the terrain manifest.");
            height ??= info.Height is < 0 or > WorldRegion.MaxHeight
                ? $"The tile {tile} has height {info.Height}, but heights go from 0 to {WorldRegion.MaxHeight}."
                : null;
            flags ??= (info.Flags & ~KnownFlags) != 0
                ? $"The tile {tile} has undefined flag bits 0x{(int)(info.Flags & ~KnownFlags):x2}."
                : null;
        }
        problems.AddRange(new[] { height, flags }.OfType<string>());
        if (materials.Count > MaxMaterials)
            problems.Add($"The region uses {materials.Count} materials, but a tile can only index {MaxMaterials}.");
        if (problems.Count > 0)
            return new WriteResult(null, [.. problems]);

        var chunks = tiles.GroupBy(t => (X: t.Key.X / WorldRegion.ChunkSize, Y: t.Key.Y / WorldRegion.ChunkSize))
            .Select(chunk => new RegionChunk(chunk.Key.X, chunk.Key.Y, Convert.ToBase64String(Bytes(chunk, materials))));
        var def = new RegionDef(source.Id, source.Name, source.Chapter, [source.Width, source.Height],
            [.. materials.OrderBy(m => m.Value).Select(m => m.Key)], [.. chunks], [.. source.Stamps], [.. source.Beats],
            new RegionSpawns(source.Start, source.Recovery), []);
        return new WriteResult(def, []);
    }

    /// <summary>One chunk's bytes: every tile impassable, then the painted ones.</summary>
    private static byte[] Bytes(IEnumerable<KeyValuePair<Tile, TileInfoNamed>> tiles, Dictionary<string, int> materials)
    {
        var bytes = new byte[WorldRegion.ChunkBytes];
        for (var i = 2; i < bytes.Length; i += 3)
            bytes[i] = (byte)TileFlags.Impassable;
        foreach (var (tile, info) in tiles)
        {
            var at = (tile.Y % WorldRegion.ChunkSize * WorldRegion.ChunkSize + tile.X % WorldRegion.ChunkSize) * 3;
            (bytes[at], bytes[at + 1], bytes[at + 2]) = ((byte)info.Height, (byte)materials[info.Material], (byte)info.Flags);
        }
        return bytes;
    }
}
