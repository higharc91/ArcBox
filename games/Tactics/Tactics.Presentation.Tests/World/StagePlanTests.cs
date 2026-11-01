using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using Xunit;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="StagePlan"/>, <see cref="BoardFit.Stage"/> and the world offset: a battle drawn in place meets the world with no seam.</summary>
public sealed class StagePlanTests(ITestOutputHelper output)
{
    private static readonly Lazy<(WorldGround Ground, BattleStage Stage, BattleMap Map)> Ford = new(StageWorld.Ford);
    private static readonly Lazy<UnitArt> Units = new(() => UnitArt.Load(Pack.Root));
    private static readonly Lazy<WorldGround> Hills = new(() => DemoRegion.Hilly(160, 120));

    private static string Geometry(BoardItem item) => item switch
    {
        Face f => $"{f.Kind} {f.Facing} {string.Join(' ', f.Points)} {f.Image?.Path} {f.Fill is null} {f.Alpha} {f.Grid} {f.Lit}",
        Sprite s => $"{s.Image.Path} {s.TopLeft} {s.Size} {s.Light is null} {s.Lit}",
        _ => throw new InvalidOperationException(),
    };

    private static string Whole(BoardItem item) => item switch
    {
        Face f => $"{f.Order} {f.Tile} {Geometry(f)} {string.Join(' ', f.Uvs)} {f.Shade}",
        Sprite s => $"{s.Order} {s.Tile} {Geometry(s)}",
        _ => throw new InvalidOperationException(),
    };

    private static Tile Local(BattleStage stage, Tile world) => stage.ToBattle(world);

    private static BattleView Battle(params UnitView[] units) => new("test", "test", 1, [], null, [.. units], [], null);

    private static UnitView Unit(string id, int x, int y) => new(new UnitId(id), id, "Party", "characters.class_fighter", new Tile(x, y), 10, 10, 0, "Up", 0, [], null);

    /// <summary>A random region round a random map, stamped at a random place and height.</summary>
    private static (WorldGround Ground, BattleStage Stage, BattleMap Map) RandomWorld(int seed)
    {
        var random = new Random(seed);
        var (width, depth) = (random.Next(4, 10), random.Next(4, 10));
        TileInfo Any(Random r) => r.Next(8) switch
        {
            0 => new TileInfo(0, StageWorld.Water, TileFlags.Water | TileFlags.Deep),
            1 => new TileInfo(r.Next(0, 3), StageWorld.Rock, TileFlags.Impassable),
            _ => new TileInfo(r.Next(0, 6), (byte)r.Next(0, 4) is var m && m == StageWorld.Water ? StageWorld.Grass : (byte)m, TileFlags.None),
        };
        var heights = new TileInfo[width, depth];
        for (var y = 0; y < depth; y++)
            for (var x = 0; x < width; x++)
                heights[x, y] = Any(random);
        PropPlacement[] props = [.. Enumerable.Range(0, random.Next(0, 4)).Select(_ => new PropPlacement(random.Next(2) == 0 ? "crate" : "torch",
            new Tile(random.Next(width), random.Next(depth)), true, Cover.None)).DistinctBy(p => p.At)];
        var map = StageWorld.Map($"map_{seed}", width, depth, (x, y) => heights[x, y], props);
        var at = new Tile(random.Next(0, 9), random.Next(0, 9));
        var raise = random.Next(0, 3);
        var (worldWidth, worldDepth) = (at.X + width + random.Next(0, 14), at.Y + depth + random.Next(0, 14));
        var world = new TileInfo[worldWidth, worldDepth];
        for (var y = 0; y < worldDepth; y++)
            for (var x = 0; x < worldWidth; x++)
                world[x, y] = Any(random);
        var (ground, stage) = StageWorld.Make(worldWidth, worldDepth, (x, y) => world[x, y], map, at, raise);
        return (ground, stage, map);
    }

