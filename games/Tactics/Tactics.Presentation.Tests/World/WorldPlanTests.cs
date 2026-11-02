using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WorldPlan"/>: a region cut into blocks draws what the whole board would, on the real region and on the stress case.</summary>
public sealed class WorldPlanTests
{
    private static readonly ImmutableSortedDictionary<string, ImmutableArray<Tile>> NoZones
        = ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty;

    private static readonly Lazy<WorldGround> FordGround = new(() => Pack.Ground("ashen_ford"));
    private static readonly Lazy<WorldGround> HillsGround = new(() => DemoRegion.Hilly(160, 120));
    private static readonly Lazy<WorldPlan> FordPlan = new(() => WorldPlan.Build(new BoardTerrain(FordGround.Value.Map, null), Pack.Art));
    private static readonly Lazy<WorldPlan> HillsPlan = new(() => WorldPlan.Build(new BoardTerrain(HillsGround.Value.Map, null), Pack.Art));

    public static TheoryData<string> Regions => ["ashen_ford", "hills"];

    [Fact]
    public void Ashen_ford_bridge_deck_is_walkable_from_bank_to_bank()
    {
        var ground = FordGround.Value;
        var deck = Enumerable.Range(105, 4).Select(x => new Tile(x, 48)).ToArray();

        Assert.All(deck, tile => Assert.True(ground.Terrain.Walkable(tile), $"Bridge deck tile {tile} is blocked."));
        Assert.Contains(new Tile(109, 48), WorldWalk.Reach(ground.Terrain, new Tile(104, 48), ClassDef.DefaultJump));
    }

    [Fact]
    public void Ashen_ford_repaints_legacy_hills_and_calmly_blends_the_ground()
    {
        var map = FordGround.Value.Map;
        Tile Find(string name) => Enumerable.Range(0, map.Height)
            .SelectMany(y => Enumerable.Range(0, map.Width).Select(x => new Tile(x, y)))
            .First(tile => !map.StampedTiles.Contains(tile) && map.Materials[map[tile].Material] == name);
        var terrain = FordPlan.Value.Terrain;
        var oldGrass = Find("grass/frost");
        var oldRock = Find("cave_floor/cool");
        var oldWater = Find("water/clear");
        var oldPlanks = Find("planks/oak");

        Assert.Equal("grass_ford", terrain.Look(oldGrass).Top.Family);
        Assert.Equal("rock_ford", terrain.Look(oldRock).Top.Family);
        Assert.Equal("ford_water", terrain.Look(oldWater).Top.Family);
        Assert.Equal("planks_ford", terrain.Look(oldPlanks).Top.Family);
        Assert.DoesNotContain(FordPlan.Value.Blocks.SelectMany(block => block.Plan.Items).OfType<Face>(),
            face => face.Tile == oldGrass && face.Kind == FaceKind.Decal);
        Assert.DoesNotContain(FordPlan.Value.Blocks.SelectMany(block => block.Plan.Items).OfType<Face>(),
            face => face.Tile == oldRock && face.Kind == FaceKind.Decal);
    }

    [Fact]
    public void Wood_region_has_at_least_1500_tall_props_in_view_and_walkable_trail()
    {
        var ground = DemoRegion.Wood(160, 120);
        Assert.True(ground.Map.Props.Length >= 6000, $"Expected >=6000 props, got {ground.Map.Props.Length}");

        var tallIds = new HashSet<string> { "tree", "pine", "dead_tree", "lamp_post", "lantern" };
        Assert.All(ground.Map.Props, p => Assert.Contains(p.Id, tallIds));

        var start = DemoRegion.WoodStart(160, 120);
        Assert.True(ground.Terrain.Walkable(start));
        var route = WorldRoute.Across(ground.Terrain, start, ClassDef.DefaultJump);
        Assert.True(route.Length >= 100, $"Expected route >= 100 tiles, got {route.Length}");

        // In a view-sized area (e.g. 80x60 tiles), there should be >= 1500 tall props
        var viewProps = ground.Map.Props.Count(p => p.At.X >= 10 && p.At.X < 90 && p.At.Y >= 20 && p.At.Y < 80);
        Assert.True(viewProps >= 1500, $"Expected >= 1500 tall props in view, got {viewProps}");
    }

