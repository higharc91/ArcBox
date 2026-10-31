using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>What is drawn on a tile, in the order it's drawn there.</summary>
public enum BoardLayer
{
    /// <summary>The chasm floor seen under a bridge.</summary>
    Under,
    /// <summary>The tile's sides, with their lips.</summary>
    Side,
    /// <summary>The tile's top.</summary>
    Top,
    /// <summary>Hazards on the ground.</summary>
    Ground,
    /// <summary>A prop.</summary>
    Prop,
    /// <summary>Units: the board leaves them this slot.</summary>
    Unit,
    /// <summary>What rises above units: flames, clouds.</summary>
    Over,
}

/// <summary>
/// The painter's order: everything on the board, back to front. Tiles with a smaller x + y are farther away, and tiles
/// level with each other don't overlap, so this order holds for any heights.
/// </summary>
public static class BoardOrder
{
    private const long Above = 1L << 31;

    /// <summary>
    /// The sort key of a layer of a tile. Anything added to the board sorts in by this, so it's hidden by what's in front.
    /// What rises above units goes after its whole diagonal, as anything that tall reaches over the tiles beside it.
    /// </summary>
    public static long Key(Tile tile, BoardLayer layer)
        => ((long)(tile.X + tile.Y) << 32) | (layer == BoardLayer.Over ? Above : 0) | ((long)tile.X << 8) | (long)layer;

    /// <summary>
    /// Two lists in painter's order as one: each entry is an index into <paramref name="board"/> or, as its complement
    /// (<c>~i</c>), into <paramref name="units"/>. The board's items stay where they are, so units can move without
    /// rebuilding its plan.
    /// </summary>
    public static int[] Merge(IReadOnlyList<long> board, IReadOnlyList<long> units)
    {
        var merged = new int[board.Count + units.Count];
        var (b, u) = (0, 0);
        for (var i = 0; i < merged.Length; i++)
            merged[i] = u == units.Count || (b < board.Count && board[b] <= units[u]) ? b++ : ~u++;
        return merged;
    }
}

/// <summary>What a <see cref="Face"/> is.</summary>
public enum FaceKind { Under, Side, Lip, Top, Tint }

/// <summary>Which way a <see cref="Face"/> looks: up, or along +U (screen lower right) or +V (screen lower left).</summary>
public enum Facing { Up, U, V }

/// <summary>A flat colour, for what the pack has no art for.</summary>
public readonly record struct Rgb(float R, float G, float B);

/// <summary>Something to draw, at <see cref="Order"/> (<see cref="BoardOrder.Key"/>).</summary>
public abstract record BoardItem(long Order, Tile Tile);

/// <summary>A textured quad on the board.</summary>
/// <param name="Order">Its place in the painter's order.</param>
/// <param name="Tile">The tile it belongs to.</param>
/// <param name="Kind">What it is.</param>
/// <param name="Facing">Which way it looks.</param>
/// <param name="Points">Its four corners, in art pixels.</param>
/// <param name="Uvs">Each corner's place in one frame of <paramref name="Image"/>. V can pass 1, where a side repeats.</param>
/// <param name="Image">The texture, or null to fill with <paramref name="Fill"/>.</param>
/// <param name="Fill">The flat colour with no texture.</param>
/// <param name="Shade">Brightness: 1 is the art as it is.</param>
/// <param name="Alpha">Opacity.</param>
/// <param name="Grid">Outlined, as a tile the battle can use.</param>
public sealed record Face(long Order, Tile Tile, FaceKind Kind, Facing Facing, ImmutableArray<Vector2> Points, ImmutableArray<Vector2> Uvs,
    ArtImage? Image, Rgb? Fill, float Shade = 1, float Alpha = 1, bool Grid = false) : BoardItem(Order, Tile);

/// <summary>An upright or flat sprite, one frame of <see cref="Image"/> at a time.</summary>
/// <param name="Order">Its place in the painter's order.</param>
/// <param name="Tile">The tile it belongs to.</param>
/// <param name="Image">The strip.</param>
/// <param name="TopLeft">Where its top left lands, in art pixels.</param>
/// <param name="Size">One frame's size.</param>
public sealed record Sprite(long Order, Tile Tile, ArtImage Image, Vector2 TopLeft, Vector2 Size) : BoardItem(Order, Tile);