    [Fact]
    public void A_local_pixel_lands_on_its_world_pixel()
    {
        var (ground, stage, map) = Ford.Value;
        var (local, world) = (new BoardTerrain(map, null), new BoardTerrain(ground.Map, null));
        var origin = new Vector2(-1234, 517);
        var (fit, worldFit) = (BoardFit.Stage(stage, origin, WorldTuning.Scale), new BoardFit(origin, WorldTuning.Scale));

        Assert.All(local.Tiles, t => Assert.Equal(worldFit.ToScreen(world.Centre(stage.ToWorld(t))), fit.ToScreen(local.Centre(t))));
        Assert.Equal(WorldTuning.Scale, fit.Scale);
    }

    [Fact]
    public void A_stamp_at_height_offsets_by_its_height()
    {
        var flat = StageWorld.Map("flat", 4, 4, (_, _) => StageWorld.Flat(1));
        var (low, lowStage) = StageWorld.Make(12, 12, (_, _) => StageWorld.Flat(0), flat, new Tile(3, 2), 0);
        var (high, highStage) = StageWorld.Make(12, 12, (_, _) => StageWorld.Flat(0), flat, new Tile(3, 2), 3);

        Assert.Equal(new Vector2(0, -3 * BoardProjection.HeightStep), BoardFit.StageOffset(highStage) - BoardFit.StageOffset(lowStage));
        var terrain = new BoardTerrain(high.Map, null);
        var local = new BoardTerrain(flat, null);
        Assert.Equal(terrain.Centre(new Tile(4, 3)), local.Centre(new Tile(1, 1)) + BoardFit.StageOffset(highStage));
        Assert.Equal(4, terrain.Top(new Tile(4, 3)));
        _ = low;
    }

    // The property: on any ground, a stamp's tiles draw on the stage as the plain board draws them, and as the world does, to the pixel.
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Stage_tiles_match_the_board_plans_tiles(int seed)
    {
        var (ground, stage, map) = seed < 0 ? Ford.Value : RandomWorld(seed);
        var changes = RandomChanges(seed, map);
        var stagePlan = StagePlan.Build(ground, stage, changes, Pack.Art);
        var plain = BoardPlan.Build(new BoardTerrain(map, changes), Pack.Art);
        var stamp = new TileRect(stage.Origin.X, stage.Origin.Y, stage.Width, stage.Depth);

        // Against the plain board: the same quads and sprites, but for the sides on its front edges, which are its skirt.
        bool Skirt(BoardItem i, Tile local) => i is Face { Kind: FaceKind.Side or FaceKind.Lip } f
            && (f.Facing == Facing.U ? local.X == map.Width - 1 : local.Y == map.Height - 1);
        var mine = stagePlan.Plan.Items.Where(i => stamp.Contains(i.Tile) && !Skirt(i, Local(stage, i.Tile)))
            .Select(i => $"{Local(stage, i.Tile)} {Geometry(i)}").Order(StringComparer.Ordinal).ToArray();
        var theirs = plain.Items.Where(i => !Skirt(i, i.Tile)).Select(i => $"{i.Tile} {Geometry(i)}").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(theirs, mine);

        // Against the world, everything the stage draws is the world's own item, moved: no seam in texture, shade, light or order.
        var world = new BoardTerrain(ground.Map, BoardChangesOnWorld.Of(changes, stage), BoardTerrain.ChasmMaterial(ground.Map, stamp));
        var drawn = new TileRect(stamp.X, stamp.Y, stamp.W + StagePlan.Reach, stamp.H + StagePlan.Reach);
        var worldItems = BoardPlan.BuildBlock(world, Pack.Art, drawn, grid: true).Shifted(-BoardFit.StageOffset(stage)).Items
            .Select(i => i is Face f && !stamp.Contains(f.Tile) ? f with { Grid = false } : i).Select(Whole).ToHashSet(StringComparer.Ordinal);
        Assert.All(stagePlan.Plan.Items, i => Assert.Contains(Whole(i), worldItems));
    }

