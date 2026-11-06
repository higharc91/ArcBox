using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;


namespace Tactics.Presentation.Tests.World;

/// <summary>
/// The whole picture, not a rule about it: for a party walking a real route, what's on screen (every block as it was recorded, then the frame's overlay) is
/// rastered in art pixels and held against true painter's order. The shape each thing covers stands in for its pixels, opaque all the way to its edge, which is the
/// harshest case: a repainted item shows at once wherever it's wrong.
/// </summary>
public sealed class OverlayPictureTests
{
    private const int Crop = 96;    // art pixels round the party: further than anything repainted can reach and still be tested

    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));

    private readonly record struct Shape(int Id, long Order, Vector2 Min, Vector2 Max, Vector2[]? Quad, ItemAt At = default, bool[]? Solid = null, int Width = 0);

    private sealed record Sheet(WorldPlan Plan, Shape[] All, Dictionary<ItemAt, Shape> Of);

    /// <summary>What's on screen, each shape to its edge, or with <paramref name="masked"/> only the pixels its art has.</summary>
    private static Sheet Read(WorldPlan plan, bool masked = false)
    {
        var all = new List<Shape>();
        var of = new Dictionary<ItemAt, Shape>();
        foreach (var block in plan.Blocks)
            for (var i = 0; i < block.Lit.Items.Length; i++)
            {
                var item = block.Lit.Items[i];
                var (min, max) = BoardPlan.Bounds(item is LitFace f ? f.Face : ((LitSprite)item).Sprite);
                var (solid, width) = masked && item is LitSprite sprite ? Mask(sprite.Sprite.Image) : (null, 0);
                var shape = new Shape(all.Count + 1, item.Order, min, max, item is LitFace face ? [.. face.Face.Points] : null, new ItemAt(block.Index, i), solid, width);
                all.Add(shape);
                of[new ItemAt(block.Index, i)] = shape;
            }
        return new Sheet(plan, [.. all], of);
    }

    private static readonly Dictionary<string, (bool[] Solid, int Width)> Masks = [];

    /// <summary>Which pixels of an image's first frame have anything in them.</summary>
    private static (bool[]? Solid, int Width) Mask(ArtImage image)
    {
        lock (Masks)
        {
            if (Masks.TryGetValue(image.Path, out var known))
                return known;
            var (rgba, width, height) = PngTexels.Read(Path.Combine(Pack.Root, image.Path));
            var solid = new bool[width * height];
            for (var i = 0; i < solid.Length; i++)
                solid[i] = rgba[i * 4 + 3] >= 128;
            return Masks[image.Path] = (solid, width);
        }
    }

    private static void Paint(int[] buffer, Vector2 corner, int size, in Shape shape)
        => Paint(buffer, corner, size, size, shape);

    private static void Paint(int[] buffer, Vector2 corner, int size, int rows, in Shape shape)
    {
        var from = Vector2.Max(shape.Min - corner, Vector2.Zero);
        var to = Vector2.Min(shape.Max - corner, new Vector2(size, rows));
        for (var y = (int)from.Y; y < to.Y; y++)
            for (var x = (int)from.X; x < to.X; x++)
            {
                var at = new Vector2(x + 0.5f, y + 0.5f) + corner;
                if (at.X >= shape.Min.X && at.X < shape.Max.X && at.Y >= shape.Min.Y && at.Y < shape.Max.Y && (shape.Quad is null || Inside(shape.Quad, at))
                    && (shape.Solid is null || shape.Solid[(int)(at.Y - shape.Min.Y) * shape.Width + (int)(at.X - shape.Min.X)]))
                    buffer[y * size + x] = shape.Id;
            }
    }

    private static bool Inside(Vector2[] quad, Vector2 at)
    {
        var (above, below) = (false, false);
        for (var i = 0; i < quad.Length; i++)
        {
            var (a, b) = (quad[i], quad[(i + 1) % quad.Length]);
            var side = (b.X - a.X) * (at.Y - a.Y) - (b.Y - a.Y) * (at.X - a.X);
            above |= side > 0;
            below |= side < 0;
        }
        return !(above && below);
    }

    private static Shape UnitShape(BoardUnit unit, int index)
    {
        var area = WorldPlan.AreaOf(unit);
        return new Shape(-1 - index, unit.Order, area.Min, area.Max, null);
    }

    /// <summary>Paints <paramref name="items"/>, in order, with <paramref name="units"/> where they sort among them.</summary>
    private static void PaintWith(int[] buffer, Vector2 corner, int size, Shape[] items, Shape[] units)
    {
        var at = 0;
        foreach (var item in items)
        {
            while (at < units.Length && units[at].Order <= item.Order)
                Paint(buffer, corner, size, units[at++]);
            Paint(buffer, corner, size, item);
        }
        while (at < units.Length)
            Paint(buffer, corner, size, units[at++]);
    }

    /// <summary>The pixels this frame gets wrong, for a party at <paramref name="units"/>.</summary>
    private static int Wrong(Sheet sheet, ImmutableArray<BoardUnit> units, IReadOnlyList<ItemAt> over, int[] drawn, int[] truth, Func<ItemAt, bool>? hidden = null)
    {
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var unit in units)
        {
            var area = WorldPlan.AreaOf(unit);
            (min, max) = (Vector2.Min(min, area.Min), Vector2.Max(max, area.Max));
        }
        var corner = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y)) - new Vector2(Crop);
        var size = (int)Math.Max(max.X - min.X, max.Y - min.Y) + 2 * Crop;
        if (size * size > drawn.Length)
            size = (int)Math.Sqrt(drawn.Length);
        Array.Clear(drawn);
        var near = sheet.All.Where(s => s.Max.X > corner.X && s.Min.X < corner.X + size && s.Max.Y > corner.Y && s.Min.Y < corner.Y + size).ToArray();
        foreach (var item in near)                     // the blocks, each as it was recorded, in their own order, less what's faded
            if (hidden?.Invoke(item.At) != true)
                Paint(drawn, corner, size, item);
        var shapes = units.Select(UnitShape).OrderBy(u => u.Order).ToArray();
        PaintWith(drawn, corner, size, [.. over.Select(at => sheet.Of[at])], shapes);

        Array.Clear(truth);
        PaintWith(truth, corner, size, [.. near.OrderBy(s => s.Order).ThenBy(s => s.Id)], shapes);

        var wrong = 0;
        for (var i = 0; i < size * size; i++)
            wrong += drawn[i] != truth[i] ? 1 : 0;
        return wrong;
    }

    private static ImmutableArray<WalkerView> Party(int count)
        => [.. Enumerable.Range(0, count).Select(i => new WalkerView(WalkFeedId(i), WalkFeedId(i), "characters.class_rogue", true, i == 0))];

    private static string WalkFeedId(int i) => ((char)('a' + i)).ToString();

    /// <summary>Walks three walkers down <paramref name="route"/>, each a little behind the one in front.</summary>
    private static (int WrongFrames, int WorstPixels, double MeanItems, int MostItems) Sweep(Sheet sheet, ImmutableArray<Tile> route, float step, ITestOutputHelper? output, Func<ImmutableArray<BoardUnit>, IReadOnlyList<ItemAt>> overlay)
    {
        var terrain = sheet.Plan.Terrain;
        var (drawn, truth) = (new int[800 * 800], new int[800 * 800]);
        var party = Party(3);
        var pose = new Dictionary<string, UnitPose>();
        var (wrongFrames, worst, frames, items, most) = (0, 0, 0, 0L, 0);
        for (var at = 3f; at < route.Length - 1; at += step)
        {
            var places = new Dictionary<string, UnitPlace>
            {
                ["a"] = WorldRoute.At(terrain, route, at),
                ["b"] = WorldRoute.At(terrain, route, at - 1),
                ["c"] = WorldRoute.At(terrain, route, at - 2),
            };
            var units = UnitPlan.Walkers(party, places, pose, Art.Value);
            var over = overlay(units);
            var wrong = Wrong(sheet, units, over, drawn, truth);
            frames++;
            items += over.Count;
            most = Math.Max(most, over.Count);
            worst = Math.Max(worst, wrong);
            wrongFrames += wrong > 0 ? 1 : 0;
            if (wrong > 0 && wrongFrames < 4)
                output?.WriteLine($"step {at}: {wrong} px wrong at {places["a"].Map}");
        }
        return (wrongFrames, worst, items / (double)frames, most);
    }

    /// <summary>
    /// Plays <paramref name="frames"/> of a party, with what it fades left out of the blocks and put back in order in the frame's overlay, as the TV does. Each item is
    /// opaque here, so a faded item is held to the same order as a solid one: what's in front of it stays in front, and what's behind it stays behind.
    /// </summary>
    private static (int WrongFrames, int WorstPixels, int FadedFrames) Faded(Sheet sheet, IEnumerable<IReadOnlyDictionary<string, UnitPlace>> frames, double seconds, ITestOutputHelper? output = null)
    {
        var fade = new PropFade(sheet.Plan);
        var (drawn, truth) = (new int[800 * 800], new int[800 * 800]);
        var (party, pose) = (Party(3), new Dictionary<string, UnitPose>());
        var (wrongFrames, worst, faded) = (0, 0, 0);
        foreach (var places in frames)
        {
            var units = UnitPlan.Walkers(party, places, pose, Art.Value);
            fade.Update(units, seconds);
            var over = sheet.Plan.PaintOver(units, fade);
            var wrong = Wrong(sheet, units, [.. over.Select(o => o.At)], drawn, truth, fade.IsFaded);
            worst = Math.Max(worst, wrong);
            faded += over.Any(o => o.Alpha < 1) ? 1 : 0;
            wrongFrames += wrong > 0 ? 1 : 0;
            if (wrong > 0 && wrongFrames < 4)
                output?.WriteLine($"{wrong} px wrong at {places["a"].Map}");
        }
        return (wrongFrames, worst, faded);
    }

    private static IEnumerable<IReadOnlyDictionary<string, UnitPlace>> Walked(BoardTerrain terrain, ImmutableArray<Tile> route, float step)
    {
        for (var at = 3f; at < route.Length - 1; at += step)
            yield return new Dictionary<string, UnitPlace>
            {
                ["a"] = WorldRoute.At(terrain, route, at),
                ["b"] = WorldRoute.At(terrain, route, at - 1),
                ["c"] = WorldRoute.At(terrain, route, at - 2),
            };
    }

    private static readonly Lazy<WorldGround> FordGround = new(() => Pack.Ground("ashen_ford"));
    private static readonly Lazy<Sheet> Ford = new(() => Read(WorldPlan.Build(new BoardTerrain(FordGround.Value.Map, null), Pack.Art)));
    private static readonly Lazy<(WorldGround Ground, Sheet Sheet)> Hilly = new(() =>
    {
        var ground = DemoRegion.Hilly(160, 120);
        return (ground, Read(WorldPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art)));
    });

    private readonly ITestOutputHelper _output;

    public OverlayPictureTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_walk_across_the_ashen_ford_comes_out_as_true_painters_order()
    {
        var route = WorldRoute.Across(FordGround.Value.Terrain, new Tile(30, 65), ClassDef.DefaultJump);

        var (wrong, worst, mean, most) = Sweep(Ford.Value, route, 0.5f, _output, u => Ford.Value.Plan.Overlay(u));

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px, mean {mean:0.0} items, most {most}");
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void A_walk_across_hilly_ground_comes_out_as_true_painters_order()
    {
        var (ground, sheet) = Hilly.Value;
        var route = WorldRoute.Across(ground.Terrain, new Tile(80, 60), ClassDef.DefaultJump);

        var (wrong, worst, mean, most) = Sweep(sheet, route, 1f, _output, u => sheet.Plan.Overlay(u));

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px, mean {mean:0.0} items, most {most}");
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void The_whole_ford_as_its_blocks_record_it_is_true_painters_order()
    {
        var sheet = Read(WorldPlan.Build(new BoardTerrain(FordGround.Value.Map, null), Pack.Art), masked: true);
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var shape in sheet.All)
            (min, max) = (Vector2.Min(min, shape.Min), Vector2.Max(max, shape.Max));
        var corner = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y));
        var (width, height) = ((int)(max.X - corner.X) + 2, (int)(max.Y - corner.Y) + 2);
        var (drawn, truth) = (new int[width * height], new int[width * height]);

        foreach (var shape in sheet.All)
            Paint(drawn, corner, width, height, shape);
        foreach (var shape in sheet.All.OrderBy(s => s.Order).ThenBy(s => s.Id))
            Paint(truth, corner, width, height, shape);

        Assert.Equal(0, drawn.Zip(truth).Count(p => p.First != p.Second));
    }

    [Fact]
    public void A_walk_across_the_ford_fading_what_it_passes_comes_out_as_true_painters_order()
    {
        var route = WorldRoute.Across(FordGround.Value.Terrain, new Tile(30, 65), ClassDef.DefaultJump);

        var (wrong, worst, faded) = Faded(Ford.Value, Walked(Ford.Value.Plan.Terrain, route, 0.25f), 0.03, _output);

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px, {faded} frames with a fade");
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void Walks_past_trees_one_tile_beside_them_come_out_as_true_painters_order()
    {
        var (terrain, plan) = (FordGround.Value.Terrain, Ford.Value.Plan);
        var trees = plan.Blocks.SelectMany(b => b.Plan.Items).OfType<Sprite>().Where(s => s.IsTallProp).Select(s => s.Tile).Distinct().ToArray();
        var routes = trees
            .Select(tree => Enumerable.Range(-5, 11).Select(d => new Tile(tree.X + 1, tree.Y + d)).ToImmutableArray())
            .Where(line => line.All(terrain.Walkable)).Take(25).ToArray();
        Assert.NotEmpty(routes);

        var (wrong, worst, faded) = (0, 0, 0);
        foreach (var route in routes)
        {
            var result = Faded(Ford.Value, Walked(plan.Terrain, route, 0.11f), 0.03, _output);
            (wrong, worst, faded) = (wrong + result.WrongFrames, Math.Max(worst, result.WorstPixels), faded + result.FadedFrames);
        }

        _output.WriteLine($"{routes.Length} walks: {wrong} wrong frames, worst {worst} px, {faded} frames with a fade");
        Assert.True(faded > 0);
        Assert.Equal(0, wrong);
    }

    /// <summary>A tree on the ford with room to stand behind it and in front of it.</summary>
    private static Tile StandingTree()
    {
        var terrain = FordGround.Value.Terrain;
        return Ford.Value.Plan.Blocks.SelectMany(b => b.Plan.Items).OfType<Sprite>().Where(s => s.IsTallProp && s.Image.Path.Contains("tree", StringComparison.Ordinal)).Select(s => s.Tile)
            .First(t => terrain.Walkable(t with { Y = t.Y - 1 }) && terrain.Walkable(t with { Y = t.Y + 1 }));
    }

    private static IEnumerable<IReadOnlyDictionary<string, UnitPlace>> Standing(int frames, Tile a, Tile b, Tile c)
    {
        var terrain = Ford.Value.Plan.Terrain;
        var places = new Dictionary<string, UnitPlace> { ["a"] = UnitPlace.On(terrain, a), ["b"] = UnitPlace.On(terrain, b), ["c"] = UnitPlace.On(terrain, c) };
        return Enumerable.Repeat(places, frames);
    }

    [Fact]
    public void A_party_stopped_behind_a_tree_comes_out_as_true_painters_order()
    {
        var tree = StandingTree();
        var behind = tree with { Y = tree.Y - 1 };

        var (wrong, worst, faded) = Faded(Ford.Value, Standing(30, behind, behind, behind), 1 / 60.0, _output);

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px");
        Assert.Equal(30, faded);
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void A_party_split_across_a_tree_keeps_the_one_in_front_clear_of_it()
    {
        var tree = StandingTree();
        var (behind, front) = (tree with { Y = tree.Y - 1 }, tree with { Y = tree.Y + 1 });

        var (wrong, worst, faded) = Faded(Ford.Value, Standing(30, behind, front, behind), 1 / 60.0, _output);

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px");
        Assert.Equal(30, faded);
        Assert.Equal(0, wrong);
    }

    private static readonly Lazy<(WorldGround Ground, Sheet Sheet)> Tavern = new(() =>
    {
        var ground = DemoRegion.Tavern(Pack.Root);
        return (ground, Read(WorldPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art)));
    });

    [Fact]
    public void The_tavern_is_really_drawn_and_blocks_where_its_manifest_says()
    {
        var (ground, sheet) = Tavern.Value;

        Assert.Contains(sheet.Plan.Blocks.SelectMany(b => b.Plan.Items), i => i is Face { Kind: FaceKind.Building });
        Assert.All(DemoRegion.TavernWalk(), t => Assert.True(ground.Terrain.Walkable(t), $"{t}"));
    }

    [Fact]
    public void A_walk_all_the_way_round_a_tavern_comes_out_as_true_painters_order()
    {
        var (_, sheet) = Tavern.Value;

        var (wrong, worst, mean, most) = Sweep(sheet, DemoRegion.TavernWalk(), 0.11f, _output, u => sheet.Plan.Overlay(u));

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px, mean {mean:0.0} items, most {most}");
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void A_walk_round_a_tavern_that_fades_it_comes_out_as_true_painters_order()
    {
        var (_, sheet) = Tavern.Value;

        var (wrong, worst, faded) = Faded(sheet, Walked(sheet.Plan.Terrain, DemoRegion.TavernWalk(), 0.11f), 0.03, _output);

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px, {faded} frames with a fade");
        Assert.True(faded > 0);
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void A_party_split_across_a_tavern_keeps_the_one_in_front_clear_of_it()
    {
        var (_, sheet) = Tavern.Value;
        var terrain = sheet.Plan.Terrain;
        var (behind, front) = (UnitPlace.On(terrain, new Tile(15, 13)), UnitPlace.On(terrain, new Tile(15, 16)));
        var places = new Dictionary<string, UnitPlace> { ["a"] = behind, ["b"] = front, ["c"] = behind };

        var (wrong, worst, faded) = Faded(sheet, Enumerable.Repeat(places, 30), 1 / 60.0, _output);

        _output.WriteLine($"{wrong} wrong frames, worst {worst} px");
        Assert.Equal(30, faded);
        Assert.Equal(0, wrong);
    }

    [Fact]
    public void The_tavern_as_its_blocks_record_it_is_true_painters_order_where_its_art_has_pixels()
    {
        var sheet = Read(WorldPlan.Build(new BoardTerrain(Tavern.Value.Ground.Map, null), Pack.Art), masked: true);
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var shape in sheet.All)
            (min, max) = (Vector2.Min(min, shape.Min), Vector2.Max(max, shape.Max));
        var corner = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y));
        var (width, height) = ((int)(max.X - corner.X) + 2, (int)(max.Y - corner.Y) + 2);
        var (drawn, truth) = (new int[width * height], new int[width * height]);

        foreach (var shape in sheet.All)
            Paint(drawn, corner, width, height, shape);
        foreach (var shape in sheet.All.OrderBy(s => s.Order).ThenBy(s => s.Id))
            Paint(truth, corner, width, height, shape);

        Assert.Equal(0, drawn.Zip(truth).Count(p => p.First != p.Second));
    }

    public static TheoryData<string> Buildings => [.. Pack.Manifests.Buildings.Keys.Order(StringComparer.Ordinal)];

    /// <summary>Blocks draw in their own order, which holds only for what isn't too wide, so every building is tried across the corner of four blocks, with things in front.</summary>
    [Theory]
    [MemberData(nameof(Buildings))]
    public void A_building_across_a_block_corner_is_true_painters_order_where_its_art_has_pixels(string id)
    {
        const int side = 32;
        var (width, depth) = (Pack.Manifests.Buildings[id].Width, Pack.Manifests.Buildings[id].Depth);
        var at = new Tile(16 - width + 1, 16 - depth + 1);
        ImmutableArray<PropPlacement> props =
        [
            new("crate", new Tile(at.X + width, at.Y + depth), true, Cover.None), new("tree", new Tile(at.X + width + 1, at.Y), true, Cover.None),
            new("tree", new Tile(at.X, at.Y + depth + 1), true, Cover.None), new("lamp_post", new Tile(at.X - 1, at.Y - 1), true, Cover.None),
        ];
        var map = BattleMap.FromBytes("corner", side, side, new byte[side * side * 3], ["grass/autumn"], props, ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty, null,
            [new BuildingPlacement(id, Pack.Manifests.Buildings[id].Variants[0], at, 0)]);
        var sheet = Read(WorldPlan.Build(new BoardTerrain(map, null), Pack.Art), masked: true);
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var shape in sheet.All)
            (min, max) = (Vector2.Min(min, shape.Min), Vector2.Max(max, shape.Max));
        var corner = new Vector2(MathF.Floor(min.X), MathF.Floor(min.Y));
        var (columns, rows) = ((int)(max.X - corner.X) + 2, (int)(max.Y - corner.Y) + 2);
        var (drawn, truth) = (new int[columns * rows], new int[columns * rows]);

        foreach (var shape in sheet.All)
            Paint(drawn, corner, columns, rows, shape);
        foreach (var shape in sheet.All.OrderBy(s => s.Order).ThenBy(s => s.Id))
            Paint(truth, corner, columns, rows, shape);

        Assert.Equal(0, drawn.Zip(truth).Count(p => p.First != p.Second));
    }

    /// <summary>The ford's timber bridge (OW-08), in the world and on the fight's own map: its pieces, and the tiles of the map they lie over.</summary>
    private static (BoardItem[] Pieces, BoardTerrain Terrain, Func<Tile, bool> Walkable) Bridge(bool world)
    {
        // Keep OW-08's original oversized-texture regression independently of
        // the new authored ford. Its library sprite remains supported.
        var map = BattleMap.FromBytes("legacy_bridge", 9, 9, new byte[9*9*3], ["grass/autumn"],
            [new PropPlacement("bridge_span_timber",new Tile(5,5),false,Cover.None)], ImmutableSortedDictionary<string,ImmutableArray<Tile>>.Empty);
        var terrain = new BoardTerrain(map, null);
        var logic = new Terrain(map, ImmutableSortedDictionary<Tile, int>.Empty, []);
        var items = world ? WorldPlan.Build(terrain, Pack.Art).Blocks.SelectMany(b => b.Plan.Items) : BoardPlan.Build(terrain, Pack.Art).Items;
        var pieces = items.Where(i => (i is Face face ? face.Image : (i as Sprite)?.Image)?.Path.Contains("bridge_timber", StringComparison.Ordinal) == true).ToArray();
        Assert.NotEmpty(pieces);
        return (pieces, terrain, logic.Walkable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_fords_bridge_is_drawn_whole_at_its_manifests_size(bool world)
    {
        var (pieces, _, _) = Bridge(world);
        var (min, max) = (new Vector2(float.MaxValue), new Vector2(float.MinValue));
        foreach (var piece in pieces)
        {
            var (from, to) = BoardPlan.Bounds(piece);
            (min, max) = (Vector2.Min(min, from), Vector2.Max(max, to));
            if (piece is Face face)
                Assert.All(face.Uvs, uv => Assert.True(uv.X is >= 0 and <= 1 && uv.Y is >= 0 and <= 1, $"{uv}"));
        }

        // The art is 192 x 128 for a 96 x 64 prop: drawn as 96 x 64 of its texels, it showed a quarter, cut off straight down and across.
        Assert.Equal(96, max.X - min.X, 0.01);
        Assert.All(pieces, p => Assert.IsType<Face>(p));
        var across = pieces.Cast<Face>().Select(f => (f.Uvs[0].X, f.Uvs[1].X)).Distinct().Sum(u => u.Item2 - u.Item1);
        Assert.InRange(across, 0.97f, 1.0001f);
        Assert.InRange(pieces.Cast<Face>().Max(f => f.Uvs.Max(uv => uv.Y)) - pieces.Cast<Face>().Min(f => f.Uvs.Min(uv => uv.Y)), 0.97f, 1.0001f);
    }

    /// <summary>Whether the bridge's art has a pixel at <paramref name="at"/> in <paramref name="piece"/>, which is 96 x 64 art pixels whole.</summary>
    private static bool Covers(BoardItem piece, Vector2 at)
    {
        var size = new Vector2(96, 64);
        var (quad, uv0, image) = piece is Face face ? ([.. face.Points], face.Uvs[0], face.Image!) : (new[] { ((Sprite)piece).TopLeft, ((Sprite)piece).TopLeft + new Vector2(96, 0), ((Sprite)piece).TopLeft + size, ((Sprite)piece).TopLeft + new Vector2(0, 64) }, Vector2.Zero, ((Sprite)piece).Image);
        if (!Inside(quad, at))
            return false;
        var uv = (at - (quad[0] - uv0 * size)) / size;
        var (solid, width) = Mask(image);
        var height = solid!.Length / width;
        var (x, y) = ((int)(uv.X * width), (int)(uv.Y * height));
        return x >= 0 && y >= 0 && x < width && y < height && solid[y * width + x];
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_unit_on_the_fords_bridge_stands_on_it_and_one_behind_is_hidden_by_it(bool world)
    {
        var (pieces, terrain, walkable) = Bridge(world);
        // It stands on its tile's middle and reaches back along U over the ford, from bank to bank.
        var own = pieces.MaxBy(p => p.Tile.X)!.Tile;
        var under = Enumerable.Range(0, 4).Select(k => own with { X = own.X - k }).Where(walkable).ToArray();
        Assert.True(under.Length >= 3, $"the bridge lies over {under.Length} walkable tiles");

        // What's at a unit's feet, in the middle of its tile, is under it, unless it's on the next tile in front and higher: the bank's ledge, with the bridge's end on it.
        var party = Party(1);
        foreach (var tile in under)
        {
            var unit = UnitPlan.Walkers(party, new Dictionary<string, UnitPlace> { ["a"] = UnitPlace.On(terrain, tile) }, new Dictionary<string, UnitPose>(), Art.Value)[0];
            var feet = Enumerable.Range(-3, 7).SelectMany(dx => Enumerable.Range(-10, 11).Select(dy => unit.Feet + new Vector2(dx, dy))).ToArray();
            bool Ledge(Tile t) => (t == tile with { X = tile.X + 1 } || t == tile with { Y = tile.Y + 1 }) && terrain.Top(t) > terrain.Top(tile);
            var over = pieces.Where(p => p.Order > unit.Order && !Ledge(p.Tile) && feet.Any(f => Covers(p, f))).Select(p => p.Tile).ToArray();
            Assert.True(over.Length == 0, $"a unit on {tile} is under the bridge's piece on {string.Join(", ", over)}");
        }

        // Behind it, a tile back from its middle, a unit is hidden where the bridge is in front: the planks still go over it.
        var middle = under.OrderBy(t => t.X).ThenBy(t => t.Y).ElementAt(under.Length / 2);
        var behind = new Tile(middle.X, middle.Y - 1);
        var back = UnitPlan.Walkers(party, new Dictionary<string, UnitPlace> { ["a"] = UnitPlace.On(terrain, behind) }, new Dictionary<string, UnitPose>(), Art.Value)[0];
        var area = WorldPlan.AreaOf(back);
        var body = Enumerable.Range((int)area.Min.X, (int)(area.Max.X - area.Min.X)).SelectMany(x => Enumerable.Range((int)area.Min.Y, (int)(area.Max.Y - area.Min.Y)).Select(y => new Vector2(x + 0.5f, y + 0.5f))).ToArray();
        Assert.Contains(pieces, p => p.Order > back.Order && body.Any(b => Covers(p, b)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Native_walkable_ramp_surfaces_never_cover_feet_in_the_world_or_battle(bool world)
    {
        var flags=new HashSet<string>{"ch1_ford_settled","ch1_skarrow_slain"};
        var map=world?Pack.Ground("ashen_ford").Map:Pack.Map("river_crossing");
        var terrain=new BoardTerrain(map,null);
        var pieces=world?BoardPlan.StoryProps(terrain,Pack.Art,flags).Items:BoardPlan.Build(terrain,Pack.Art,flags).Items;
        var walkable = new Terrain(map.WithStoryFlags(flags), ImmutableSortedDictionary<Tile,int>.Empty, []);
        // Walls in front may legitimately hide feet. A walkable surface may not.
        var images=pieces.OfType<Sprite>().Where(p=>walkable.Walkable(p.Tile) && p.Image.Path.Contains("props/ford/",StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(images);
        foreach(var y in new[]{9,10})foreach(var x in new[]{3,4,8,9})
        {
            var tile=new Tile(x+(world?100:0),y+(world?52:0));
            var place=UnitPlace.On(terrain,tile);
            var unit=UnitPlan.Walkers(Party(1),new Dictionary<string,UnitPlace>{{"a",place}},new Dictionary<string,UnitPose>(),Art.Value)[0];
            foreach(var dx in Enumerable.Range(-2,5))
            {
                var point=place.Feet+new Vector2(dx,-3);
                var wrong=images.Where(p=>p.Order>unit.Order&&NativeCovers(p,point)).Select(p=>p.Image.Path).ToArray();
                Assert.True(wrong.Length==0,$"Native art over feet on {tile}: {string.Join(',',wrong)}");
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_real_parapet_hides_feet_behind_its_corner_but_not_the_upper_body(bool world)
    {
        var flags=new HashSet<string>{"ch1_ford_settled","ch1_skarrow_slain"};
        var map=world?Pack.Ground("ashen_ford").Map:Pack.Map("river_crossing");
        var terrain=new BoardTerrain(map,null);
        var pieces=world?BoardPlan.StoryProps(terrain,Pack.Art,flags).Items:BoardPlan.Build(terrain,Pack.Art,flags).Items;
        var tile=new Tile(4+(world?100:0),10+(world?52:0));
        var place=UnitPlace.On(terrain,tile);
        var unit=UnitPlan.Walkers(Party(1),new Dictionary<string,UnitPlace>{{"a",place}},new Dictionary<string,UnitPose>(),Art.Value)[0];
        var walls=pieces.OfType<Sprite>().Where(p=>map[p.Tile].Flags.HasFlag(TileFlags.Impassable)&&p.Order>unit.Order).ToArray();
        Assert.Contains(walls,p=>NativeCovers(p,place.Feet+new Vector2(0,-3)));
        Assert.DoesNotContain(walls,p=>NativeCovers(p,place.Feet+new Vector2(0,-16)));
    }

    private static bool NativeCovers(Sprite sprite,Vector2 point)
    {
        var at=point-sprite.TopLeft;var x=(int)MathF.Floor(at.X);var y=(int)MathF.Floor(at.Y);
        var (solid,width)=Mask(sprite.Image);
        return x>=0&&y>=0&&x<width&&y<solid!.Length/width&&solid[y*width+x];
    }

    /// <summary>A field with a haystack at (8, 8) and a goat at <paramref name="goat"/>, which never leaves home in a test that doesn't step the herd.</summary>
    private static (Sheet Sheet, WorldHerd Herd) Haystack(Tile goat)
    {
        const int side = 16;
        ImmutableArray<PropPlacement> props = [new("haystack", new Tile(8, 8), true, Cover.Half, "hay"), new("farm_goat", goat, false, Cover.None)];
        var map = BattleMap.FromBytes("field", side, side, new byte[side * side * 3], ["grass/autumn"], props, ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty, null);
        var plan = WorldPlan.Build(new BoardTerrain(map, null), Pack.Art);
        return (Read(plan, masked: true), WorldHerd.Build(plan.Terrain, Pack.Art, Art.Value));
    }

    [Theory]
    [InlineData(7, 8)]
    [InlineData(8, 7)]
    [InlineData(7, 7)]
    public void A_goat_behind_a_haystack_is_hidden_by_it_in_painters_order(int x, int y)
    {
        var (sheet, herd) = Haystack(new Tile(x, y));
        var goat = Assert.Single(herd.Units());
        var hay = sheet.All.Single(s => sheet.Plan.Blocks[s.At.Block].Lit.Items[s.At.Item] is LitSprite { Sprite.Image.Path: var path } && path.Contains("haystack", StringComparison.Ordinal));

        var over = sheet.Plan.Overlay([goat]);
        var (drawn, truth) = (new int[800 * 800], new int[800 * 800]);

        Assert.True(hay.Order > goat.Order);
        Assert.Contains(hay.At, over);      // it goes again over the goat
        Assert.Equal(0, Wrong(sheet, [goat], over, drawn, truth));
        Assert.Contains(hay.Id, drawn);
    }

    [Fact]
    public void A_goat_in_front_of_a_haystack_is_over_it()
    {
        var (sheet, herd) = Haystack(new Tile(9, 9));
        var goat = Assert.Single(herd.Units());
        var hay = sheet.All.Single(s => sheet.Plan.Blocks[s.At.Block].Lit.Items[s.At.Item] is LitSprite { Sprite.Image.Path: var path } && path.Contains("haystack", StringComparison.Ordinal));

        var over = sheet.Plan.Overlay([goat]);

        Assert.True(hay.Order < goat.Order);
        Assert.DoesNotContain(hay.At, over);
        Assert.Equal(0, Wrong(sheet, [goat], over, new int[800 * 800], new int[800 * 800]));
    }

    [Fact]
    public void Without_the_closure_the_walk_goes_wrong_so_this_test_can_fail()
    {
        var plan = Ford.Value.Plan;
        var route = WorldRoute.Across(FordGround.Value.Terrain, new Tile(30, 65), ClassDef.DefaultJump);

        var (wrong, _, _, _) = Sweep(Ford.Value, route, 1f, null, units =>
            [.. units.SelectMany(u => plan.Over(WorldPlan.AreaOf(u), u.Order)).Distinct().OrderBy(a => plan.Blocks[a.Block].Lit.Items[a.Item].Order).ThenBy(a => a.Block).ThenBy(a => a.Item)]);

        Assert.True(wrong > 0);
    }
}