/// <summary>
/// The board, ready to draw: every face and sprite in painter's order, and the area they cover. Pure: built from a
/// <see cref="BoardTerrain"/> and the art's manifests. Later layers add their own items with <see cref="BoardOrder.Key"/>.
/// </summary>
/// <param name="Items">Back to front.</param>
/// <param name="Min">The top left of what's drawn, in art pixels.</param>
/// <param name="Max">The bottom right.</param>
/// <param name="Animated">Some art is animated, so the board needs redrawing as time passes.</param>
public sealed record BoardPlan(ImmutableArray<BoardItem> Items, Vector2 Min, Vector2 Max, bool Animated)
{
    private const int TilesPerTexture = 4;    // a texture covers 4 x 4 tiles (the manifests' "span")
    private const float TileTexels = 32;      // a side texture's repeat
    private const float LipDepth = 12;        // the lips' height, in pixels (at most; each lip is transparent below its edge)
    private const float ShadeU = 0.62f;       // the +U faces are in shadow, the +V ones half so: it's what shows the steps
    private const float ShadeV = 0.8f;
    private const float ShadePerStep = 0.04f; // and higher ground reads lighter
    private const float ShadeDeep = 0.9f;
    private const float ShadeFord = 1.3f;     // a ford has to read as shallower than the river around it
    private const float TintAlpha = 0.4f;

    private static readonly Rgb NoArt = new(0.3f, 0.3f, 0.32f);
    private static readonly Rgb ZoneTint = new(0.55f, 0.4f, 0.95f);

    /// <summary>The plan for a board.</summary>
    public static BoardPlan Build(BoardTerrain terrain, ArtIndex art)
    {
        var items = new List<BoardItem>();
        foreach (var tile in terrain.Tiles)
            AddTile(items, terrain, art, tile);
        AddProps(items, terrain, art);
        AddZones(items, terrain, art);

        ImmutableArray<BoardItem> ordered = [.. items.OrderBy(i => i.Order)];   // stable: a lip stays after its side
        var points = ordered.SelectMany(Extent).ToArray();
        var min = points.Aggregate(new Vector2(float.MaxValue), Vector2.Min);
        var max = points.Aggregate(new Vector2(float.MinValue), Vector2.Max);
        return new BoardPlan(ordered, min, max, ordered.Any(i => ImageOf(i) is { Frames: > 1 }));
    }

    private static ArtImage? ImageOf(BoardItem item) => item is Face face ? face.Image : ((Sprite)item).Image;

    private static IEnumerable<Vector2> Extent(BoardItem item)
        => item is Face face ? face.Points : [((Sprite)item).TopLeft, ((Sprite)item).TopLeft + ((Sprite)item).Size];