    private static WorldGround GroundOf(string name) => name == "hills" ? HillsGround.Value : FordGround.Value;

    private static WorldPlan PlanOf(string name) => name == "hills" ? HillsPlan.Value : FordPlan.Value;

    /// <summary>A map of grass, with a height for each tile.</summary>
    private static BattleMap Map(int width, int height, Func<int, int, int> heightAt, params PropPlacement[] props)
    {
        var bytes = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bytes[(y * width + x) * 3] = (byte)heightAt(x, y);
        return BattleMap.FromBytes("test", width, height, bytes, ["grass/lush"], [.. props], NoZones);
    }

    private static string Key(BoardItem item) => item switch
    {
        Face f => $"{f.Order} {f.Tile} {f.Kind} {f.Facing} {string.Join(' ', f.Points)} {string.Join(' ', f.Uvs)} {f.Image?.Path} {f.Fill} {f.Shade} {f.Alpha} {f.Grid} {f.Lit}",
        Sprite s => $"{s.Order} {s.Tile} {s.Image.Path} {s.TopLeft} {s.Size} {s.Light is null} {s.Lit}",
        _ => throw new InvalidOperationException(),
    };

    private static ArtArea AreaOf(BoardItem item)
    {
        var (min, max) = BoardPlan.Bounds(item);
        return new ArtArea(min, max);
    }

    private static ArtArea LitArea(LitItem item) => item is LitFace face ? AreaOf(face.Face) : AreaOf(((LitSprite)item).Sprite);

