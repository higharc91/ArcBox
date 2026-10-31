using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>Reads a map file (<c>rules/maps/&lt;id&gt;.json</c>).</summary>
public static class BoardMap
{
    /// <summary>The map in <paramref name="json"/>, decoded by the engine. Throws on a malformed file.</summary>
    public static BattleMap Parse(string json)
        => (JsonSerializer.Deserialize<MapDef>(json, ArcJson.Options) ?? throw new FormatException("A map file can't be null."))
            .ToBattleMap();
}

/// <summary>
/// The map as the board draws it now: the map file plus what the battle changed, with each tile's look and the height its
/// top is drawn at. That height isn't always the rules' (a void is drawn sunk), so anything standing on a tile takes its
/// place from <see cref="Centre"/>.
/// </summary>
public sealed class BoardTerrain
{
    /// <summary>How far a void is drawn below its height, in steps, so the chasm walls show (dungeon/manifest.json).</summary>
    public const float VoidSink = 2;

    /// <summary>How far deep water is drawn below its banks, in steps.</summary>
    public const float WaterSink = 0.25f;

    /// <summary>How thick a bridge's deck is, in steps.</summary>
    public const float DeckDepth = 0.5f;

    /// <summary>How far an impassable tile is drawn above its height, in steps, so a wall reads as a wall and not a ledge.</summary>
    public const float WallRise = 1;

    private readonly MaterialLook[] _looks;
    private readonly float[] _tops;
    private readonly bool[] _deep;

    /// <summary>The board for <paramref name="map"/> with <paramref name="changes"/> (null: none) applied.</summary>
    public BoardTerrain(BattleMap map, BoardChanges? changes)
    {
        Map = map;
        Changes = changes ?? BoardChanges.None;
        var heights = Changes.Heights.ToImmutableSortedDictionary(h => h.At, h => h.Delta);
        var sunk = Changes.Sunk.ToImmutableSortedSet();
        var terrain = new Terrain(map, heights, [], sunk);
        var (width, count) = (map.Width, map.Width * map.Height);
        _looks = new MaterialLook[count];
        _tops = new float[count];
        _deep = new bool[count];

        var chasm = MaterialLook.Of(ChasmMaterial(map));
        var looks = map.Materials.Select(MaterialLook.Of).ToArray();
        var lowest = int.MaxValue;
        foreach (var tile in Tiles)
        {
            var i = tile.Y * width + tile.X;
            var height = terrain.HeightAt(tile);
            var deep = terrain.Deep(tile);
            lowest = Math.Min(lowest, height);
            _looks[i] = sunk.Contains(tile) ? chasm : looks[map[tile].Material];
            _deep[i] = deep;
            _tops[i] = height + (_looks[i].Void ? -VoidSink
                : _looks[i].Water && deep ? -WaterSink
                : map[tile].Flags.HasFlag(TileFlags.Impassable) ? WallRise
                : 0);
        }
        Base = lowest - 1;
    }

    /// <summary>The map file.</summary>
    public BattleMap Map { get; }

    /// <summary>What the battle changed.</summary>
    public BoardChanges Changes { get; }

    /// <summary>The height the board's outer skirt goes down to, in steps.</summary>
    public float Base { get; }

    /// <summary>Every tile, row by row.</summary>
    public IEnumerable<Tile> Tiles => Enumerable.Range(0, Map.Height).SelectMany(y => Enumerable.Range(0, Map.Width).Select(x => new Tile(x, y)));

    /// <summary>How a tile looks. A tile sunk in the battle takes the look of the map's chasm.</summary>
    public MaterialLook Look(Tile tile) => _looks[Index(tile)];

    /// <summary>True when the tile is deep, on the map or sunk in the battle.</summary>
    public bool Deep(Tile tile) => _deep[Index(tile)];

    /// <summary>The height a tile's top is drawn at, in steps.</summary>
    public float Top(Tile tile) => _tops[Index(tile)];

    /// <summary>The middle of a tile's top, in art pixels: where a prop or a unit stands.</summary>
    public Vector2 Centre(Tile tile) => BoardProjection.Centre(tile, Top(tile));

    /// <summary>True when the tile is a bridge's deck: planks with a void beside them.</summary>
    public bool Spans(Tile tile) => Look(tile).Deck && Beside(tile).Any(n => Look(n).Void);

    /// <summary>What a neighbouring side comes down to at this tile: its top, or the chasm floor under a bridge's deck.</summary>
    public float Floor(Tile tile) => Spans(tile) ? Beside(tile).Where(n => Look(n).Void).Min(Top) : Top(tile);

    private int Index(Tile tile) => tile.Y * Map.Width + tile.X;

    private IEnumerable<Tile> Beside(Tile tile)
    {
        Tile[] all = [tile with { X = tile.X - 1 }, tile with { X = tile.X + 1 }, tile with { Y = tile.Y - 1 }, tile with { Y = tile.Y + 1 }];
        return all.Where(Map.InBounds);
    }

    private static string ChasmMaterial(BattleMap map)
    {
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
                if (map[new Tile(x, y)] is { } info && info.Flags.HasFlag(TileFlags.Deep))
                    return map.Materials[info.Material];
        return "void/deep";
    }
}