    /// <summary>What a fight might have done to <paramref name="map"/>: nothing for an odd seed, else raised, lowered and sunk tiles and a zone.</summary>
    private static BoardChanges? RandomChanges(int seed, BattleMap map)
    {
        if (seed % 2 != 0)
            return null;
        var random = new Random(seed * 7 + 1);
        Tile Any() => new(random.Next(map.Width), random.Next(map.Height));
        ImmutableArray<HeightChange> heights = [.. Enumerable.Range(0, random.Next(1, 4)).Select(_ => new HeightChange(Any(), random.Next(2) == 0 ? -1 : 2)).DistinctBy(h => h.At)];
        ImmutableArray<Tile> sunk = [.. Enumerable.Range(0, random.Next(0, 3)).Select(_ => Any()).Distinct()];
        return new BoardChanges([new ZoneView(1, "fire", [Any(), Any()])], heights, sunk);
    }

    public static TheoryData<int> Seeds => [-1, .. Enumerable.Range(1, 40)];

    [Fact]
    public void No_skirt_is_drawn_at_the_stamp_edge()
    {
        var flat = StageWorld.Map("flat", 6, 5, (_, _) => StageWorld.Flat(2));
        var (ground, stage) = StageWorld.Make(20, 20, (_, _) => StageWorld.Flat(2), flat, new Tile(5, 6), 0);

        var plain = BoardPlan.Build(new BoardTerrain(flat, null), Pack.Art);
        var plan = StagePlan.Build(ground, stage, null, Pack.Art).Plan;

        Assert.Contains(plain.Items, i => i is Face { Kind: FaceKind.Side });
        Assert.DoesNotContain(plan.Items, i => i is Face { Kind: FaceKind.Side or FaceKind.Lip });
    }

    // At the region's own edge the stage draws the world's skirt, which goes down to the region's lowest tile, not the window's.
    [Fact]
    public void A_stamp_at_the_regions_edge_has_the_regions_skirt()
    {
        var flat = StageWorld.Map("flat", 6, 5, (_, _) => StageWorld.Flat(4));
        var (ground, stage) = StageWorld.Make(40, 25, (x, _) => StageWorld.Flat(x < 5 ? 0 : 4), flat, new Tile(34, 20), 0);
        var plan = StagePlan.Build(ground, stage, null, Pack.Art).Plan;
        var world = BoardPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art).Shifted(-BoardFit.StageOffset(stage));

