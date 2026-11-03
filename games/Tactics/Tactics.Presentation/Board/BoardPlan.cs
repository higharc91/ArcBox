using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.World;

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
public enum FaceKind { Under, Side, Lip, Top, Blend, Tint, Decal, Building, Shadow }

/// <summary>Which way a <see cref="Face"/> looks: up, or along +U (screen lower right) or +V (screen lower left).</summary>
public enum Facing { Up, U, V }

/// <summary>A flat colour, for what the pack has no art for.</summary>
public readonly record struct Rgb(float R, float G, float B)
{
    /// <summary>This colour lit by <paramref name="light"/>: channel by channel.</summary>
    public Rgb Times(Rgb light) => new(R * light.R, G * light.G, B * light.B);

    /// <summary>A colour as the manifests write it: <c>#rrggbb</c>.</summary>
    public static Rgb Hex(string hex)
    {
        var value = Convert.ToInt32(hex[1..], 16);
        return new Rgb((value >> 16 & 255) / 255f, (value >> 8 & 255) / 255f, (value & 255) / 255f);
    }
}

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
/// <param name="Lit">Takes the board's light. What the rules mean is left out, so it stays clear.</param>
/// <param name="VertexTints">Per-corner tint colors before light (empty for uniform <see cref="Tint"/>).</param>
/// <param name="Lights">What shows over it, unlit, once the light has gone down: a building's windows.</param>
/// <param name="Part">The building it's a piece of, by its place in the map's buildings, or -1.</param>
/// <param name="Emits">Where light comes out of it once it's dark, from its first corner: a building's forge and doors.</param>
public sealed record Face(long Order, Tile Tile, FaceKind Kind, Facing Facing, ImmutableArray<Vector2> Points, ImmutableArray<Vector2> Uvs,
    ArtImage? Image, Rgb? Fill, float Shade = 1, float Alpha = 1, bool Grid = false, bool Lit = true,
    ImmutableArray<Rgb> VertexTints = default, ArtImage? Lights = null, int Part = -1, ImmutableArray<Vector2> Emits = default) : BoardItem(Order, Tile)
{
    /// <summary>It lies turned on a tile, so the TV gets its art through <see cref="DitherTexels"/>. A shadow lies down but its art is drawn upright, pixel for pixel.</summary>
    public bool Flat => Facing == Facing.Up && Kind != FaceKind.Shadow;

    /// <summary>Its colour before any light: its fill, or white for art, at its shade.</summary>
    public Rgb Tint => (Fill ?? new Rgb(1, 1, 1)).Times(new Rgb(Shade, Shade, Shade));

    /// <summary>The colour at corner before light.</summary>
    public Rgb TintAt(int corner) => VertexTints.IsDefaultOrEmpty ? Tint : VertexTints[corner];

    /// <summary>The face as four cells that cover it, so light can peak inside a tile and not only on its corners. None is outlined.</summary>
    public ImmutableArray<Face> Cells()
    {
        static ImmutableArray<Vector2> Split(ImmutableArray<Vector2> q, int cell)
        {
            var (back, right, front, left, middle) = ((q[0] + q[1]) / 2, (q[1] + q[2]) / 2, (q[2] + q[3]) / 2, (q[3] + q[0]) / 2, (q[0] + q[2]) / 2);
            return cell switch
            {
                0 => [q[0], back, middle, left],
                1 => [back, q[1], right, middle],
                2 => [middle, right, q[2], front],
                _ => [left, middle, front, q[3]],
            };
        }
        static ImmutableArray<Rgb> SplitTints(ImmutableArray<Rgb> q, int cell)
        {
            static Rgb Mid(Rgb a, Rgb b) => new((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2);
            var (back, right, front, left, middle) = (Mid(q[0], q[1]), Mid(q[1], q[2]), Mid(q[2], q[3]), Mid(q[3], q[0]), Mid(q[0], q[2]));
            return cell switch
            {
                0 => [q[0], back, middle, left],
                1 => [back, q[1], right, middle],
                2 => [middle, right, q[2], front],
                _ => [left, middle, front, q[3]],
            };
        }
        return [.. Enumerable.Range(0, 4).Select(cell => this with
        {
            Points = Split(Points, cell),
            Uvs = Split(Uvs, cell),
            Grid = false,
            VertexTints = VertexTints.IsDefaultOrEmpty ? default : SplitTints(VertexTints, cell),
        })];
    }
}

/// <summary>An upright or flat sprite, one frame of <see cref="Image"/> at a time.</summary>
/// <param name="Order">Its place in the painter's order.</param>
/// <param name="Tile">The tile it belongs to.</param>
/// <param name="Image">The strip.</param>
/// <param name="TopLeft">Where its top left lands, in art pixels.</param>
/// <param name="Size">One frame's size.</param>
/// <param name="Light">The light it gives, or null.</param>
/// <param name="Lit">Takes the board's light. What the rules mean is left out, so it stays clear.</param>
/// <param name="TileHeight">The manifest height of the prop in tiles (default 1).</param>
/// <param name="Part">The building it hangs on, by its place in the map's buildings, or -1.</param>
public sealed record Sprite(long Order, Tile Tile, ArtImage Image, Vector2 TopLeft, Vector2 Size, PropLight? Light = null, bool Lit = true, int TileHeight = 1, int Part = -1) : BoardItem(Order, Tile)
{
    /// <summary>True when this sprite is a prop of height 2 or more.</summary>
    public bool IsTallProp => (Order & 0xFF) == (long)BoardLayer.Prop && TileHeight >= 2;
}

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
    private const int TilesPerTexture = 4;    // standard terrain textures cover 4 x 4 tiles
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
        BuildingPlan.Add(items, terrain, art);
        AddZones(items, terrain, art);

        ImmutableArray<BoardItem> ordered = [.. items.OrderBy(i => i.Order)];   // stable: a lip stays after its side
        var points = ordered.SelectMany(Extent).ToArray();
        var min = points.Aggregate(new Vector2(float.MaxValue), Vector2.Min);
        var max = points.Aggregate(new Vector2(float.MinValue), Vector2.Max);
        return new BoardPlan(ordered, min, max, ordered.Any(i => ImageOf(i) is { Frames: > 1 }));
    }

    /// <summary>
    /// The part of <see cref="Build"/>'s plan that belongs to the tiles of <paramref name="rect"/>, by the same code, so
    /// neighbours outside it are read from <paramref name="terrain"/> and there's no seam or skirt at its edges. Items stay in
    /// the terrain's own tiles and art pixels, with their keys, so plans of different rects sort together. Without
    /// <paramref name="grid"/> no tile is outlined. An empty rect's plan has no items and sits at the origin.
    /// With <paramref name="world"/>, decals and ground tint are added (skipping stamped tiles). They go by where a tile is in the region, and <paramref name="origin"/>
    /// is where this terrain's tile (0, 0) is in it, for a terrain that's a window on a region.
    /// </summary>
    public static BoardPlan BuildBlock(BoardTerrain terrain, ArtIndex art, TileRect rect, bool grid, bool world = false, Tile origin = default)
    {
        var items = new List<BoardItem>();
        foreach (var tile in terrain.TilesIn(rect))
            AddTile(items, terrain, art, tile, world, origin);
        AddProps(items, terrain, art, rect, world);
        BuildingPlan.Add(items, terrain, art, rect);
        AddZones(items, terrain, art, rect);

        return Of([.. items.OrderBy(i => i.Order).Select(i => !grid && i is Face { Grid: true } face ? face with { Grid = false } : i)]);
    }

    /// <summary>
    /// A plan of <paramref name="ordered"/>, which are already in painter's order, with the area they cover. An empty plan has no items and
    /// sits at the origin.
    /// </summary>
    public static BoardPlan Of(ImmutableArray<BoardItem> ordered)
    {
        if (ordered.IsEmpty)
            return new BoardPlan([], Vector2.Zero, Vector2.Zero, false);
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var item in ordered)
        {
            var (from, to) = Bounds(item);
            (min, max) = (Vector2.Min(min, from), Vector2.Max(max, to));
        }
        return new BoardPlan(ordered, min, max, ordered.Any(i => ImageOf(i) is { Frames: > 1 }));
    }

    /// <summary>
    /// The same plan moved by <paramref name="delta"/> art pixels: every face's points, every sprite's top left and the area. Tiles, keys, UVs
    /// and light stay as they are, so a plan built in one frame (a region's) can be drawn in another (a battle map's, overworld 5c §4).
    /// </summary>
    public BoardPlan Shifted(Vector2 delta)
    {
        if (delta == Vector2.Zero)
            return this;
        ImmutableArray<BoardItem> items = [.. Items.Select(BoardItem (i) => i switch
        {
            Face face => face with { Points = [.. face.Points.Select(p => p + delta)] },
            Sprite sprite => sprite with { TopLeft = sprite.TopLeft + delta },
            _ => throw new InvalidOperationException($"Unknown board item {i.GetType().Name}."),
        })];
        return this with { Items = items, Min = Min + delta, Max = Max + delta };
    }

    /// <summary>The box an item covers, in art pixels.</summary>
    public static (Vector2 Min, Vector2 Max) Bounds(BoardItem item)
    {
        if (item is Sprite sprite)
            return (sprite.TopLeft, sprite.TopLeft + sprite.Size);
        var points = ((Face)item).Points;
        var (min, max) = (points[0], points[0]);
        for (var i = 1; i < points.Length; i++)
            (min, max) = (Vector2.Min(min, points[i]), Vector2.Max(max, points[i]));
        return (min, max);
    }

    private static ArtImage? ImageOf(BoardItem item) => item is Face face ? face.Image : ((Sprite)item).Image;

    private static IEnumerable<Vector2> Extent(BoardItem item)
        => item is Face face ? face.Points : [((Sprite)item).TopLeft, ((Sprite)item).TopLeft + ((Sprite)item).Size];

    /// <summary>A slow continuous tint drift across world tile coordinates, stopping ash and water repeating every 4 tiles.</summary>
    public static Rgb GroundTint(float wx, float wy, Rgb baseTint)
    {
        var d1 = MathF.Sin(wx * 0.08f + wy * 0.05f);
        var d2 = MathF.Cos(wx * 0.05f - wy * 0.07f);
        var r = Math.Clamp(1f + 0.06f * d1, 0.8f, 1.2f);
        var g = Math.Clamp(1f + 0.03f * (d1 + d2), 0.8f, 1.2f);
        var b = Math.Clamp(1f - 0.05f * d2, 0.8f, 1.2f);
        return new Rgb(baseTint.R * r, baseTint.G * g, baseTint.B * b);
    }

    private static uint DecalHash(int x, int y, string material)
    {
        uint h = 2166136261u;
        h = (h ^ (uint)x) * 16777619u;
        h = (h ^ (uint)y) * 16777619u;
        foreach (var c in material)
            h = (h ^ (uint)c) * 16777619u;
        h ^= h >> 16;
        h *= 0x85ebca6bu;
        h ^= h >> 13;
        h *= 0xc2b2ae35u;
        h ^= h >> 16;
        return h;
    }

    private static readonly ImmutableArray<ArtRef> Flowers =
    [
        new("flowers_0", "red"), new("flowers_0", "yellow"), new("flowers_0", "white"), new("flowers_0", "blue"),
        new("flowers_1", "red"), new("flowers_1", "yellow"), new("flowers_1", "white"), new("flowers_1", "blue"),
        new("flowers_2", "red"), new("flowers_2", "yellow"), new("flowers_2", "white"), new("flowers_2", "blue"),
    ];

    private static readonly ImmutableArray<ArtRef> Leaves =
    [
        new("leaves_0", "autumn"), new("leaves_0", "red"), new("leaves_0", "green"),
        new("leaves_1", "autumn"), new("leaves_1", "red"), new("leaves_1", "green"),
    ];

    private static readonly ImmutableArray<ArtRef> Pebbles =
    [
        new("pebbles_0", "grey"), new("pebbles_1", "grey"),
    ];

    private static readonly ImmutableArray<ArtRef> FallenLeaves =
    [
        new("leaves_0", "autumn"), new("leaves_0", "red"), new("leaves_1", "autumn"), new("leaves_1", "red"),
    ];

    private static readonly ArtRef ScorchBurnt = new("scorch", "burnt");
    private static readonly ArtRef BonesBone = new("bones", "bone");
    private static readonly ArtRef CracksDark = new("cracks", "dark");
    private static readonly ArtRef PuddleSwamp = new("puddle", "swamp");
    private static readonly ArtRef BloodRed = new("blood", "red");

    /// <summary>
    /// Selects a decal for a tile in the world by deterministic hash of tile coordinates and material (~1 tile in 6). What's chosen from has to belong on the material: pebbles and
    /// fallen leaves on earth and grass, scorch on ash, a puddle only on what holds water, and stone laid by hand keeps clean but for a stray pebble. Green grass has its flowers.
    /// </summary>
    public static ArtImage? DecalFor(Tile tile, string material, ArtIndex art)
    {
        var family = material.Split('/')[0];
        if (family is "ash_ford" or "grass_ford" or "dirt_ford" or "rock_ford" or "ford_water" or "path_ford")
            return null;

        var h = DecalHash(tile.X, tile.Y, material);
        if (h % 6 != 0)
            return null;

        ImmutableArray<ArtRef> pool = family switch
        {
            "grass" when material is "grass/green" or "grass/lush" => [.. Flowers, .. Leaves, .. Pebbles],
            "grass_ford" or "grass" or "forest_floor" or "forest_needles" => [.. FallenLeaves, .. Pebbles],
            "dirt_ford" or "dirt" or "path" or "sand" => [.. Pebbles, .. FallenLeaves],
            "swamp" => [PuddleSwamp, CracksDark, .. Leaves],
            "mud" => [PuddleSwamp, .. Pebbles],
            "rock_ford" or "rock" or "basalt" => [.. Pebbles],
            "ash_ford" or "ash" or "scorch" => [ScorchBurnt, CracksDark, .. Pebbles, .. Pebbles],
            "bone" or "crypt_tile" or "crypt_tiles" or "ruin" or "dungeon_floor" or "dungeon_floor_mossy" or "dungeon_floor_cracked" or "dungeon_wall"
                => [ScorchBurnt, CracksDark, BonesBone, BloodRed, .. Pebbles],
            _ => [],
        };
        if (pool.IsEmpty)
            return (family is "cobble" or "flagstone" or "pavers") && (h / 6) % 8 == 0 ? art.Decal(Pebbles[(int)(h / 48 % 2)]) : null;

        var pick = pool[(int)((h / 6) % (uint)pool.Length)];
        return art.Decal(pick);
    }

    private static void AddTile(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, Tile tile, bool world = false, Tile origin = default)
    {
        var look = terrain.Look(tile);
        var isStamped = terrain.Map.StampedTiles.Contains(tile);
        var height = terrain.Top(tile);
        if (terrain.Spans(tile))
        {
            var floor = terrain.Floor(tile);
            items.Add(Flat(FaceKind.Under, BoardOrder.Key(tile, BoardLayer.Under), tile, floor,
                art.Texture(MaterialLook.Of("void/deep").Top), ShadeDeep));
        }
        AddSide(items, terrain, art, tile, Facing.U, world);
        AddSide(items, terrain, art, tile, Facing.V, world);

        var shade = look.Water
            ? terrain.Deep(tile) ? ShadeDeep : ShadeFord
            : 1 + Math.Max(0, height) * ShadePerStep;
        var top = Flat(FaceKind.Top, BoardOrder.Key(tile, BoardLayer.Top), tile, height, art.Texture(look.Top), shade) with
        {
            Grid = !look.Void && !terrain.Map[tile].Flags.HasFlag(TileFlags.Impassable),
        };
        var inRegion = new Tile(tile.X + origin.X, tile.Y + origin.Y);
        var graded = world ? top.Tint.Times(GradeOf(terrain, tile)) : top.Tint;
        if (world && !isStamped)
        {
            var (x, y) = (inRegion.X, inRegion.Y);
            top = top with
            {
                VertexTints =
                [
                    GroundTint(x, y, graded),
                    GroundTint(x + 1, y, graded),
                    GroundTint(x + 1, y + 1, graded),
                    GroundTint(x, y + 1, graded),
                ]
            };
        }
        else if (graded != top.Tint)
        {
            top = top with { VertexTints = [graded, graded, graded, graded] };
        }
        items.Add(top);

        if (world)
            AddBlends(items, terrain, art, tile, top, inRegion);

        if (world && !isStamped && !look.Void && !look.Water && !terrain.Changes.Sunk.Contains(tile))
        {
            var material = MaterialLook.ArtMaterial(terrain.Map.Id, terrain.Map.Materials[terrain.Map[tile].Material]);
            if (DecalFor(inRegion, material, art) is { } decalImage)
            {
                var (x, y) = (tile.X, tile.Y);
                items.Add(new Face(BoardOrder.Key(tile, BoardLayer.Ground), tile, FaceKind.Decal, Facing.Up,
                    [BoardProjection.Corner(x, y, height), BoardProjection.Corner(x + 1, y, height),
                     BoardProjection.Corner(x + 1, y + 1, height), BoardProjection.Corner(x, y + 1, height)],
                    [new(0, 0), new(1, 0), new(1, 1), new(0, 1)], decalImage, null, shade));
            }
        }
    }

    /// <summary>What runs over a tile from beside it (<see cref="GroundBlend"/>): the neighbour's top where this one lies, at the colour it has there. A stamp's tile has no drift.</summary>
    private static void AddBlends(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, Tile tile, Face top, Tile inRegion)
    {
        foreach (var edge in GroundBlend.Of(terrain, tile))
        {
            var material = edge.Stamped ? edge.Material : MaterialLook.ArtMaterial(terrain.Map.Id, edge.Material);
            if (art.Texture(MaterialLook.Of(material).Top) is not { } image)
                continue;
            var graded = top.Tint.Times(GroundGrade.Of(terrain.Map.Id, material));
            var (x, y) = (inRegion.X, inRegion.Y);
            ImmutableArray<Rgb> tints = edge.Stamped
                ? [graded, graded, graded, graded]
                : [GroundTint(x, y, graded), GroundTint(x + 1, y, graded), GroundTint(x + 1, y + 1, graded), GroundTint(x, y + 1, graded)];
            items.Add(top with { Order = BoardOrder.Key(tile, BoardLayer.Ground), Kind = FaceKind.Blend, Image = image with { Glow = null, Blend = edge.Mask }, Grid = false, VertexTints = tints });
        }
    }

    private static Rgb GradeOf(BoardTerrain terrain, Tile tile)
    {
        var material = terrain.Map[tile].Material;
        if (material >= terrain.Map.Materials.Length)
            return new Rgb(1, 1, 1);
        var name = terrain.Map.Materials[material];
        if (!terrain.Map.StampedTiles.Contains(tile))
            name = MaterialLook.ArtMaterial(terrain.Map.Id, name);
        return GroundGrade.Of(terrain.Map.Id, name);
    }

    private static void AddSide(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, Tile tile, Facing facing, bool world)
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
        var tilesPerTexture = image?.Span ?? TilesPerTexture;
        var along = (facing == Facing.U ? tile.Y : tile.X) % tilesPerTexture / (float)tilesPerTexture;
        var (u0, u1) = (along, along + 1f / tilesPerTexture);
        var clamps = image is { Height: > (int)TileTexels };     // a tall texture clamps, others repeat
        var drawn = clamps ? Math.Min(depth, image!.Height) : depth;
        var rows = clamps ? drawn / image!.Height : depth / TileTexels;
        var shade = facing == Facing.U ? ShadeU : ShadeV;
        var key = BoardOrder.Key(tile, BoardLayer.Side);
        var edge = Edge(tile, facing, top);
        var drop = new Vector2(0, drawn);
        items.Add(new Face(key, tile, FaceKind.Side, facing, [edge.Near, edge.Far, edge.Far + drop, edge.Near + drop],
            [new(u0, 0), new(u1, 0), new(u1, rows), new(u0, rows)], image, image is null ? NoArt : null, shade));
        if (depth > drawn)
        {
            // Clamped: past the art, the wall goes on as its last row, which is what the art fades to.
            var last = 1 - 0.5f / image!.Height;
            var floor = new Vector2(0, depth);
            items.Add(new Face(key, tile, FaceKind.Side, facing, [edge.Near + drop, edge.Far + drop, edge.Far + floor, edge.Near + floor],
                [new(u0, last), new(u1, last), new(u1, last), new(u0, last)], image, null, shade));
        }

        if (look.Lip is { } lip && art.Texture(lip) is { } lipImage)
        {
            var band = Math.Min(LipDepth, depth);
            var hang = new Vector2(0, band);
            var v = band / lipImage.Height;
            var lipFace = new Face(key, tile, FaceKind.Lip, facing, [edge.Near, edge.Far, edge.Far + hang, edge.Near + hang],
                [new(u0, 0), new(u1, 0), new(u1, v), new(u0, v)], lipImage, null, shade);
            var graded = world ? lipFace.Tint.Times(GradeOf(terrain, tile)) : lipFace.Tint;
            items.Add(graded == lipFace.Tint ? lipFace : lipFace with { VertexTints = [graded, graded, graded, graded] });
        }
    }

    /// <summary>The tile's bottom edge on the side facing <paramref name="facing"/>, at <paramref name="height"/>.</summary>
    private static (Vector2 Near, Vector2 Far) Edge(Tile tile, Facing facing, float height) => facing == Facing.U
        ? (BoardProjection.Corner(tile.X + 1, tile.Y, height), BoardProjection.Corner(tile.X + 1, tile.Y + 1, height))
        : (BoardProjection.Corner(tile.X, tile.Y + 1, height), BoardProjection.Corner(tile.X + 1, tile.Y + 1, height));

    /// <summary>A tile-sized diamond at a height, its texture laid in world space.</summary>
    private static Face Flat(FaceKind kind, long order, Tile tile, float height, ArtImage? image, float shade)
    {
        var span = image?.Span ?? TilesPerTexture;
        var (x, y) = (tile.X, tile.Y);
        var u = x % span / (float)span;
        var v = y % span / (float)span;
        var step = 1f / span;
        return new Face(order, tile, kind, Facing.Up,
            [BoardProjection.Corner(x, y, height), BoardProjection.Corner(x + 1, y, height),
                BoardProjection.Corner(x + 1, y + 1, height), BoardProjection.Corner(x, y + 1, height)],
            [new(u, v), new(u + step, v), new(u + step, v + step), new(u, v + step)], image, image is null ? NoArt : null, shade);
    }

    private static void AddProps(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, TileRect? within = null, bool world = false)
    {
        var mapVariants = terrain.Map.Materials.Select(m => MaterialLook.Of(m).Variant).ToArray();
        foreach (var prop in terrain.Map.Props.Where(p => terrain.Map.InBounds(p.At) && within?.Contains(p.At) != false))
        {
            // Living world creatures are drawn by the TV's actor layer. Battle maps keep the same art baked into the board.
            if (world && WorldAnimalIds.Contains(prop.Id))
                continue;
            // A prop takes its variant if specified, then the colour of the ground under it, or failing that of anything on the map.
            var prefer = prop.Variant is not null
                ? mapVariants.Prepend(terrain.Look(prop.At).Variant).Prepend(prop.Variant)
                : mapVariants.Prepend(terrain.Look(prop.At).Variant);
            if (art.Prop(prop.Id, prefer) is not { } sprite)
                continue;
            var topLeft = terrain.Centre(prop.At) - new Vector2(sprite.AnchorX, sprite.AnchorY);
            items.Add(new Sprite(BoardOrder.Key(prop.At, BoardLayer.Prop), prop.At, sprite.Image, topLeft, new Vector2(sprite.Width, sprite.Height),
                terrain.Changes.Sunk.Contains(prop.At) ? null : sprite.Light, TileHeight: sprite.TileHeight));    // what went down with its tile has gone out, whatever the chasm is
        }
    }

    private static void AddZones(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, TileRect? within = null)
    {
        foreach (var zone in terrain.Changes.Zones.OrderBy(z => z.Serial))
        {
            var hazard = ZoneArt.HazardFor(zone.Kind) is { } id ? art.Hazard(id) : null;
            foreach (var tile in zone.Tiles.Where(t => terrain.Map.InBounds(t) && within?.Contains(t) != false))
            {
                var ground = BoardOrder.Key(tile, BoardLayer.Ground);
                if (zone.Kind == "scorched" && art.Decal("scorch_burnt") is { } decal)
                {
                    items.Add(Flat(FaceKind.Decal, ground, tile, terrain.Top(tile), decal, 1) with { Alpha = 0.7f });
                    continue;
                }
                if (hazard is null)
                {
                    items.Add(Flat(FaceKind.Tint, ground, tile, terrain.Top(tile), null, 1) with { Fill = ZoneTint, Alpha = TintAlpha, Lit = false });
                    continue;
                }
                var centre = terrain.Centre(tile);
                items.Add(new Sprite(ground, tile, hazard.Ground, centre - hazard.Origin, hazard.Frame, Lit: false));
                if (hazard.Over is { } over)
                    items.Add(new Sprite(BoardOrder.Key(tile, BoardLayer.Over), tile, over, centre - hazard.OverOrigin, hazard.OverFrame, Lit: false));
            }
        }
    }
}
