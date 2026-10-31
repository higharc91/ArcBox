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
    /// Builds the region, decoding every chunk. Throws <see cref="FormatException"/> or <see cref="ArgumentException"/> on bad
    /// tiles, size or a repeated chunk; the loader reports those as content errors first.
    /// </summary>
    public static WorldRegion From(RegionDef def)
    {
        if (def.Size is not [var width and > 0, var height and > 0])
            throw new ArgumentException($"Region '{def.Id}' needs a size of [w, h].", nameof(def));

        var chunks = new Dictionary<(int X, int Y), TileInfo[]>();
        foreach (var chunk in def.Chunks)
            if (!chunks.TryAdd((chunk.X, chunk.Y), Decode(chunk, def)))
                throw new ArgumentException($"Region '{def.Id}' lists chunk ({chunk.X}, {chunk.Y}) twice.", nameof(def));
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

    private static TileInfo[] Decode(RegionChunk chunk, RegionDef def)
    {
        var bytes = Convert.FromBase64String(chunk.Tiles);
        if (bytes.Length != ChunkBytes)
            throw new ArgumentException($"Expected {ChunkBytes} bytes for chunk ({chunk.X}, {chunk.Y}), got {bytes.Length}.", nameof(chunk));

        var tiles = new TileInfo[ChunkSize * ChunkSize];
        for (var i = 0; i < tiles.Length; i++)
        {
            var material = bytes[i * 3 + 1];
            if (material >= def.Materials.Length)
                throw new ArgumentException(
                    $"Chunk ({chunk.X}, {chunk.Y}) uses material {material} but there are {def.Materials.Length} materials.", nameof(chunk));
            tiles[i] = new TileInfo(bytes[i * 3], material, (TileFlags)bytes[i * 3 + 2]);
        }
        return tiles;
    }
}
