using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A loaded region of the overworld: its definition, with the chunks decoded once for tile lookups. Its tiles are the same
/// <see cref="TileInfo"/> a <see cref="BattleMap"/> has, with <see cref="TileInfo.Material"/> indexing
/// <see cref="RegionDef.Materials"/>. A tile in no chunk, or outside the region's size, is void.
/// </summary>
public sealed class WorldRegion
{
    /// <summary>The tiles along one side of a chunk.</summary>
    public const int ChunkSize = 32;

    /// <summary>The bytes of a decoded chunk: three per tile.</summary>
    public const int ChunkBytes = ChunkSize * ChunkSize * 3;

    /// <summary>The highest a tile can be, as on a battle map.</summary>
    public const int MaxHeight = 9;

    private readonly Dictionary<(int X, int Y), TileInfo[]> _chunks;
    private readonly int _width;
    private readonly int _height;

    private WorldRegion(RegionDef def, int width, int height, Dictionary<(int X, int Y), TileInfo[]> chunks)
    {
        Def = def;
        _width = width;
        _height = height;
        _chunks = chunks;
    }

    /// <summary>The region as written.</summary>
    public RegionDef Def { get; }

    /// <summary>
    /// Builds the region, decoding every chunk. Throws <see cref="ArgumentException"/> on a bad size, a repeated chunk or bad
    /// chunk bytes; the loader reports those as content errors first. Only the loader checks that chunks are inside the region.
    /// </summary>
    public static WorldRegion From(RegionDef def)
    {
        if (def.Size is not [var width and > 0, var height and > 0])
            throw new ArgumentException($"Region '{def.Id}' needs a size of [w, h].", nameof(def));

        var chunks = new Dictionary<(int X, int Y), TileInfo[]>();
        foreach (var chunk in def.Chunks)
        {
            var tiles = Decode(chunk, def.Materials.Length, out var problems)
                ?? throw new ArgumentException($"Region '{def.Id}', chunk ({chunk.X}, {chunk.Y}): {problems[0]}", nameof(def));
            if (!chunks.TryAdd((chunk.X, chunk.Y), tiles))
                throw new ArgumentException($"Region '{def.Id}' lists chunk ({chunk.X}, {chunk.Y}) twice.", nameof(def));
        }
        return new WorldRegion(def, width, height, chunks);
    }

    /// <summary>The tile's info, or null for void.</summary>
    public TileInfo? TileAt(Tile t)
    {
        if (t.X < 0 || t.Y < 0 || t.X >= _width || t.Y >= _height)
            return null;
        return _chunks.TryGetValue((t.X / ChunkSize, t.Y / ChunkSize), out var tiles)
            ? tiles[t.Y % ChunkSize * ChunkSize + t.X % ChunkSize]
            : null;
    }

    /// <summary>True when the party may walk on the tile: not void, not impassable, not deep, as on a battle map.</summary>
    public bool Walkable(Tile t) => TileAt(t) is { } tile && (tile.Flags & (TileFlags.Impassable | TileFlags.Deep)) == 0;

    /// <summary>
    /// Decodes one chunk for <paramref name="materials"/> listed materials, or returns null with what's wrong with it. The first
    /// bad tile of each kind is enough to find the problem, as on a battle map: one problem per tile could be thousands.
    /// </summary>
    internal static TileInfo[]? Decode(RegionChunk chunk, int materials, out ImmutableArray<string> problems)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(chunk.Tiles);
        }
        catch (FormatException)
        {
            problems = ["The tiles aren't valid base64."];
            return null;
        }
        if (bytes.Length != ChunkBytes)
        {
            problems = [$"Expected {ChunkBytes} tile bytes for a chunk ({ChunkSize}x{ChunkSize}, 3 per tile), got {bytes.Length}."];
            return null;
        }

        const int knownFlags = (int)(TileFlags.Water | TileFlags.Difficult | TileFlags.Impassable | TileFlags.Hazard | TileFlags.Deep);
        Tile WorldTile(int i) => new(chunk.X * ChunkSize + i % ChunkSize, chunk.Y * ChunkSize + i / ChunkSize);
        var tiles = new TileInfo[ChunkSize * ChunkSize];
        string? height = null, material = null, flags = null;
        for (var i = 0; i < tiles.Length; i++)
        {
            int tileHeight = bytes[i * 3], tileMaterial = bytes[i * 3 + 1], tileFlags = bytes[i * 3 + 2];
            height ??= tileHeight > MaxHeight ? $"Tile {WorldTile(i)} has height {tileHeight}, above the limit of {MaxHeight}." : null;
            material ??= tileMaterial >= materials ? $"Tile {WorldTile(i)} uses material {tileMaterial}, but the region has {materials} materials." : null;
            flags ??= (tileFlags & ~knownFlags) != 0 ? $"Tile {WorldTile(i)} has undefined flag bits 0x{tileFlags & ~knownFlags:x2}." : null;
            tiles[i] = new TileInfo(tileHeight, tileMaterial, (TileFlags)tileFlags);
        }

        problems = [.. new[] { height, material, flags }.OfType<string>()];
        return problems.IsEmpty ? tiles : null;
    }
}
