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

/// <summary>An image a plan draws.</summary>
/// <param name="Image">The image.</param>
/// <param name="Flat">It lies on the ground, turned, so the TV gets it through <see cref="DitherTexels"/>.</param>
public readonly record struct PlannedImage(ArtImage Image, bool Flat);

/// <summary>Where a lit item is in a plan: in <see cref="WorldPlan.Blocks"/>, and in that block's <see cref="LitBoard.Items"/>.</summary>
public readonly record struct ItemAt(int Block, int Item);

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

    private readonly (long Order, ArtArea Box)[][] _boxes;

    private WorldPlan(BoardTerrain terrain, ImmutableArray<BlockPlan> blocks, BoardLighting lighting, ImmutableArray<string> paths, ImmutableArray<PlannedImage> images, ImmutableArray<string> halos)
    {
        Terrain = terrain;
        Blocks = blocks;
        Lighting = lighting;
        Paths = paths;
        Images = images;
        Halos = halos;
        _boxes = [.. blocks.Select(b => b.Lit.Items.Select(i => (i.Order, BoxOf(i))).ToArray())];
    }

    /// <summary>The ground it was built from.</summary>
    public BoardTerrain Terrain { get; }

    /// <summary>Every block, in the order they're drawn.</summary>
    public ImmutableArray<BlockPlan> Blocks { get; private set; }

    /// <summary>The one lighting of the whole region.</summary>
    public BoardLighting Lighting { get; }

    /// <summary>Every image the plan draws, once each, relative to the pack's root.</summary>
    public ImmutableArray<string> Paths { get; }

    /// <summary>The images of its items, once each for each way they're used.</summary>
    public ImmutableArray<PlannedImage> Images { get; }

    /// <summary>The images of its lights' halos, once each, which <see cref="Images"/> doesn't have.</summary>
    public ImmutableArray<string> Halos { get; }

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

        ImmutableArray<PlannedImage> images =
        [
            .. plans.SelectMany(p => p.Items)
                .Select(i => i is Face face ? (face.Image, Flat: face.Facing == Facing.Up) : (((Sprite)i).Image, Flat: false))
                .Where(i => i.Image is not null)
                .DistinctBy(i => (i.Image!.Path, i.Flat))
                .OrderBy(i => i.Image!.Path, StringComparer.Ordinal)
                .ThenBy(i => i.Flat)
                .Select(i => new PlannedImage(i.Image!, i.Flat)),
        ];
        ImmutableArray<string> halos = [.. lighting.Lights.Select(l => l.Halo?.Path).OfType<string>().Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return new WorldPlan(terrain, blocks, lighting, [.. images.Select(i => i.Image.Path).Concat(halos).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)], images, halos);
    }

    /// <summary>
    /// Lets go of what each item read of the light, once its colours are baked. A region's probes are most of what its plan weighs, and nothing but
    /// baking reads them. Do this before the plan is shared: it isn't safe beside another thread's reads.
    /// </summary>
    public void DropProbes()
    {
        var blocks = ImmutableArray.CreateBuilder<BlockPlan>(Blocks.Length);
        foreach (var block in Blocks)
        {
            ImmutableArray<LitItem> items = [.. block.Lit.Items.Select(i => i switch
            {
                LitFace face => face with { Probes = [] },
                LitSprite sprite => sprite with { Probe = null },
                _ => i,
            })];
            blocks.Add(block with { Lit = block.Lit with { Items = items } });
        }
        Blocks = blocks.MoveToImmutable();
    }

    /// <summary>The blocks with something drawn in <paramref name="view"/>, in draw order.</summary>
    public ImmutableArray<BlockPlan> BlocksIn(ArtArea view) => [.. Blocks.Where(b => !b.Plan.Items.IsEmpty && new ArtArea(b.Plan.Min, b.Plan.Max).Overlaps(view))];

    /// <summary>What a unit covers: its sprite and the ring at its feet, facing either way. Its marks stand above everything, so they don't count.</summary>
    public static ArtArea AreaOf(BoardUnit unit)
    {
        var sprite = unit.Sprite;
        var left = unit.Feet.X - (unit.Mirror ? sprite.Width - sprite.AnchorX : sprite.AnchorX);
        var top = unit.Feet.Y - sprite.AnchorY;
        var ring = new Vector2(unit.Ring, unit.Ring / 2);
        return new ArtArea(Vector2.Min(new Vector2(left, top), unit.Feet - ring),
            Vector2.Max(new Vector2(left + sprite.Width, top + sprite.Height), unit.Feet + ring));
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
    public ImmutableArray<LitItem> ItemsOver(ArtArea area, long afterOrder) => [.. Over(area, afterOrder).Select(at => Blocks[at.Block].Lit.Items[at.Item])];

    /// <summary>As <see cref="ItemsOver"/>, as where each item is, for a frame to find what it prepared of them.</summary>
    public ImmutableArray<ItemAt> Over(ArtArea area, long afterOrder)
    {
        var found = new List<(long Order, ItemAt At)>();
        foreach (var block in Blocks)
        {
            if (block.Plan.Items.IsEmpty || !new ArtArea(block.Plan.Min, block.Plan.Max).Overlaps(area))
                continue;
            var boxes = _boxes[block.Index];
            for (var i = 0; i < boxes.Length; i++)
                if (boxes[i].Order > afterOrder && boxes[i].Box.Overlaps(area))
                    found.Add((boxes[i].Order, new ItemAt(block.Index, i)));
        }
        return [.. found.OrderBy(f => f.Order).Select(f => f.At)];
    }

    private static ArtArea BoxOf(LitItem item)
    {
        var (min, max) = BoardPlan.Bounds(item is LitFace face ? face.Face : ((LitSprite)item).Sprite);
        return new ArtArea(min, max);
    }
}
