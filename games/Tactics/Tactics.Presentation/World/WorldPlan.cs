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

/// <summary>An item a frame paints over the blocks, at <paramref name="Alpha"/>.</summary>
public readonly record struct OverItem(ItemAt At, float Alpha);

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
    private readonly Shape[][] _shapes;
    private readonly ItemGrid _grid;

    private WorldPlan(BoardTerrain terrain, ImmutableArray<BlockPlan> blocks, BoardLighting lighting, ImmutableArray<string> paths, ImmutableArray<PlannedImage> images, ImmutableArray<string> halos)
    {
        Terrain = terrain;
        Blocks = blocks;
        Lighting = lighting;
        Paths = paths;
        Images = images;
        Halos = halos;
        _shapes = [.. blocks.Select(b => b.Lit.Items.Select(ShapeOf).ToArray())];
        _grid = new ItemGrid(_shapes);
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
        var plans = inOrder.Select(c => BoardPlan.BuildBlock(terrain, art, c.Tiles, grid: false, world: true)).ToArray();
        var lighting = BoardLighting.Of(plans, terrain.Map.Id, art.Lighting);
        ImmutableArray<BlockPlan> blocks = [.. inOrder.Select((c, i) => new BlockPlan(i, c.Tiles, c.Column + c.Row, plans[i], LitBoard.Build(plans[i], lighting)))];

        ImmutableArray<PlannedImage> images =
        [
            .. plans.SelectMany(p => p.Items)
                .SelectMany(i => i is Face face ? [(face.Image, Flat: face.Flat), (Image: face.Lights, Flat: false)] : new[] { (Image: (ArtImage?)((Sprite)i).Image, Flat: false) })
                .Where(i => i.Image is not null)
                .DistinctBy(i => (i.Image!.Key, i.Flat))
                .OrderBy(i => i.Image!.Key, StringComparer.Ordinal)
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

    /// <summary>
    /// The blocks a change of height or sinking at <paramref name="touched"/> reaches, in <see cref="Blocks"/>' order. A tile's change reaches
    /// its neighbours' sides, and a sunk tile turns a deck beside it into a bridge, whose own neighbours' sides then come down further, so every
    /// block within two tiles counts (<see cref="StagePlan.WorldBlocks"/> builds them again).
    /// </summary>
    public ImmutableArray<BlockPlan> Near(IEnumerable<Tile> touched)
    {
        const int Spread = 2;
        var near = touched.SelectMany(t => Enumerable.Range(-Spread, 2 * Spread + 1)
            .SelectMany(dy => Enumerable.Range(-Spread, 2 * Spread + 1).Select(dx => new Tile(t.X + dx, t.Y + dy)))).ToHashSet();
        return [.. Blocks.Where(b => near.Any(b.Tiles.Contains))];
    }

    /// <summary>
    /// <paramref name="block"/> drawing <paramref name="plan"/> instead, in this plan's lighting, which a fight doesn't change: the light of a prop
    /// that sank stays (overworld handoff §6). The TV swaps it in for the block of the same <see cref="BlockPlan.Index"/>.
    /// </summary>
    public BlockPlan Relit(BlockPlan block, BoardPlan plan) => block with { Plan = plan, Lit = LitBoard.Build(plan, Lighting) };

    /// <summary>The blocks with something drawn in <paramref name="view"/>, in draw order.</summary>
    public ImmutableArray<BlockPlan> BlocksIn(ArtArea view) => [.. Blocks.Where(b => !b.Plan.Items.IsEmpty && new ArtArea(b.Plan.Min, b.Plan.Max).Overlaps(view))];

    /// <summary>What a unit covers: its sprite and the ring at its feet, facing either way. Its marks stand above everything, so they don't count.</summary>
    public static ArtArea AreaOf(BoardUnit unit) => BoundsOf(unit, bodyOnly: false);

    /// <summary>The visible body used for prop fading, excluding transparent animation padding.</summary>
    public static ArtArea BodyAreaOf(BoardUnit unit) => BoundsOf(unit, bodyOnly: true);

    private static ArtArea BoundsOf(BoardUnit unit, bool bodyOnly)
    {
        var sprite = unit.Sprite;
        var left = unit.Feet.X - (unit.Mirror ? sprite.Width - sprite.AnchorX : sprite.AnchorX) * sprite.PixelScale;
        var top = unit.Feet.Y - sprite.AnchorY * sprite.PixelScale;
        var right = left + sprite.WorldWidth;
        var bottom = top + sprite.WorldHeight;
        // High-resolution sheets reserve transparent room for weapons and gait. It must not fade a building.
        if (bodyOnly && sprite.FootprintWidth > 0)
        {
            left = unit.Feet.X - sprite.FootprintWidth / 2;
            right = unit.Feet.X + sprite.FootprintWidth / 2;
            top = unit.Feet.Y - sprite.HeadLift;
            bottom = unit.Feet.Y;
        }
        var ring = new Vector2(unit.Ring, unit.Ring / 2);
        return new ArtArea(Vector2.Min(new Vector2(left, top), unit.Feet - ring),
            Vector2.Max(new Vector2(right, bottom), unit.Feet + ring));
    }

    /// <summary>
    /// What's drawn over <paramref name="area"/> later than <paramref name="afterOrder"/>, in painter's order: what must go again over a unit
    /// standing there. It reads each item's real extent, so a tall side in front is found though its tile is off the area.
    /// </summary>
    public ImmutableArray<LitItem> ItemsOver(ArtArea area, long afterOrder) => [.. Over(area, afterOrder).Select(at => Blocks[at.Block].Lit.Items[at.Item])];

    /// <summary>As <see cref="ItemsOver"/>, as where each item is, for a frame to find what it prepared of them.</summary>
    public ImmutableArray<ItemAt> Over(ArtArea area, long afterOrder)
    {
        var (found, shape) = (new List<ItemAt>(), Shape.Of(area));
        Touching(shape, default, afterOrder, found.Add);
        return [.. Painted(found)];
    }

    /// <summary>
    /// What a frame must repaint over <paramref name="units"/>, in painter's order. An item is repainted whole, so what crosses a unit isn't enough: an item it
    /// doesn't cross can stand in front of a part of a repainted one, and would be painted over. So what crosses a repainted item, later than it, goes
    /// again too, until nothing more does. Items cross by the shapes they cover, not their boxes: on open ground every tile's box overlaps the next one's, and
    /// the closure would run away across the map, where their diamonds only meet. An item faded by <paramref name="fade"/> isn't repainted solid, and goes in with the
    /// units: it's out of its block and painted again in its place, so what stands in front of it has to go over it.
    /// </summary>
    /// <remarks>
    /// <paramref name="extra"/> are items drawn over the world as well (a secret door's plug of wall, <see cref="WallPlugs"/>): what stands in front of them goes
    /// again over them too.
    /// </remarks>
    public ImmutableArray<ItemAt> Overlay(IEnumerable<BoardUnit> units, PropFade? fade = null, IEnumerable<LitItem>? extra = null)
    {
        var (chosen, waiting) = (new HashSet<ItemAt>(), new Stack<ItemAt>());
        void Add(ItemAt at)
        {
            if (fade?.IsFaded(at) == true)
                return;
            if (chosen.Add(at))
                waiting.Push(at);
        }

        foreach (var unit in units)
            Touching(Shape.Of(AreaOf(unit)), default, unit.Order, Add);
        foreach (var item in extra ?? [])
            Touching(ShapeOf(item), default, item.Order, Add);
        if (fade is not null)
            foreach (var faded in fade.Faded)
                waiting.Push(fade.AtOf(faded));
        while (waiting.TryPop(out var at))
        {
            var shape = _shapes[at.Block][at.Item];
            Touching(shape, at, shape.Order, Add);
        }
        return [.. Painted(chosen)];
    }

    /// <summary>
    /// All a frame paints over the blocks, in painter's order, for merging with the units: <see cref="Overlay"/>, solid, and each item <paramref name="fade"/> has left out of its
    /// block, at its alpha, so a faded tree is over a walker behind it and under one in front. This is the whole of what decides what's over the blocks.
    /// </summary>
    public ImmutableArray<OverItem> PaintOver(IEnumerable<BoardUnit> units, PropFade? fade = null, IEnumerable<LitItem>? extra = null)
    {
        var solid = Overlay(units, fade, extra).Select(at => new OverItem(at, 1));
        if (fade is null || fade.Faded.Count == 0)
            return [.. solid];
        return [.. solid.Concat(fade.Faded.Select(i => new OverItem(fade.AtOf(i), fade.AlphaOf(i))))
            .OrderBy(o => OrderOf(o.At)).ThenBy(o => o.At.Block).ThenBy(o => o.At.Item)];
    }

    /// <summary>The place in the painter's order of the item <paramref name="at"/>.</summary>
    public long OrderOf(ItemAt at) => _shapes[at.Block][at.Item].Order;

    /// <summary>Finds what crosses <paramref name="shape"/> and paints after <paramref name="after"/>, an item of the given order, or anything of a later order.</summary>
    private void Touching(in Shape shape, ItemAt after, long order, Action<ItemAt> found)
    {
        var (left, top, right, bottom) = _grid.Cells(shape.Box);
        for (var row = top; row <= bottom; row++)
            for (var column = left; column <= right; column++)
                foreach (var at in _grid.At(column, row))
                {
                    var item = _shapes[at.Block][at.Item];
                    if (_grid.First(item.Box, column, row, left, top) && Later(item.Order, at.Block, at.Item, order, after) && item.Crosses(shape))
                        found(at);
                }
    }

    /// <summary>As <see cref="Over"/>, reading every item of every block: what the grid has to agree with, and what the world did before it had people in it.</summary>
    public ImmutableArray<ItemAt> OverByReadingEverything(ArtArea area, long afterOrder)
    {
        var (found, shape) = (new List<ItemAt>(), Shape.Of(area));
        foreach (var block in Blocks)
        {
            var shapes = _shapes[block.Index];
            for (var i = 0; i < shapes.Length; i++)
                if (Later(shapes[i].Order, block.Index, i, afterOrder, default) && shapes[i].Crosses(shape))
                    found.Add(new ItemAt(block.Index, i));
        }
        return [.. Painted(found)];
    }

    private static bool Later(long order, int block, int item, long afterOrder, ItemAt after)
        => order != afterOrder ? order > afterOrder : block != after.Block ? block > after.Block : item > after.Item;

    /// <summary>In painter's order. Items of one order keep theirs: a top's cells, then its glow and outline, are one order.</summary>
    private IEnumerable<ItemAt> Painted(IEnumerable<ItemAt> items) => items.OrderBy(OrderOf).ThenBy(at => at.Block).ThenBy(at => at.Item);

    private static Shape ShapeOf(LitItem item)
    {
        var drawn = item is LitFace face ? (BoardItem)face.Face : ((LitSprite)item).Sprite;
        var (min, max) = BoardPlan.Bounds(drawn);
        return new Shape(item.Order, new ArtArea(min, max), drawn is Face quad ? [.. quad.Points] : Shape.Corners(new ArtArea(min, max)));
    }

    /// <summary>
    /// Every item, by the cells of art pixels its box touches, so what crosses a shape is found from the few items round it and not all of a block's. The overlay asks this of every item
    /// it repaints as well as every unit, so with people about it's most of what a frame in town spends: 9.17 ms a frame, median, read the long way, and 3.87 ms like this. It's built once, off the
    /// main thread, with the plan, as what the plan draws doesn't move. An item is in each cell it touches and counts only in the first the two share, so nothing is found twice.
    /// </summary>
    private sealed class ItemGrid
    {
        private const int CellWidth = 128, CellHeight = 64;     // two tiles across, or two down

        private readonly ItemAt[][] _cells;
        private readonly int _left, _top, _columns, _rows;

        public ItemGrid(Shape[][] shapes)
        {
            var all = shapes.SelectMany(s => s).ToArray();
            if (all.Length == 0)
            {
                (_columns, _rows, _cells) = (1, 1, [[]]);
                return;
            }
            _left = Cell(all.Min(s => s.Box.Min.X), CellWidth);
            _top = Cell(all.Min(s => s.Box.Min.Y), CellHeight);
            _columns = Cell(all.Max(s => s.Box.Max.X), CellWidth) - _left + 1;
            _rows = Cell(all.Max(s => s.Box.Max.Y), CellHeight) - _top + 1;

            var cells = new List<ItemAt>?[_columns * _rows];
            for (var block = 0; block < shapes.Length; block++)
                for (var item = 0; item < shapes[block].Length; item++)
                {
                    var (left, top, right, bottom) = Cells(shapes[block][item].Box);
                    for (var row = top; row <= bottom; row++)
                        for (var column = left; column <= right; column++)
                            (cells[row * _columns + column] ??= []).Add(new ItemAt(block, item));
                }
            _cells = [.. cells.Select(c => c?.ToArray() ?? [])];
        }

        private static int Cell(float pixel, int size) => (int)MathF.Floor(pixel / size);

        /// <summary>The cells a box touches, on the grid.</summary>
        public (int Left, int Top, int Right, int Bottom) Cells(ArtArea box)
            => (Math.Clamp(Cell(box.Min.X, CellWidth) - _left, 0, _columns - 1), Math.Clamp(Cell(box.Min.Y, CellHeight) - _top, 0, _rows - 1),
                Math.Clamp(Cell(box.Max.X, CellWidth) - _left, 0, _columns - 1), Math.Clamp(Cell(box.Max.Y, CellHeight) - _top, 0, _rows - 1));

        public ItemAt[] At(int column, int row) => _cells[row * _columns + column];

        /// <summary>Whether (<paramref name="column"/>, <paramref name="row"/>) is the first cell an item with this box shares with a search that began at (<paramref name="left"/>, <paramref name="top"/>).</summary>
        public bool First(ArtArea box, int column, int row, int left, int top)
        {
            var (itemLeft, itemTop, _, _) = Cells(box);
            return column == Math.Max(left, itemLeft) && row == Math.Max(top, itemTop);
        }
    }

    /// <summary>What an item covers: a quad, or a sprite's or unit's rectangle, with its box for a quick look.</summary>
    private readonly record struct Shape(long Order, ArtArea Box, Vector2[] Points)
    {
        public static Shape Of(ArtArea area) => new(0, area, Corners(area));

        public static Vector2[] Corners(ArtArea area)
            => [area.Min, new Vector2(area.Max.X, area.Min.Y), area.Max, new Vector2(area.Min.X, area.Max.Y)];

        /// <summary>True when the two share more than an edge: no edge of either has the other wholly to its outside.</summary>
        public bool Crosses(in Shape other) => Box.Overlaps(other.Box) && !Apart(Points, other.Points) && !Apart(other.Points, Points);

        private static bool Apart(Vector2[] a, Vector2[] b)
        {
            for (var i = 0; i < a.Length; i++)
            {
                var edge = a[(i + 1) % a.Length] - a[i];
                var across = new Vector2(-edge.Y, edge.X);
                var (minA, maxA) = Range(a, across);
                var (minB, maxB) = Range(b, across);
                if (maxA <= minB + Touch || maxB <= minA + Touch)
                    return true;
            }
            return false;
        }

        private static (float Min, float Max) Range(Vector2[] points, Vector2 axis)
        {
            var (min, max) = (float.MaxValue, float.MinValue);
            foreach (var point in points)
            {
                var along = Vector2.Dot(point, axis);
                (min, max) = (Math.Min(min, along), Math.Max(max, along));
            }
            return (min, max);
        }

        private const float Touch = 1e-3f;
    }
}