    [Theory]
    [MemberData(nameof(Regions))]
    public void Blocks_add_up_to_the_whole_board(string name)
    {
        var terrain = new BoardTerrain(GroundOf(name).Map, null);
        var whole = BoardPlan.Build(terrain, Pack.Art);
        var cuts = PlanOf(name).Blocks.Select(b => b.Tiles).ToArray();

        var union = cuts.SelectMany(c => BoardPlan.BuildBlock(terrain, Pack.Art, c, grid: true).Items).OrderBy(i => i.Order);

        Assert.Equal(whole.Items.Select(Key), union.Select(Key));
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void A_block_of_the_world_is_the_same_board_with_no_grid(string name)
    {
        var terrain = new BoardTerrain(GroundOf(name).Map, null);
        var whole = BoardPlan.Build(terrain, Pack.Art);
        var blocks = PlanOf(name).Blocks.SelectMany(b => b.Plan.Items).OrderBy(i => i.Order).ToArray();
        var baseBlocks = blocks.Where(i => i is not Face { Kind: FaceKind.Decal or FaceKind.Blend }).ToArray();

        Assert.DoesNotContain(baseBlocks, i => i is Face { Grid: true });
        Assert.Equal(whole.Items.Select(i => i is Face f ? Key(f with { Grid = false }) : Key(i)), baseBlocks.Select(Key));
        Assert.Contains(blocks, i => i is Face { Kind: FaceKind.Decal });
    }

    [Fact]
    public void Every_tile_is_in_one_block_and_a_short_last_block_stops_at_the_map()
    {
        var terrain = new BoardTerrain(Map(40, 20, (_, _) => 0), null);
        var plan = WorldPlan.Build(terrain, Pack.Art);

        Assert.Equal(new TileRect(32, 16, 8, 4), plan.Blocks.MaxBy(b => (b.Tiles.Y, b.Tiles.X))!.Tiles);
        Assert.Equal(40 * 20, plan.Blocks.Sum(b => b.Tiles.W * b.Tiles.H));
        Assert.Equal(40 * 20, plan.Blocks.SelectMany(b => terrain.TilesIn(b.Tiles)).Distinct().Count());
    }

    [Fact]
    public void A_rect_past_the_map_is_cut_to_it()
    {
        var terrain = new BoardTerrain(Map(4, 4, (_, _) => 0), null);

        Assert.Equal([new Tile(3, 3)], terrain.TilesIn(new TileRect(3, 3, 16, 16)));
        Assert.Empty(terrain.TilesIn(new TileRect(4, 0, 16, 16)));
        Assert.Empty(BoardPlan.BuildBlock(terrain, Pack.Art, new TileRect(4, 0, 16, 16), grid: false).Items);
    }

    [Fact]
    public void A_cliff_across_a_block_edge_has_its_side()
    {
        var terrain = new BoardTerrain(Map(32, 32, (x, y) => x < 16 && y < 16 ? 5 : 0), null);
        var plan = WorldPlan.Build(terrain, Pack.Art);
        var first = plan.Blocks.Single(b => b.Tiles.X == 0 && b.Tiles.Y == 0).Plan;

        var along = first.Items.OfType<Face>().Where(f => f.Kind == FaceKind.Side && f.Tile == new Tile(15, 7)).ToArray();
        var down = first.Items.OfType<Face>().Where(f => f.Kind == FaceKind.Side && f.Tile == new Tile(7, 15)).ToArray();

        var side = Assert.Single(along);
        Assert.Equal(Facing.U, side.Facing);
        Assert.Equal(5 * BoardProjection.HeightStep, side.Points[3].Y - side.Points[0].Y);
        Assert.Equal(Facing.V, Assert.Single(down).Facing);
    }

    [Fact]
    public void A_block_edge_on_level_ground_has_no_side()
    {
        var plan = WorldPlan.Build(new BoardTerrain(Map(32, 32, (_, _) => 3), null), Pack.Art);

        var sides = plan.Blocks.SelectMany(b => b.Plan.Items).OfType<Face>().Where(f => f.Kind == FaceKind.Side).ToArray();

        Assert.NotEmpty(sides);
        Assert.All(sides, s => Assert.True(s.Tile.X == 31 || s.Tile.Y == 31, $"{s.Tile}"));
    }

    [Fact]
    public void A_zone_is_drawn_in_the_block_of_its_tile()
    {
        var changes = new BoardChanges([new ZoneView(1, "fire", [new Tile(3, 3), new Tile(20, 3)])], [], []);
        var terrain = new BoardTerrain(Map(32, 16, (_, _) => 0), changes);

        var plan = BoardPlan.BuildBlock(terrain, Pack.Art, new TileRect(16, 0, 16, 16), grid: false);

        Assert.Equal([new Tile(20, 3)], plan.Items.OfType<Sprite>().Where(s => s.Order == BoardOrder.Key(s.Tile, BoardLayer.Ground)).Select(s => s.Tile));
    }

    [Fact]
    public void A_stamped_prop_is_drawn_on_its_tile()
    {
        var terrain = FordPlan.Value.Terrain;
        var stamp = new Tile(100, 52);

        Assert.NotEmpty(Pack.Map("river_crossing").Props);
        foreach (var prop in Pack.Map("river_crossing").Props)
        {
            var at = new Tile(prop.At.X + stamp.X, prop.At.Y + stamp.Y);
            var art = Pack.Art.Prop(prop.Id, [terrain.Look(at).Variant])!;

            var block = FordPlan.Value.Blocks.Single(b => b.Tiles.Contains(at));
            var sprite = block.Plan.Items.OfType<Sprite>().First(s => s.Tile == at);

            Assert.Equal(terrain.Centre(at) - new Vector2(art.AnchorX, art.AnchorY), sprite.TopLeft);
            Assert.Equal(BoardOrder.Key(at, BoardLayer.Prop), sprite.Order);
        }
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void Block_order_agrees_with_painters_order_where_items_overlap(string name)
    {
        var blocks = PlanOf(name).Blocks;
        var areas = blocks.Select(b => b.Plan.Items.Select(i => (Item: i, Area: AreaOf(i))).ToArray()).ToArray();
        var boxes = blocks.Select(b => new ArtArea(b.Plan.Min, b.Plan.Max)).ToArray();

        var pairs = 0;
        for (var i = 0; i < blocks.Length; i++)
            for (var j = i + 1; j < blocks.Length; j++)
            {
                if (!boxes[i].Overlaps(boxes[j]))
                    continue;
                var (first, last) = (boxes[i], boxes[j]);
                var behind = areas[i].Where(a => a.Area.Overlaps(last)).ToArray();
                var before = areas[j].Where(a => a.Area.Overlaps(first)).ToArray();
                foreach (var a in behind)
                    foreach (var b in before)
                        if (a.Area.Overlaps(b.Area))
                        {
                            pairs++;
                            Assert.True(a.Item.Order <= b.Item.Order, $"{a.Item.Tile} is painted before {b.Item.Tile}, but sorts after it");
                        }
            }
        Assert.True(pairs > 100, $"Only {pairs} overlapping pairs were checked.");
    }

    [Fact]
    public void Blocks_are_listed_in_the_order_they_are_drawn()
    {
        var blocks = FordPlan.Value.Blocks;

        Assert.Equal(blocks.Select(b => b.Z).Order(), blocks.Select(b => b.Z));
        Assert.All(blocks, b => Assert.Equal(b.Tiles.X / WorldTuning.BlockSize + b.Tiles.Y / WorldTuning.BlockSize, b.Z));
        Assert.Equal(Enumerable.Range(0, blocks.Length), blocks.Select(b => b.Index));
    }

    [Fact]
    public void Blocks_are_queued_nearest_the_party_first()
    {
        var queue = new BlockQueue(FordPlan.Value.Blocks);
        var party = new Vector2(30, 65);

        var order = new List<BlockPlan>();
        while (queue.Next(party) is { } block)
            order.Add(block);

        Assert.Equal(FordPlan.Value.Blocks.Length, order.Count);
        Assert.Equal(0, queue.Count);
        var distances = order.Select(b => Vector2.Distance(b.Middle, party)).ToArray();
        Assert.Equal(distances.Order(), distances);
        Assert.True(order[0].Tiles.Contains(new Tile(30, 65)));
    }

    [Fact]
    public void Peeking_takes_nothing()
    {
        var queue = new BlockQueue(FordPlan.Value.Blocks);

        var peeked = queue.Peek(Vector2.Zero);

        Assert.Equal(FordPlan.Value.Blocks.Length, queue.Count);
        Assert.Same(peeked, queue.Next(Vector2.Zero));
        Assert.NotSame(peeked, queue.Peek(Vector2.Zero));
        Assert.Null(new BlockQueue([]).Peek(Vector2.Zero));
    }

    [Fact]
    public void A_moving_party_queues_what_it_is_walking_into()
    {
        var (leader, velocity) = (new Vector2(30, 65), new Vector2(3.3f, 0));

        Assert.Equal(leader + velocity * 2, BlockQueue.Aim(leader, velocity));
        Assert.Equal(leader, BlockQueue.Aim(leader, Vector2.Zero));
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void Items_over_a_rect_are_only_those_in_front(string name)
    {
        var plan = PlanOf(name);
        var tiles = plan.Terrain.TilesIn(new TileRect(0, 0, plan.Terrain.Map.Width - 1, plan.Terrain.Map.Height - 1));
        var tile = tiles.First(t => !plan.Terrain.Look(t).Void && plan.Terrain.Top(new Tile(t.X + 1, t.Y + 1)) >= plan.Terrain.Top(t) + 1);
        var feet = BoardProjection.Centre(tile, plan.Terrain.Top(tile));
        var unit = new ArtArea(feet - new Vector2(16, 48), feet + new Vector2(16, 4));
        var after = BoardOrder.Key(tile, BoardLayer.Unit);

        var over = plan.ItemsOver(unit, after);

        var inFront = plan.Blocks.SelectMany(b => b.Lit.Items).Where(i => i.Order > after && LitArea(i).Overlaps(unit)).ToHashSet();
        Assert.NotEmpty(over);
        Assert.All(over, i => Assert.Contains(i, inFront));      // and of those, only what really crosses it: a tile's box covers its neighbours' corners, and its diamond doesn't
    }

    [Fact]
    public void Items_over_a_rect_reach_a_cliff_whose_tile_is_off_the_rect()
    {
        var plan = WorldPlan.Build(new BoardTerrain(Map(32, 32, (x, y) => x is >= 20 and <= 22 && y is >= 20 and <= 22 ? 9 : 0), null), Pack.Art);
        var behind = new Tile(18, 18);
        var feet = BoardProjection.Centre(behind, 0);
        var unit = new ArtArea(feet - new Vector2(16, 48), feet + new Vector2(16, 4));

        var over = plan.ItemsOver(unit, BoardOrder.Key(behind, BoardLayer.Unit));

        Assert.Contains(over, i => i is LitFace { Face: { Kind: FaceKind.Side, Tile: { X: 22 } } });
    }

    [Fact]
    public void A_pool_of_light_does_not_stop_at_a_block_edge()
    {
        var torch = new PropPlacement("torch", new Tile(15, 8), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(32, 16, (_, _) => 0, torch), null), Pack.Art);

        Assert.Single(plan.Lighting.Lights);
        Assert.All(plan.Blocks, b => Assert.Same(plan.Lighting, b.Lit.Lighting));
        var next = plan.Blocks.Single(b => b.Tiles.X == 16).Lit.Items.OfType<LitFace>().First(f => f.Face.Tile == new Tile(16, 8));
        var light = next.MeanColourAt(0, plan.Lighting);
        Assert.True(light.R > next.Face.Tint.R, $"{light}");
    }

    [Fact]
    public void Paths_names_each_image_and_halo_once()
    {
        var torch = new PropPlacement("torch", new Tile(5, 5), true, Cover.None);
        var plan = WorldPlan.Build(new BoardTerrain(Map(16, 16, (_, _) => 0, torch), null), Pack.Art);

        Assert.Equal(plan.Paths.Distinct(), plan.Paths);
        Assert.Contains(Pack.Art.Texture(MaterialLook.Of("grass/lush").Top)!.Path, plan.Paths);
        Assert.Contains(Pack.Art.Prop("torch", [])!.Image.Path, plan.Paths);
        Assert.Contains(plan.Lighting.Lights[0].Halo!.Path, plan.Paths);
    }

    [Fact]
    public void The_stress_region_has_a_river_hills_and_a_heavy_count()
    {
        var map = HillsGround.Value.Map;
        var tiles = Enumerable.Range(0, map.Height).SelectMany(y => Enumerable.Range(0, map.Width).Select(x => map[new Tile(x, y)])).ToArray();

        Assert.Equal((160, 120), (map.Width, map.Height));
        Assert.Contains(tiles, t => t.Flags.HasFlag(TileFlags.Water));
        Assert.InRange(tiles.Max(t => t.Height), 6, WorldRegion.MaxHeight);
        Assert.True(map.Props.Length >= 3000, $"{map.Props.Length}");
        Assert.True(HillsPlan.Value.Lighting.Lights.Length >= 900, $"{HillsPlan.Value.Lighting.Lights.Length}");
    }

    [Fact]
    public void The_tvs_ground_and_the_servers_agree_on_every_tile_of_the_ford()
    {
        var tv = DemoRegion.FromPack(Pack.Root, "ashen_ford");
        var server = RulesPack.Load([Path.Combine(Pack.Root, "rules")], ArtManifests.Load(Pack.Root)).GroundFor("ashen_ford")!;
        var blocked = 0;

        for (var y = 0; y < server.Map.Height; y++)
            for (var x = 0; x < server.Map.Width; x++)
            {
                var tile = new Tile(x, y);
                Assert.True(server.Terrain.Walkable(tile) == tv.Terrain.Walkable(tile), $"{tile}");
                blocked += server.Terrain.PropAt(tile) is { Blocks: true } ? 1 : 0;
            }
        Assert.True(blocked > 0);
    }

    [Fact]
    public void The_pack_region_loads_from_its_file_as_the_rules_build_it()
    {
        var loaded = DemoRegion.FromPack(Pack.Root, "ashen_ford");

        Assert.Equal(FordGround.Value.Map.Width, loaded.Map.Width);
        Assert.Equal(FordGround.Value.Map.Props, loaded.Map.Props);
        Assert.Equal(FordGround.Value.Map.Materials, loaded.Map.Materials);
    }

    [Fact]
    public void A_region_prop_is_drawn_on_its_tile_with_its_variant()
    {
        var region = WorldRegion.From(Pack.Region("ashen_ford").Def with { Props = [new RegionProps("tree", "autumn", [new Tile(10, 10)])] });
        var ground = WorldGround.Of(region, ImmutableDictionary<string, BattleMap>.Empty, Pack.Manifests);
        var plan = WorldPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art);
        var block = plan.Blocks.Single(b => b.Tiles.Contains(new Tile(10, 10)));
        var sprite = block.Plan.Items.OfType<Sprite>().Single(s => s.Tile == new Tile(10, 10));

        Assert.Contains("autumn", sprite.Image.Path);
    }

    [Fact]
    public void A_prop_with_light_data_lights_the_ground()
    {
        var region = WorldRegion.From(Pack.Region("ashen_ford").Def with { Props = [new RegionProps("torch", "fire", [new Tile(10, 10)])] });
        var ground = WorldGround.Of(region, ImmutableDictionary<string, BattleMap>.Empty, Pack.Manifests);
        var plan = WorldPlan.Build(new BoardTerrain(ground.Map, null), Pack.Art);
        var block = plan.Blocks.Single(b => b.Tiles.Contains(new Tile(10, 10)));

        Assert.NotEmpty(plan.Lighting.Lights);
        var topFace = block.Lit.Items.OfType<LitFace>().First(f => f.Face.Tile == new Tile(10, 10) && f.Face.Kind == FaceKind.Top);
        Assert.NotEmpty(topFace.Probes);
        var mean = topFace.MeanColourAt(0, plan.Lighting);
        Assert.True(mean.R > topFace.Face.Tint.R || mean.G > topFace.Face.Tint.G, "Ground should be lit by torch");
    }

    [Fact]
    public void Decals_are_the_same_on_every_build()
    {
        var terrain1 = new BoardTerrain(FordGround.Value.Map, null);
        var terrain2 = new BoardTerrain(FordGround.Value.Map, null);
        var plan1 = WorldPlan.Build(terrain1, Pack.Art);
        var plan2 = WorldPlan.Build(terrain2, Pack.Art);

        var decals1 = plan1.Blocks.SelectMany(b => b.Plan.Items).OfType<Face>().Where(f => f.Kind == FaceKind.Decal).Select(Key).ToArray();
        var decals2 = plan2.Blocks.SelectMany(b => b.Plan.Items).OfType<Face>().Where(f => f.Kind == FaceKind.Decal).Select(Key).ToArray();

        Assert.NotEmpty(decals1);
        Assert.Equal(decals1, decals2);
    }

    [Fact]
    public void A_stamped_tile_gets_no_decal()
    {
        var plan = FordPlan.Value;
        var stampOrigin = new Tile(100, 52);
        var stampMap = Pack.Map("river_crossing");
        var stampRect = new TileRect(stampOrigin.X, stampOrigin.Y, stampMap.Width, stampMap.Height);

        var decals = plan.Blocks.SelectMany(b => b.Plan.Items).OfType<Face>()
            .Where(f => f.Kind == FaceKind.Decal && stampRect.Contains(f.Tile)).ToArray();

        Assert.Empty(decals);
    }

    [Fact]
    public void Tint_is_continuous_across_a_block_edge()
    {
        var plan = FordPlan.Value;
        var left = plan.Blocks.Single(b => b.Tiles.X == 0 && b.Tiles.Y == 0);
        var right = plan.Blocks.Single(b => b.Tiles.X == 16 && b.Tiles.Y == 0);

        var checkedCount = 0;
        for (var y = 0; y < 16; y++)
        {
            var leftFace = left.Plan.Items.OfType<Face>().FirstOrDefault(f => f.Tile == new Tile(15, y) && f.Kind == FaceKind.Top);
            var rightFace = right.Plan.Items.OfType<Face>().FirstOrDefault(f => f.Tile == new Tile(16, y) && f.Kind == FaceKind.Top);
            if (leftFace is not null && rightFace is not null && !leftFace.VertexTints.IsDefaultOrEmpty && !rightFace.VertexTints.IsDefaultOrEmpty)
            {
                Assert.Equal(leftFace.VertexTints[1], rightFace.VertexTints[0]);
                Assert.Equal(leftFace.VertexTints[2], rightFace.VertexTints[3]);
                checkedCount++;
            }
        }
        Assert.True(checkedCount > 0, "Should have checked tint continuity on shared block edge faces");
    }

    [Fact]
    public void A_battle_board_is_unchanged()
    {
        var map = Pack.Map("river_crossing");
        var terrain = new BoardTerrain(map, null);
        var plan = BoardPlan.Build(terrain, Pack.Art);

        Assert.DoesNotContain(plan.Items, i => i is Face { Kind: FaceKind.Decal });
        Assert.All(plan.Items.OfType<Face>(), f => Assert.True(f.VertexTints.IsDefaultOrEmpty));
    }
}
