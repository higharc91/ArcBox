using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;

namespace Tactics.Presentation.Board;

/// <summary>Something pinned to a building: a smoke strip or a swinging sign. <see cref="Anchor"/> is the pixel to pin on the attach point.</summary>
public sealed record OverlayArt(ArtImage Image, Vector2 Size, Vector2 Anchor);

/// <summary>
/// Where a building's art has pixels, which its manifest doesn't say: a frame leaves room to the right for the shadow, and a roof doesn't fill a column to the frame's top.
/// Worked out once per image from its PNG. Shared between every <see cref="ArtIndex"/>, so a fight's first frame doesn't read a file the world has read.
/// </summary>
/// <param name="Tops">For each pixel column of a frame, the first row with a pixel in any frame, or -1 for none.</param>
/// <param name="Bottoms">The last such row.</param>
public sealed record BuildingShape(ImmutableArray<int> Tops, ImmutableArray<int> Bottoms)
{
    private static readonly ConcurrentDictionary<string, BuildingShape> Shapes = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, ImmutableArray<(int X, int Y)>> Shadows = new(StringComparer.Ordinal);

    /// <summary>The shape of the strip in <paramref name="file"/>, of frames this wide.</summary>
    public static BuildingShape Of(string file, int frameWidth) => Shapes.GetOrAdd(file, f =>
    {
        var (rgba, width, height) = PngPixels.Read(f);
        var (tops, bottoms) = (new int[frameWidth], new int[frameWidth]);
        Array.Fill(tops, -1);
        Array.Fill(bottoms, -1);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (rgba[(y * width + x) * 4 + 3] > 0)
                {
                    var column = x % frameWidth;
                    tops[column] = tops[column] < 0 ? y : Math.Min(tops[column], y);
                    bottoms[column] = Math.Max(bottoms[column], y);
                }
        return new BuildingShape([.. tops], [.. bottoms]);
    });

    /// <summary>The tiles, from a building's back corner, with any of the shadow in <paramref name="file"/> on them, with <paramref name="origin"/> that corner's pixel.</summary>
    public static ImmutableArray<(int X, int Y)> ShadowTiles(string file, Vector2 origin) => Shadows.GetOrAdd(file, f =>
    {
        var (rgba, width, height) = PngPixels.Read(f);
        var tiles = new HashSet<(int, int)>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                if (rgba[(y * width + x) * 4 + 3] > 0)
                {
                    var (across, down) = ((x + 0.5f - origin.X) / BoardProjection.HalfWidth, (y + 0.5f - origin.Y) / BoardProjection.HalfHeight);
                    tiles.Add(((int)MathF.Floor((down + across) / 2), (int)MathF.Floor((down - across) / 2)));
                }
        return [.. tiles.Order()];
    });
}