    private static void AddTile(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, Tile tile)
    {
        var look = terrain.Look(tile);
        var height = terrain.Top(tile);
        if (terrain.Spans(tile))
        {
            var floor = terrain.Floor(tile);
            items.Add(Flat(FaceKind.Under, BoardOrder.Key(tile, BoardLayer.Under), tile, floor,
                art.Texture(MaterialLook.Of("void/deep").Top), ShadeDeep));
        }
        AddSide(items, terrain, art, tile, Facing.U);
        AddSide(items, terrain, art, tile, Facing.V);

        var shade = look.Water
            ? terrain.Deep(tile) ? ShadeDeep : ShadeFord
            : 1 + Math.Max(0, height) * ShadePerStep;
        items.Add(Flat(FaceKind.Top, BoardOrder.Key(tile, BoardLayer.Top), tile, height, art.Texture(look.Top), shade) with
        {
            Grid = !look.Void && !terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable),
        });
    }

    private static void AddSide(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, Tile tile, Facing facing)
    {
        var next = facing == Facing.U ? tile with { X = tile.X + 1 } : tile with { Y = tile.Y + 1 };
        var inside = terrain.Map.InBounds(next);
        var top = terrain.Top(tile);
        var deck = terrain.Spans(tile);
        if (deck && inside && terrain.Top(next) >= top)
            return;
        var bottom = inside ? terrain.Floor(next) : terrain.Base;
        if (deck)
            bottom = Math.Max(bottom, top - BoardTerrain.DeckDepth);
        if (bottom >= top)
            return;

        var look = terrain.Look(tile);
        var chasm = inside && (terrain.Look(next).Void || terrain.Spans(next));
        var image = art.Texture(chasm && !deck ? look.Chasm : look.Side);
        var depth = (top - bottom) * BoardProjection.HeightStep;
        var along = (facing == Facing.U ? tile.Y : tile.X) % TilesPerTexture / (float)TilesPerTexture;
        var (u0, u1) = (along, along + 1f / TilesPerTexture);
        var rows = image is { Height: > (int)TileTexels } ? Math.Min(1, depth / image.Height) : depth / TileTexels;   // a tall texture clamps, others repeat
        var shade = facing == Facing.U ? ShadeU : ShadeV;
        var key = BoardOrder.Key(tile, BoardLayer.Side);
        var edge = Edge(tile, facing, top);
        var drop = new Vector2(0, depth);
        items.Add(new Face(key, tile, FaceKind.Side, facing, [edge.Near, edge.Far, edge.Far + drop, edge.Near + drop],
            [new(u0, 0), new(u1, 0), new(u1, rows), new(u0, rows)], image, image is null ? NoArt : null, shade));

        if (look.Lip is { } lip && art.Texture(lip) is { } lipImage)
        {
            var band = Math.Min(LipDepth, depth);
            var hang = new Vector2(0, band);
            var v = band / lipImage.Height;
            items.Add(new Face(key, tile, FaceKind.Lip, facing, [edge.Near, edge.Far, edge.Far + hang, edge.Near + hang],
                [new(u0, 0), new(u1, 0), new(u1, v), new(u0, v)], lipImage, null, shade));
        }
    }

    /// <summary>The tile's bottom edge on the side facing <paramref name="facing"/>, at <paramref name="height"/>.</summary>
    private static (Vector2 Near, Vector2 Far) Edge(Tile tile, Facing facing, float height) => facing == Facing.U
        ? (BoardProjection.Corner(tile.X + 1, tile.Y, height), BoardProjection.Corner(tile.X + 1, tile.Y + 1, height))
        : (BoardProjection.Corner(tile.X, tile.Y + 1, height), BoardProjection.Corner(tile.X + 1, tile.Y + 1, height));

    /// <summary>A tile-sized diamond at a height, its texture laid in world space.</summary>
    private static Face Flat(FaceKind kind, long order, Tile tile, float height, ArtImage? image, float shade)
    {
        var (x, y) = (tile.X, tile.Y);
        var u = x % TilesPerTexture / (float)TilesPerTexture;
        var v = y % TilesPerTexture / (float)TilesPerTexture;
        var step = 1f / TilesPerTexture;
        return new Face(order, tile, kind, Facing.Up,
            [BoardProjection.Corner(x, y, height), BoardProjection.Corner(x + 1, y, height),
                BoardProjection.Corner(x + 1, y + 1, height), BoardProjection.Corner(x, y + 1, height)],
            [new(u, v), new(u + step, v), new(u + step, v + step), new(u, v + step)], image, image is null ? NoArt : null, shade);
    }

    private static void AddProps(List<BoardItem> items, BoardTerrain terrain, ArtIndex art)
    {
        var mapVariants = terrain.Map.Materials.Select(m => MaterialLook.Of(m).Variant).ToArray();
        foreach (var prop in terrain.Map.Props.Where(p => terrain.Map.InBounds(p.At)))
        {
            // A prop takes the colour of the ground under it, or failing that of anything on the map.
            if (art.Prop(prop.Id, mapVariants.Prepend(terrain.Look(prop.At).Variant)) is not { } sprite)
                continue;
            var topLeft = terrain.Centre(prop.At) - new Vector2(sprite.AnchorX, sprite.AnchorY);
            items.Add(new Sprite(BoardOrder.Key(prop.At, BoardLayer.Prop), prop.At, sprite.Image, topLeft, new Vector2(sprite.Width, sprite.Height)));
        }
    }

    private static void AddZones(List<BoardItem> items, BoardTerrain terrain, ArtIndex art)
    {
        foreach (var zone in terrain.Changes.Zones.OrderBy(z => z.Serial))
        {
            var hazard = ZoneArt.HazardFor(zone.Kind) is { } id ? art.Hazard(id) : null;
            foreach (var tile in zone.Tiles.Where(terrain.Map.InBounds))
            {
                var ground = BoardOrder.Key(tile, BoardLayer.Ground);
                if (hazard is null)
                {
                    items.Add(Flat(FaceKind.Tint, ground, tile, terrain.Top(tile), null, 1) with { Fill = ZoneTint, Alpha = TintAlpha });
                    continue;
                }
                var centre = terrain.Centre(tile);
                items.Add(new Sprite(ground, tile, hazard.Ground, centre - hazard.Origin, hazard.Frame));
                if (hazard.Over is { } over)
                    items.Add(new Sprite(BoardOrder.Key(tile, BoardLayer.Over), tile, over, centre - hazard.OverOrigin, hazard.OverFrame));
            }
        }
    }
}
