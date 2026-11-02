using System.Collections.Immutable;
using Tactics.Logic;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.World;

/// <summary>Regions made for a test, with one battle map stamped in them, built as the server builds the real one.</summary>
internal static class StageWorld
{
    public static readonly ImmutableArray<string> Materials = ["grass/lush", "rock/grey", "water/clear", "dirt/brown"];
    public const byte Grass = 0, Rock = 1, Water = 2, Dirt = 3;

    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones = ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    /// <summary>The real Ashen Ford with River Crossing on it.</summary>
    public static (WorldGround Ground, BattleStage Stage, BattleMap Map) Ford()
    {
        var map = Pack.Map("river_crossing");
        return (Pack.Ground("ashen_ford"), BattleStage.Of(Pack.Region("ashen_ford"), map)!.Value, map);
    }

    /// <summary>A map of <see cref="Materials"/>, each tile from <paramref name="tile"/>.</summary>
    public static BattleMap Map(string id, int width, int depth, Func<int, int, TileInfo> tile, params PropPlacement[] props)
    {
        var bytes = new byte[width * depth * 3];
        for (var y = 0; y < depth; y++)
            for (var x = 0; x < width; x++)
            {
                var info = tile(x, y);
                (bytes[(y * width + x) * 3], bytes[(y * width + x) * 3 + 1], bytes[(y * width + x) * 3 + 2]) = ((byte)info.Height, (byte)info.Material, (byte)info.Flags);
            }
        return BattleMap.FromBytes(id, width, depth, bytes, Materials, [.. props], NoZones);
    }

    /// <summary>
    /// A region of <paramref name="width"/> by <paramref name="depth"/>, each tile from <paramref name="tile"/>, with
    /// <paramref name="map"/> stamped at <paramref name="at"/>, raised <paramref name="raise"/> levels. The stamp's tiles are the map's, as the loader insists.
    /// </summary>
    public static (WorldGround Ground, BattleStage Stage) Make(int width, int depth, Func<int, int, TileInfo> tile, BattleMap map, Tile at, int raise)
    {
        var bytes = new byte[width * depth * 3];
        for (var y = 0; y < depth; y++)
            for (var x = 0; x < width; x++)
            {
                var local = new Tile(x - at.X, y - at.Y);
                var info = map.InBounds(local) ? map[local] with { Height = map[local].Height + raise } : tile(x, y);
                (bytes[(y * width + x) * 3], bytes[(y * width + x) * 3 + 1], bytes[(y * width + x) * 3 + 2]) = ((byte)info.Height, (byte)info.Material, (byte)info.Flags);
            }

        var size = WorldRegion.ChunkSize;
        var chunks = ImmutableArray.CreateBuilder<RegionChunk>();
        for (var row = 0; row * size < depth; row++)
            for (var column = 0; column * size < width; column++)
            {
                var chunk = new byte[WorldRegion.ChunkBytes];
                var any = false;
                for (var y = 0; y < size; y++)
                    for (var x = 0; x < size; x++)
                    {
                        var (wx, wy) = (column * size + x, row * size + y);
                        if (wx >= width || wy >= depth)
                            continue;
                        any = true;
                        bytes.AsSpan((wy * width + wx) * 3, 3).CopyTo(chunk.AsSpan((y * size + x) * 3, 3));
                    }
                if (any)
                    chunks.Add(new RegionChunk(column, row, Convert.ToBase64String(chunk)));
            }

        var def = new RegionDef("test_region", "Test region", 1, [width, depth], Materials, chunks.ToImmutable(), [new RegionStamp(map.Id, at, raise)], [],
            new RegionSpawns(new Tile(0, 0), new Tile(0, 0)), []);
        var region = WorldRegion.From(def);
        return (WorldGround.Of(region, new Dictionary<string, BattleMap> { [map.Id] = map }, ArtManifest.None), BattleStage.Of(region, map)!.Value);
    }

    public static TileInfo Flat(int height, byte material = Grass, TileFlags flags = TileFlags.None) => new(height, material, flags);
}