/// <summary>
/// A building of <c>town/manifest.json</c>, in one variant. All pixels are in a frame of <see cref="Size"/>, and <see cref="Origin"/> is its back corner at ground level.
/// </summary>
/// <param name="Id">Its id.</param>
/// <param name="Image">The strip of frames.</param>
/// <param name="Shadow">The ground shadow, in the same frame, or null.</param>
/// <param name="Lights">What shows at dusk, the same frames over it, or null.</param>
/// <param name="Size">One frame.</param>
/// <param name="Origin">The footprint's back corner in a frame.</param>
/// <param name="Width">Footprint tiles along x.</param>
/// <param name="Depth">Footprint tiles along y.</param>
/// <param name="Blocks">By row and column: true stops the party.</param>
/// <param name="Walkable">The whole of it is walked on, as steps and bridges are, not under, as a gate's arch is.</param>
/// <param name="Smoke">Where chimneys smoke, in frame 0, and what with.</param>
/// <param name="SmokeArt">The smoke.</param>
/// <param name="Signs">Where a sign's root is, in frame 0.</param>
/// <param name="SignArt">The sign, or null.</param>
/// <param name="Glows">Where light comes from, in frame 0.</param>
/// <param name="Shape">Where it has pixels.</param>
/// <param name="ShadowTiles">The tiles from its back corner that its shadow falls on.</param>
public sealed record BuildingArt(string Id, ArtImage Image, ArtImage? Shadow, ArtImage? Lights, Vector2 Size, Vector2 Origin, int Width, int Depth,
    ImmutableArray<ImmutableArray<bool>> Blocks, bool Walkable, ImmutableArray<Vector2> Smoke, OverlayArt? SmokeArt, ImmutableArray<Vector2> Signs, OverlayArt? SignArt,
    ImmutableArray<Vector2> Glows, BuildingShape Shape, ImmutableArray<(int X, int Y)> ShadowTiles)
{
    /// <summary>The tile of the footprint that a screen column counts as, from the back corner: the frontmost one the column crosses, or past the footprint's sides the nearest.</summary>
    /// <param name="column">Columns are <see cref="BuildingPlan.ColumnWidth"/> pixels wide and column 0 starts at the origin, so a tile (x, y) crosses columns x - y - 1 and x - y.</param>
    public (int X, int Y) FrontOf(int column)
    {
        var within = Math.Clamp(column, -Depth, Width - 1);
        var (best, sum) = ((0, 0), -1);
        for (var y = 0; y < Depth; y++)
            for (var x = 0; x < Width; x++)
                if (x - y == within || x - y == within + 1)
                    if (x + y > sum)
                        (best, sum) = ((x, y), x + y);
        return best;
    }
}

/// <summary>
/// What a building draws, from <c>town/manifest.json</c>'s notes. It's cut into 32 px screen columns from its origin, each sorted at the frontmost footprint tile in that column, so
/// a unit passes behind and in front of it. A column over a tile that's walked under, as a gate's arch is, sorts after what's on that tile. Its shadow lies on the ground, a tile at
/// a time, so each piece goes down with the ground it's on and under what stands there. Smoke and signs are pinned to their attach points and sort after what they hang on.
/// Its pieces carry <see cref="Sprite.Part"/>, so the building can fade as one.
/// </summary>
public static class BuildingPlan
{
    /// <summary>The width of a column, in pixels: half a tile's diamond.</summary>
    public const int ColumnWidth = 32;

    /// <summary>Adds the buildings of <paramref name="terrain"/> whose pieces belong to the tiles of <paramref name="within"/>.</summary>
    public static void Add(List<BoardItem> items, BoardTerrain terrain, ArtIndex art, TileRect? within = null)
    {
        var buildings = terrain.Map.Buildings;
        for (var part = 0; part < buildings.Length; part++)
        {
            if (art.Building(buildings[part].Id, buildings[part].Variant) is { } building)
                AddBuilding(items, terrain, buildings[part], building, part, within);
        }
    }

