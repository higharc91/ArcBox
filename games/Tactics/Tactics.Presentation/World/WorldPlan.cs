using System.Collections.Immutable;
using System.Numerics;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>A box in art pixels.</summary>
public readonly record struct ArtArea(Vector2 Min, Vector2 Max)
{
    /// <summary>True when the two share more than an edge.</summary>
    public bool Overlaps(ArtArea other) => Min.X < other.Max.X && other.Min.X < Max.X && Min.Y < other.Max.Y && other.Min.Y < Max.Y;
}

/// <summary>One block of the world.</summary>
/// <param name="Index">Its place in <see cref="WorldPlan.Blocks"/>.</param>
/// <param name="Tiles">The tiles it holds.</param>
/// <param name="Z">The block column plus the block row, which puts nearer blocks in front. Blocks with the same Z draw in <see cref="Index"/> order.</param>
/// <param name="Plan">Its part of the board.</param>
/// <param name="Lit">That part in the region's light.</param>
public sealed record BlockPlan(int Index, TileRect Tiles, int Z, BoardPlan Plan, LitBoard Lit)
{
    /// <summary>Its middle, in tiles.</summary>
    public Vector2 Middle => new(Tiles.X + Tiles.W / 2f, Tiles.Y + Tiles.H / 2f);
}

/// <summary>
/// A region ready to draw: <see cref="BoardPlan"/>'s items cut into square blocks, each cached by the TV once, sharing one
/// <see cref="BoardLighting"/>, so a pool doesn't stop at a block's edge. Pure and safe to build off the main thread.
/// Items keep the region's own tiles, pixels and keys. Whole blocks can be drawn in <see cref="Blocks"/>' order and still agree with
/// the painter's order, because items that sort the other way across a block edge are three half-tiles apart on the screen, so
/// only a sprite over two tiles wide could overlap one, and the pack has none.
/// </summary>
public sealed class WorldPlan
{
    // A tile's top sits between these heights, in steps: a void sunk in a chasm, and a wall on the highest ground.
    private const float Lowest = -BoardTerrain.VoidSink;
    private const float Highest = WorldRegion.MaxHeight + BoardTerrain.WallRise;

    private static readonly int ChunkShift = BitOperations.Log2(WorldRegion.ChunkSize);   // a shift floors, for tiles above the map

    private WorldPlan(BoardTerrain terrain, ImmutableArray<BlockPlan> blocks, BoardLighting lighting, ImmutableArray<string> paths)
    {
        Terrain = terrain;
        Blocks = blocks;
        Lighting = lighting;
        Paths = paths;
    }

    /// <summary>The ground it was built from.</summary>
    public BoardTerrain Terrain { get; }

    /// <summary>Every block, in the order they're drawn.</summary>
    public ImmutableArray<BlockPlan> Blocks { get; }

    /// <summary>The one lighting of the whole region.</summary>
    public BoardLighting Lighting { get; }

    /// <summary>Every image the plan draws, once each, relative to the pack's root.</summary>
    public ImmutableArray<string> Paths { get; }

    /// <summary>The plan for <paramref name="terrain"/>, whose map's id names the region for its light.</summary>
    public static WorldPlan Build(BoardTerrain terrain, ArtIndex art, int blockSize = WorldTuning.BlockSize)
    {
        var (width, height) = (terrain.Map.Width, terrain.Map.Height);
        var cuts = new List<(int Column, int Row, TileRect Tiles)>();
        for (var y = 0; y < height; y += blockSize)
            for (var x = 0; x < width; x += blockSize)
                cuts.Add((x / blockSize, y / blockSize, new TileRect(x, y, Math.Min(blockSize, width - x), Math.Min(blockSize, height - y))));

        var inOrder = cuts.OrderBy(c => c.Column + c.Row).ThenBy(c => c.Column).ToArray();
        var plans = inOrder.Select(c => BoardPlan.BuildBlock(terrain, art, c.Tiles, grid: false)).ToArray();
        var lighting = BoardLighting.Of(plans, terrain.Map.Id, art.Lighting);
        ImmutableArray<BlockPlan> blocks = [.. inOrder.Select((c, i) => new BlockPlan(i, c.Tiles, c.Column + c.Row, plans[i], LitBoard.Build(plans[i], lighting)))];

        var images = plans.SelectMany(p => p.Items).Select(i => i is Face face ? face.Image?.Path : ((Sprite)i).Image.Path).OfType<string>();
        var halos = lighting.Lights.Select(l => l.Halo?.Path).OfType<string>();
        return new WorldPlan(terrain, blocks, lighting, [.. images.Concat(halos).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    /// <summary>
    /// The chunks a view of the map can show, as chunk column and row. A tile counts if its diamond, at any height it could be drawn at,
    /// crosses the view, so this holds for any ground.
    /// </summary>
    public static ImmutableArray<(int X, int Y)> ChunksIn(ArtArea view)
    {
        var (halfWidth, halfHeight, step) = (BoardProjection.HalfWidth, BoardProjection.HalfHeight, BoardProjection.HeightStep);
        var (firstAcross, lastAcross) = ((int)MathF.Floor(view.Min.X / halfWidth) - 1, (int)MathF.Ceiling(view.Max.X / halfWidth) + 1);
        var firstDown = (int)MathF.Floor((view.Min.Y + Lowest * step) / halfHeight) - 2;
        var lastDown = (int)MathF.Ceiling((view.Max.Y + Highest * step) / halfHeight);

        var chunks = new SortedSet<(int, int)>();
        for (var down = firstDown; down <= lastDown; down++)
            for (var across = firstAcross; across <= lastAcross; across++)
                if (((down + across) & 1) == 0)
                    chunks.Add((((down + across) >> 1) >> ChunkShift, ((down - across) >> 1) >> ChunkShift));
        return [.. chunks];
    }

    /// <summary>
    /// What's drawn over <paramref name="area"/> later than <paramref name="afterOrder"/>, in painter's order: what must go again over a unit
    /// standing there. It reads each item's real extent, so a tall side in front is found though its tile is off the area.
    /// </summary>
    public ImmutableArray<LitItem> ItemsOver(ArtArea area, long afterOrder)
    {
        var found = new List<LitItem>();
        foreach (var block in Blocks)
        {
            if (block.Plan.Items.IsEmpty || !new ArtArea(block.Plan.Min, block.Plan.Max).Overlaps(area))
                continue;
            foreach (var item in block.Lit.Items)
                if (item.Order > afterOrder && Covers(item, area))
                    found.Add(item);
        }
        return [.. found.OrderBy(i => i.Order)];
    }

    private static bool Covers(LitItem item, ArtArea area)
    {
        var (min, max) = BoardPlan.Bounds(item is LitFace face ? face.Face : ((LitSprite)item).Sprite);
        return new ArtArea(min, max).Overlaps(area);
    }
}
