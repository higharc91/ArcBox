using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class WorldWalkTests
{
    private const int Jump = ClassDef.DefaultJump;

    private static readonly Tile Origin = new(0, 0);

    private static Terrain TerrainOf(string ascii) => new(AsciiMap.Parse(ascii), ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    private static WalkMove? From00(string ascii, WalkDir dir, bool? lastAlongX = null) => WorldWalk.Next(TerrainOf(ascii), Origin, dir, Jump, lastAlongX);

    /// <summary>A 64x32 region whose east chunk is missing, with a 2x1 map stamped at (4, 5) whose second tile holds a blocking prop.</summary>
    private static WorldGround Stamped()
    {
        var def = new RegionDef("r", "R", 1, [64, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [new RegionStamp("ascii", new Tile(4, 5), 0)], [],
            new RegionSpawns(Origin, Origin), []);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap> { ["ascii"] = AsciiMap.Parse("0. 0C") });
    }

    [Fact]
    public void Stamped_props_are_on_the_ground_at_world_tiles()
    {
        var ground = Stamped();

        Assert.Equal(new Tile(5, 5), Assert.Single(ground.Map.Props).At);
        Assert.True(ground.Terrain.PropAt(new Tile(5, 5)) is { Blocks: true });
        Assert.Null(ground.Terrain.PropAt(new Tile(1, 0)));
    }

    [Fact]
    public void A_blocking_prop_stops_the_party()
    {
        Assert.Null(WorldWalk.Next(Stamped().Terrain, new Tile(4, 5), WalkDir.SE, Jump, null));
        Assert.NotNull(WorldWalk.Next(Stamped().Terrain, new Tile(4, 5), WalkDir.NW, Jump, null));
    }

    [Fact]
    public void A_missing_chunk_is_impassable_void()
    {
        var ground = Stamped();
        var edge = new Tile(31, 5);

        Assert.False(ground.Terrain.Walkable(new Tile(32, 5)));
        Assert.Equal("void/deep", ground.Map.Materials[ground.Map[new Tile(32, 5)].Material]);
        Assert.Null(WorldWalk.Next(ground.Terrain, edge, WalkDir.SE, Jump, null));
    }

    [Fact]
    public void A_tile_that_is_painted_keeps_its_material()
    {
        var ground = Stamped();

        Assert.Equal("grass/lush", ground.Map.Materials[ground.Map[Origin].Material]);
    }

    [Theory]
    [InlineData(WalkDir.N, -1, -1)]
    [InlineData(WalkDir.NE, 0, -1)]
    [InlineData(WalkDir.E, 1, -1)]
    [InlineData(WalkDir.SE, 1, 0)]
    [InlineData(WalkDir.S, 1, 1)]
    [InlineData(WalkDir.SW, 0, 1)]
    [InlineData(WalkDir.W, -1, 1)]
    [InlineData(WalkDir.NW, -1, 0)]
    public void Screen_directions_map_to_tiles(WalkDir dir, int dx, int dy)
        => Assert.Equal((dx, dy), WorldWalk.Delta(dir));

    [Fact]
    public void A_climb_over_the_default_jump_is_blocked()
    {
        Assert.Null(From00("0. 3.", WalkDir.SE));
        Assert.Equal(new WalkMove(new Tile(1, 0), 2, Diagonal: false, Slid: false), From00("0. 2.", WalkDir.SE));
    }

    [Fact]
    public void A_drop_of_any_height_is_allowed()
        => Assert.Equal(new WalkMove(new Tile(1, 0), 1, Diagonal: false, Slid: false), From00("9. 0.", WalkDir.SE));

    [Fact]
    public void Deep_water_blocks()
        => Assert.Null(From00("0. 0x", WalkDir.SE));

    [Fact]
    public void A_ford_costs_what_the_rules_say()
        => Assert.Equal(2, From00("0. 0~", WalkDir.SE)!.Value.Cost);

    [Fact]
    public void An_open_diagonal_is_one_step()
        => Assert.Equal(new WalkMove(new Tile(1, 1), 1, Diagonal: true, Slid: false), From00("0. 0.\n0. 0.", WalkDir.S));

    [Fact]
    public void A_diagonal_cant_cut_a_corner()
    {
        Assert.NotEqual(new Tile(1, 1), From00("0. 0#\n0. 0.", WalkDir.S)!.Value.To);
        Assert.Null(From00("0. 0#\n0# 0.", WalkDir.S));
    }

    [Fact]
    public void A_blocked_diagonal_slides_along_the_wall()
        => Assert.Equal(new WalkMove(new Tile(0, 1), 1, Diagonal: false, Slid: true), From00("0. 0#\n0. 0.", WalkDir.S));

    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(false, 0, 1)]
    public void A_slide_prefers_the_axis_it_was_on(bool lastAlongX, int x, int y)
        => Assert.Equal(new Tile(x, y), From00("0. 0.\n0. 0#", WalkDir.S, lastAlongX)!.Value.To);

    [Fact]
    public void A_slide_with_no_axis_to_prefer_goes_along_x()
        => Assert.Equal(new Tile(1, 0), From00("0. 0.\n0. 0#", WalkDir.S)!.Value.To);

    [Fact]
    public void A_blocked_straight_step_doesnt_slide()
        => Assert.Null(From00("0. 0#\n0. 0.", WalkDir.SE));

    [Fact]
    public void A_diagonal_can_climb_4_through_two_legal_legs()
        => Assert.Equal(new WalkMove(new Tile(1, 1), 2, Diagonal: true, Slid: false), From00("0. 2.\n2. 4.", WalkDir.S));

    [Fact]
    public void A_diagonal_costs_half_its_cheaper_l_path_rounded_up()
    {
        Assert.Equal(1, From00("0. 0.\n0^ 0.", WalkDir.S)!.Value.Cost);     // 2 by way of (1, 0)
        Assert.Equal(2, From00("0. 0^\n0^ 0.", WalkDir.S)!.Value.Cost);     // 3 either way
    }

    [Fact]
    public void A_diagonal_needs_only_one_legal_l_path()
        => Assert.Equal(new WalkMove(new Tile(1, 1), 2, Diagonal: true, Slid: false), From00("0. 3.\n2. 3.", WalkDir.S));

    [Fact]
    public void A_diagonal_with_no_legal_l_path_slides()
        => Assert.Equal(new Tile(0, 1), From00("0. 3.\n0. 3.", WalkDir.S)!.Value.To);

    [Fact]
    public void A_pit_is_reached_but_cant_walk_back()
    {
        var terrain = TerrainOf("3. 3. 0.\n3. 0# 3.\n9# 3. 3.");

        Assert.Equal([new Tile(2, 0)], WorldWalk.Trapped(terrain, Origin, Jump));
    }

    [Fact]
    public void Ground_nobody_can_reach_isnt_trapped()
        => Assert.Empty(WorldWalk.Trapped(TerrainOf("0. 0# 0."), Origin, Jump));

    [Fact]
    public void The_steps_of_a_tile_are_its_legal_neighbours_on_the_axes()
        => Assert.Equal([new Tile(2, 1), new Tile(1, 2)], WorldWalk.Steps(TerrainOf("0. 0# 0.\n0# 0. 0.\n0. 0. 0#"), new Tile(1, 1), Jump));

    [Fact]
    public void The_pack_shares_one_ground_per_region()
    {
        var rules = RulesPack.Load([ContentPaths.BaseRules]);
        var id = rules.Regions.Keys.First();

        Assert.Same(rules.GroundFor(id), rules.GroundFor(id));
        Assert.Null(rules.GroundFor("nowhere"));
    }
}