    private static void AddBuilding(List<BoardItem> items, BoardTerrain terrain, BuildingPlacement placed, BuildingArt art, int part, TileRect? within)
    {
        bool Mine(Tile tile) => within?.Contains(tile) != false;
        Tile World((int X, int Y) tile) => new(placed.At.X + tile.X, placed.At.Y + tile.Y);
        var corner = BoardProjection.Corner(placed.At.X, placed.At.Y, placed.Height);
        var frame = corner - art.Origin;

        AddShadow(items, terrain, placed, art, frame, Mine);

        var first = (int)MathF.Floor(-art.Origin.X / ColumnWidth);
        var last = (int)MathF.Floor((art.Size.X - 1 - art.Origin.X) / ColumnWidth);
        for (var column = first; column <= last; column++)
        {
            var (from, to) = (Math.Max(0, (int)art.Origin.X + column * ColumnWidth), Math.Min((int)art.Size.X, (int)art.Origin.X + (column + 1) * ColumnWidth));
            var (top, bottom) = Span(art.Shape, from, to);
            var front = art.FrontOf(column);
            var tile = World(front);
            if (top < 0 || !Mine(tile))
                continue;

            // What's walked under goes after whoever's on its tile, where what's walked on goes under them.
            var layer = !art.Walkable && !art.Blocks[front.Y][front.X] ? BoardLayer.Over : BoardLayer.Prop;
            var (min, max) = (frame + new Vector2(from, top), frame + new Vector2(to, bottom + 1));
            items.Add(new Face(BoardOrder.Key(tile, layer), tile, FaceKind.Building, Facing.U,
                [min, new Vector2(max.X, min.Y), max, new Vector2(min.X, max.Y)],
                [.. new[] { new Vector2(from, top), new Vector2(to, top), new Vector2(to, bottom + 1), new Vector2(from, bottom + 1) }.Select(p => p / art.Size)],
                art.Image, null, Lights: art.Lights, Part: part));
        }

        // Smoke rises from the roof, so it goes after all of the building. A sign hangs on one side, and only has to go after the columns behind it.
        foreach (var chimney in art.Smoke)
            if (art.SmokeArt is { } smoke && Mine(World((art.Width - 1, art.Depth - 1))))
                items.Add(new Sprite(BoardOrder.Key(World((art.Width - 1, art.Depth - 1)), BoardLayer.Over), World((art.Width - 1, art.Depth - 1)), smoke.Image, frame + chimney - smoke.Anchor, smoke.Size, Part: part));
        foreach (var root in art.Signs)
        {
            if (art.SignArt is not { } sign)
                continue;
            var at = frame + root - sign.Anchor;
            var (left, right) = (Column(art, root.X - sign.Anchor.X), Column(art, root.X - sign.Anchor.X + sign.Size.X - 1));
            var hung = Enumerable.Range(left, right - left + 1).Select(art.FrontOf).MaxBy(t => t.X + t.Y);
            if (Mine(World(hung)))
                items.Add(new Sprite(BoardOrder.Key(World(hung), BoardLayer.Over), World(hung), sign.Image, at, sign.Size, Part: part));
        }
    }

    private static int Column(BuildingArt art, float x) => (int)MathF.Floor((x - art.Origin.X) / ColumnWidth);

    /// <summary>The first and last rows with a pixel in the pixel columns from <paramref name="from"/> up to <paramref name="to"/>, or -1 if they're empty.</summary>
    private static (int Top, int Bottom) Span(BuildingShape shape, int from, int to)
    {
        var (top, bottom) = (-1, -1);
        for (var x = from; x < to; x++)
        {
            if (shape.Tops[x] >= 0)
            {
                top = top < 0 ? shape.Tops[x] : Math.Min(top, shape.Tops[x]);
                bottom = Math.Max(bottom, shape.Bottoms[x]);
            }
        }
        return (top, bottom);
    }

    /// <summary>
    /// The shadow, one face to a tile it falls on, in the ground's own place in the order, so nothing laid later hides it and it's under what stands there. Each face stands at its
    /// tile's height and samples the image where the tile would be level with the building, so it goes down steps with the ground.
    /// </summary>
    private static void AddShadow(List<BoardItem> items, BoardTerrain terrain, BuildingPlacement placed, BuildingArt art, Vector2 frame, Func<Tile, bool> mine)
    {
        if (art.Shadow is not { } shadow)
            return;
        foreach (var (dx, dy) in art.ShadowTiles)
        {
            if (dx >= 0 && dy >= 0 && dx < art.Width && dy < art.Depth)
                continue;      // the building stands there
            var tile = new Tile(placed.At.X + dx, placed.At.Y + dy);
            if (!mine(tile) || !terrain.Map.InBounds(tile) || terrain.Look(tile).Void)
                continue;
            var (x, y, top) = (tile.X, tile.Y, terrain.Top(tile));
            Vector2 Uv(int cornerX, int cornerY) => (BoardProjection.Corner(cornerX, cornerY, placed.Height) - frame) / art.Size;
            items.Add(new Face(BoardOrder.Key(tile, BoardLayer.Ground), tile, FaceKind.Shadow, Facing.Up,
                [BoardProjection.Corner(x, y, top), BoardProjection.Corner(x + 1, y, top), BoardProjection.Corner(x + 1, y + 1, top), BoardProjection.Corner(x, y + 1, top)],
                [Uv(x, y), Uv(x + 1, y), Uv(x + 1, y + 1), Uv(x, y + 1)], shadow, null, Lit: false));
        }
    }
}