        var edge = plan.Items.OfType<Face>().Where(f => f.Kind == FaceKind.Side).ToArray();
        Assert.NotEmpty(edge);
        Assert.All(edge, f => Assert.Contains(world.Items, w => w is Face face && face.Kind == FaceKind.Side && face.Tile == f.Tile && face.Facing == f.Facing
            && face.Points.SequenceEqual(f.Points) && face.Uvs.SequenceEqual(f.Uvs)));
    }

    [Fact]
    public void A_cliff_in_front_of_the_stamp_hides_a_unit()
    {
        var flat = StageWorld.Map("flat", 6, 6, (_, _) => StageWorld.Flat(0));
        var cliff = new Tile(8 + 6, 8 + 6);     // just past the stamp's front corner
        var (ground, stage) = StageWorld.Make(30, 30, (x, y) => new Tile(x, y) == cliff ? StageWorld.Flat(6, StageWorld.Rock) : StageWorld.Flat(0), flat, new Tile(8, 8), 0);
        var plan = StagePlan.Build(ground, stage, null, Pack.Art).Plan;
        var terrain = new BoardTerrain(flat, null);
        var fit = BoardFit.Stage(stage, Vector2.Zero, WorldTuning.Scale);

        var unit = Assert.Single(UnitPlan.Build(Battle(Unit("hero1", 5, 5)), terrain, Units.Value, fit, worldOffset: stage.Origin));
        var area = WorldPlan.AreaOf(unit);
        var over = plan.Items.Where(i => i.Order > unit.Order && i.Tile == cliff && new ArtArea(BoardPlan.Bounds(i).Min, BoardPlan.Bounds(i).Max).Overlaps(area)).ToArray();

        Assert.NotEmpty(over);
        // Without the world offset the unit would sort as if it stood at the region's far corner, behind everything.
        var unshifted = Assert.Single(UnitPlan.Build(Battle(Unit("hero1", 5, 5)), terrain, Units.Value, fit));
        Assert.True(unshifted.Order < unit.Order);
    }

    [Fact]
    public void A_stamped_prop_sorts_with_a_unit_as_on_the_board()
    {
        var (ground, stage, map) = Ford.Value;
        var terrain = new BoardTerrain(map, null);
        var plain = BoardPlan.Build(terrain, Pack.Art);
        var plan = StagePlan.Build(ground, stage, null, Pack.Art).Plan;
        var fit = BoardFit.For(terrain, Pack.Art);

        Assert.NotEmpty(map.Props);
        foreach (var prop in map.Props)
            foreach (var near in new[] { new Tile(prop.At.X + 1, prop.At.Y), new Tile(prop.At.X - 1, prop.At.Y), new Tile(prop.At.X, prop.At.Y + 1), new Tile(prop.At.X, prop.At.Y - 1) }.Where(map.InBounds))
            {
                var onBoard = Assert.Single(UnitPlan.Build(Battle(Unit("hero1", near.X, near.Y)), terrain, Units.Value, fit));
                var onStage = Assert.Single(UnitPlan.Build(Battle(Unit("hero1", near.X, near.Y)), terrain, Units.Value, fit, worldOffset: stage.Origin));
                var boardProp = Assert.Single(plain.Items, i => i is Sprite && i.Tile == prop.At);
                var stageProp = Assert.Single(plan.Items, i => i is Sprite && i.Tile == stage.ToWorld(prop.At));
                Assert.Equal(boardProp.Order < onBoard.Order, stageProp.Order < onStage.Order);
                Assert.Equal(onBoard.Feet, onStage.Feet);
            }
    }

    [Fact]
    public void Only_stamp_tiles_have_a_grid()
    {
        var (ground, stage, map) = Ford.Value;
        var plan = StagePlan.Build(ground, stage, null, Pack.Art).Plan;

        var grid = plan.Items.OfType<Face>().Where(f => f.Grid).ToArray();
        Assert.All(grid, f => Assert.True(stage.Holds(f.Tile)));
        Assert.Equal(BoardPlan.Build(new BoardTerrain(map, null), Pack.Art).Items.OfType<Face>().Count(f => f.Grid), grid.Length);
        Assert.Contains(plan.Items, i => !stage.Holds(i.Tile));     // the ground in front is there, unoutlined
    }

    [Fact]
    public void A_zone_shows_on_the_stage()
    {
        var (ground, stage, _) = Ford.Value;
        var zone = new Tile(3, 4);
        var changes = BoardChanges.None with { Zones = [new ZoneView(1, "fire", [zone])] };

        var plan = StagePlan.Build(ground, stage, changes, Pack.Art).Plan;
        var plainPlan = StagePlan.Build(ground, stage, null, Pack.Art).Plan;

        var ground1 = plan.Items.Where(i => i.Tile == stage.ToWorld(zone) && (i.Order & 0xFF) == (long)BoardLayer.Ground).ToArray();
        Assert.NotEmpty(ground1);
        Assert.DoesNotContain(plainPlan.Items, i => i.Tile == stage.ToWorld(zone) && (i.Order & 0xFF) == (long)BoardLayer.Ground);
        Assert.Equal(BoardChangesOnWorld.Of(changes, stage).Zones[0].Tiles, [stage.ToWorld(zone)]);
    }

    [Fact]
    public void A_lowered_tile_changes_the_blocks_too()
    {
        var flat = StageWorld.Map("flat", 6, 6, (_, _) => StageWorld.Flat(3));
        var (ground, stage) = StageWorld.Make(40, 40, (_, _) => StageWorld.Flat(3), flat, new Tile(14, 15), 0);
        var world = WorldPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art);
        var lowered = new Tile(2, 0);    // the stamp crosses a block edge at x 16
        var changes = BoardChanges.None with { Heights = [new HeightChange(lowered, -2)] };
        var stagePlan = StagePlan.Build(ground, stage, null, Pack.Art);

        var touched = BoardChangesOnWorld.Touched(BoardChanges.None, BoardChangesOnWorld.Of(changes, stage));
        Assert.Equal([stage.ToWorld(lowered)], touched);
        var rebuilt = stagePlan.WorldBlocks(world, touched, changes);

        // The world's blocks round it are what the whole region, changed, would build: the lowered top and the sides now down to it.
        var changed = new BoardTerrain(ground.Map, BoardChangesOnWorld.Of(changes, stage));
        Assert.True(rebuilt.Length >= 2);
        Assert.All(rebuilt, b => Assert.Equal(BoardPlan.BuildBlock(changed, Pack.Art, b.Tiles, grid: false).Items.Select(Whole), b.Plan.Items.Select(Whole)));
        Assert.All(rebuilt, b => Assert.Equal(world.Blocks[b.Index].Tiles, b.Tiles));
        var top = (BoardItem i) => i is Face { Kind: FaceKind.Top } && i.Tile == stage.ToWorld(lowered);
        var stageTop = (Face)Assert.Single(stagePlan.With(changes).Plan.Items, i => top(i));
        var newTop = (Face)Assert.Single(rebuilt.SelectMany(b => b.Plan.Items), i => top(i));
        var oldTop = (Face)Assert.Single(world.Blocks.SelectMany(b => b.Plan.Items), i => top(i));
        Assert.Equal(stageTop.Points.Select(p => p + BoardFit.StageOffset(stage)), newTop.Points);
        Assert.NotEqual(oldTop.Points, newTop.Points);
        Assert.Contains(rebuilt.SelectMany(b => b.Plan.Items), i => i is Face { Kind: FaceKind.Side } && i.Tile == stage.ToWorld(lowered with { X = lowered.X - 1 }));

        var healed = stagePlan.WorldBlocks(world, touched, null);
        Assert.Equal(rebuilt.Select(b => b.Index), healed.Select(b => b.Index));
        Assert.Equal(healed.SelectMany(h => world.Blocks[h.Index].Plan.Items).Select(Whole), healed.SelectMany(b => b.Plan.Items).Select(Whole));
    }

    [Fact]
    public void A_change_keeps_the_stages_light()
    {
        var (ground, stage, _) = Ford.Value;
        var plan = StagePlan.Build(ground, stage, null, Pack.Art);
        var changes = BoardChanges.None with { Sunk = [new Tile(4, 6)], Heights = [new HeightChange(new Tile(2, 2), 1)] };

        var changed = plan.With(changes);

        Assert.Same(plan.Lighting, changed.Lighting);
        Assert.Equal(changes, changed.Changes);
        Assert.Equal(StagePlan.Build(ground, stage, changes, Pack.Art).Plan.Items.Select(Whole), changed.Plan.Items.Select(Whole));
        Assert.NotEqual(plan.Plan.Items.Select(Whole), changed.Plan.Items.Select(Whole));
    }

    // In place the board is drawn at the world's scale, not fitted. Its ground has to fit the board's area; props and the plain board's skirt
    // can run past it, as they run into the world round it (plan 5c, "falls short" 9: hall_of_the_flame is 1024 px tall with its skirt).
    [Fact]
    public void Every_map_fits_the_area_at_scale_2()
    {
        foreach (var id in Pack.Maps)
        {
            var plan = BoardPlan.Build(new BoardTerrain(Pack.Map(id), null), Pack.Art);
            var tops = plan.Items.OfType<Face>().Where(f => f.Kind == FaceKind.Top).SelectMany(f => f.Points).ToArray();
            var size = (tops.Aggregate(Vector2.Max) - tops.Aggregate(Vector2.Min)) * WorldTuning.Scale;
            output.WriteLine($"{id}: ground {size}, everything {(plan.Max - plan.Min) * WorldTuning.Scale}");
            Assert.True(size.X <= BoardFit.Area.Width && size.Y <= BoardFit.Area.Height, $"{id} is {size} at scale {WorldTuning.Scale}");
        }
    }

    [Fact]
    public void Lights_and_figures_read_the_same_frame()
    {
        var ground = Hills.Value;
        var stage = new BattleStage("hills", new Tile(60, 30), 0, 14, 12);
        var stagePlan = StagePlan.Build(ground, stage, null, Pack.Art);
        var world = BoardLighting.Of(BoardPlan.BuildBlock(new BoardTerrain(ground.Map, null), Pack.Art, new TileRect(40, 10, 60, 60), grid: false), ground.Map.Id, Pack.Art.Lighting);
        var terrain = new BoardTerrain(ground.Map, null);
        var offset = BoardFit.StageOffset(stage);

        Assert.True(stagePlan.Lighting.Active);
        var lit = 0;
        for (var y = 0; y < stage.Depth + StagePlan.Reach; y++)
            for (var x = 0; x < stage.Width + StagePlan.Reach; x++)
            {
                var figure = BoardLighting.BodyOf(terrain.Centre(stage.ToWorld(new Tile(x, y))), 48);
                var (mine, theirs) = (stagePlan.Lighting.MeanAt(figure - offset), world.MeanAt(figure));
                Assert.Equal(theirs.R, mine.R, 4);
                Assert.Equal(theirs.G, mine.G, 4);
                Assert.Equal(theirs.B, mine.B, 4);
                lit += theirs != new Rgb(1, 1, 1) ? 1 : 0;
            }
        Assert.True(lit > 10, $"only {lit} lit points");
    }

    // Plan 5c Task 5, step 5: a beat that changes the ground rebuilds the stage on the TV's frame, so that has to fit in 8 ms, measured alone. The world's blocks
    // round the change cost what any block does to bake (about 4 ms each on the stress region), so they go through the world's own block queue.
    [Fact]
    [Trait("Category", "Perf")]
    public void A_rebuild_on_the_largest_region_is_quick()
    {
        var ground = Hills.Value;
        var stage = new BattleStage("hills", new Tile(60, 30), 0, 14, 12);
        var changes = BoardChanges.None with { Heights = [new HeightChange(new Tile(3, 3), 2)], Sunk = [new Tile(5, 5)] };
        var world = WorldPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art);
        var plan = StagePlan.Build(ground, stage, null, Pack.Art);
        var touched = BoardChangesOnWorld.Touched(null, BoardChangesOnWorld.Of(changes, stage));
        _ = plan.With(changes);
        _ = plan.WorldBlocks(world, touched, changes);

        static double Median(int runs, Action run)
        {
            var times = new double[runs];
            for (var i = 0; i < runs; i++)
            {
                var watch = Stopwatch.StartNew();
                run();
                times[i] = watch.Elapsed.TotalMilliseconds;
            }
            Array.Sort(times);
            return times[runs / 2];
        }
        // The same rebuild as it would be without the window: a terrain of the whole region, and the drawn rect's plan in its light. Runs of the
        // two take turns, so whatever else the machine is doing slows both alike.
        var worldChanges = BoardChangesOnWorld.Of(changes, stage);
        var drawn = plan.Drawn;
        var lighting = plan.Lighting;
        void Whole()
        {
            var terrain = new BoardTerrain(ground.Map, worldChanges);
            _ = LitBoard.Build(BoardPlan.BuildBlock(terrain, Pack.Art, drawn, grid: true), lighting);
        }
        var (window, whole) = (new List<double>(), new List<double>());
        for (var i = 0; i < 21; i++)
        {
            var watch = Stopwatch.StartNew();
            plan = plan.With(i % 2 == 0 ? changes : null);
            window.Add(watch.Elapsed.TotalMilliseconds);
            watch.Restart();
            Whole();
            whole.Add(watch.Elapsed.TotalMilliseconds);
        }
        var (stageMs, wholeMs) = (window.Order().ElementAt(10), whole.Order().ElementAt(10));
        var terrainMs = Median(9, () => _ = new BoardTerrain(ground.Map, null));
        var blocks = 0;
        var blocksMs = Median(5, () => blocks = plan.WorldBlocks(world, touched, changes).Length);
        output.WriteLine($"160x120: whole BoardTerrain {terrainMs:F2} ms; stage rebuild {stageMs:F2} ms against {wholeMs:F2} ms without the window "
            + $"({plan.Plan.Items.Length} items, {plan.Lit.Items.Length} lit, {plan.Lighting.Lights.Length} lights); {blocks} world blocks {blocksMs:F2} ms");

        // Alone it's about 5 ms in Debug against 13 or so; under load both grow. A rebuild that went back to reading the whole region would cost
        // what the second does, so it has to stay well under it.
        Assert.True(stageMs < wholeMs * 0.75, $"a stage rebuild took {stageMs:F2} ms, against {wholeMs:F2} ms reading the whole region");
    }
}
