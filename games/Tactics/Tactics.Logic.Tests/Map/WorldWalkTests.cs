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
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap> { ["ascii"] = AsciiMap.Parse("0. 0C") }, ArtManifest.None);
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

        var def = new RegionDef("r", "R", 1, [64, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
            new RegionSpawns(Origin, Origin), [],
            [new RegionProps("tree", "autumn", [new Tile(1, 0)])]);
        var ground = WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(),
            new ArtManifest([], ImmutableDictionary<string, PropArtDef>.Empty.Add("tree", new("tree", ["autumn"], true, Cover.Total, 4))));
        Assert.Null(WorldWalk.Next(ground.Terrain, Origin, WalkDir.E, Jump, null));
    }

    private static readonly ArtManifest Town = ArtManifests.Load(ContentPaths.ContentBase);

    /// <summary>A flat chunk with <paramref name="buildings"/> standing on it.</summary>
    private static WorldGround Built(params RegionBuilding[] buildings)
    {
        var def = new RegionDef("r", "R", 1, [32, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
            new RegionSpawns(Origin, Origin), [], Buildings: [.. buildings]);
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), Town);
    }

    [Fact]
    public void Every_tile_of_a_buildings_footprint_stops_the_party_as_its_manifest_says()
    {
        var terrain = Built(new RegionBuilding("tavern", "red", new Tile(10, 10))).Terrain;

        foreach (var tile in Town.Buildings["tavern"].Footprint(new Tile(10, 10)))
            Assert.False(terrain.Walkable(tile), $"{tile}");
        Assert.True(terrain.Walkable(new Tile(13, 10)));
        Assert.True(terrain.Walkable(new Tile(10, 12)));
    }

    [Theory]
    [InlineData("gate_u", 11, 10, 10, 10, 12, 10)]
    [InlineData("gate_v", 10, 11, 10, 10, 10, 12)]
    public void A_gates_middle_stays_walkable_and_its_towers_dont(string gate, int middleX, int middleY, int leftX, int leftY, int rightX, int rightY)
    {
        var terrain = Built(new RegionBuilding(gate, "grey", new Tile(10, 10))).Terrain;

        Assert.True(terrain.Walkable(new Tile(middleX, middleY)));
        Assert.False(terrain.Walkable(new Tile(leftX, leftY)));
        Assert.False(terrain.Walkable(new Tile(rightX, rightY)));
    }

    [Fact]
    public void A_bridge_deck_stays_walkable()
        => Assert.True(Built(new RegionBuilding("bridge_u", "grey", new Tile(10, 10))).Terrain.Walkable(new Tile(10, 10)));

    [Fact]
    public void The_map_knows_each_building_and_the_ground_it_stands_on()
    {
        var map = Built(new RegionBuilding("stall", "red", new Tile(10, 10))).Map;

        Assert.Equal(new BuildingPlacement("stall", "red", new Tile(10, 10), 0), Assert.Single(map.Buildings));
    }

    [Fact]
    public void A_doors_step_is_the_tile_outside_it()
    {
        // The pack's doors are all on the +y face, the side nearest the sofa on the left.
        Assert.Equal([new Tile(11, 12)], Town.Buildings["tavern"].DoorSteps(new Tile(10, 10)));
        Assert.Equal([new Tile(10, 12)], Town.Buildings["cottage"].DoorSteps(new Tile(10, 10)));
        Assert.Equal([new Tile(10, 11)], Town.Buildings["watchtower"].DoorSteps(new Tile(10, 10)));
    }

    [Fact]
    public void A_prop_nothing_defines_cant_quietly_be_walkable()
    {
        var def = new RegionDef("r", "R", 1, [32, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
            new RegionSpawns(Origin, Origin), [], [new RegionProps("tree", "autumn", [new Tile(1, 0)])]);

        Assert.Contains("'tree'", Assert.Throws<ArgumentException>(() => WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), ArtManifest.None)).Message);
    }

    [Fact]
    public void A_building_nothing_defines_cant_quietly_be_walkable()
    {
        var def = new RegionDef("r", "R", 1, [32, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
            new RegionSpawns(Origin, Origin), [], Buildings: [new RegionBuilding("tavern", "red", new Tile(1, 0))]);

        Assert.Contains("'tavern'", Assert.Throws<ArgumentException>(() => WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), ArtManifest.None)).Message);
    }

    [Fact]
    public void A_soft_prop_doesnt()
    {
        var def = new RegionDef("r", "R", 1, [64, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
            new RegionSpawns(Origin, Origin), [],
            [new RegionProps("bush", null, [new Tile(1, 0)])]);
        var ground = WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(),
            new ArtManifest([], ImmutableDictionary<string, PropArtDef>.Empty.Add("bush", new("bush", [], false, Cover.None, 1))));
        Assert.NotNull(WorldWalk.Next(ground.Terrain, Origin, WalkDir.E, Jump, null));
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

    /// <summary>A flat region with the props <paramref name="props"/>: a slim "tree" and a full "fence", as the art manifest says.</summary>
    private static Terrain Planted(params RegionProps[] props)
    {
        var def = new RegionDef("r", "R", 1, [64, 32], ["grass/lush"], [new RegionChunk(0, 0, RegionTests.Tiles())], [], [],
            new RegionSpawns(Origin, Origin), [], [.. props]);
        var art = new ArtManifest([], ImmutableDictionary<string, PropArtDef>.Empty
            .Add("tree", new("tree", ["oak"], true, Cover.Total, 4, Slim: true))
            .Add("fence", new("fence", ["oak"], true, Cover.Half, 1)));
        return WorldGround.Of(WorldRegion.From(def), new Dictionary<string, BattleMap>(), art).Terrain;
    }

    [Fact]
    public void A_diagonal_steps_past_a_trees_corner()
    {
        // A tree on one corner: the party cuts past its trunk instead of snagging and sliding.
        var terrain = Planted(new RegionProps("tree", "oak", [new Tile(2, 1)]));
        Assert.Equal(new WalkMove(new Tile(2, 2), 1, Diagonal: true, Slid: false), WorldWalk.Next(terrain, new Tile(1, 1), WalkDir.S, Jump, null));
        // The tree's own tile still stops the party.
        Assert.False(terrain.Walkable(new Tile(2, 1)));
        Assert.NotEqual(new Tile(2, 1), WorldWalk.Next(terrain, new Tile(1, 1), WalkDir.SE, Jump, null)?.To);
    }

    [Fact]
    public void A_diagonal_still_snags_on_a_fence_and_never_squeezes_between_two_trees()
    {
        var fence = Planted(new RegionProps("fence", "oak", [new Tile(2, 1)]));
        Assert.NotEqual(new Tile(2, 2), WorldWalk.Next(fence, new Tile(1, 1), WalkDir.S, Jump, null)!.Value.To);
        // Trees on both corners: no L-path is open, so the diagonal is refused and nothing new is reachable.
        var pair = Planted(new RegionProps("tree", "oak", [new Tile(2, 1), new Tile(1, 2)]));
        Assert.Null(WorldWalk.Next(pair, new Tile(1, 1), WalkDir.S, Jump, null));
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

    // A straight step is a road direction on screen (ne, se, sw, nw). Blocked, it slides round a one-tile jag in the wall, and only then.

    [Fact]
    public void A_blocked_straight_step_slides_round_a_one_tile_jag()
        => Assert.Equal(new WalkMove(new Tile(0, 1), 1, Diagonal: false, Slid: true), From00("0. 0#\n0. 0.", WalkDir.SE));

    [Fact]
    public void A_jag_slide_costs_the_side_step()
        => Assert.Equal(new WalkMove(new Tile(0, 1), 2, Diagonal: false, Slid: true), From00("0. 0#\n0~ 0.", WalkDir.SE));

    [Fact]
    public void A_blocked_straight_step_along_a_straight_wall_doesnt_slide()
        => Assert.Null(From00("0. 0#\n0. 0#", WalkDir.SE));

    [Fact]
    public void A_blocked_straight_step_with_both_sides_open_doesnt_guess()
        => Assert.Null(WorldWalk.Next(TerrainOf("0. 0.\n0. 0#\n0. 0."), new Tile(0, 1), WalkDir.SE, Jump, null));

    [Fact]
    public void A_jag_slide_needs_the_side_step_itself_to_be_legal()
    {
        Assert.Null(From00("0. 0#\n3. 0.", WalkDir.SE));      // too high to climb to the side
        Assert.Null(From00("0. 0#\n0. 3.", WalkDir.SE));      // the side is fine, but the way on from it is a climb too high
    }

    [Fact]
    public void The_map_edge_is_a_closed_side()
        => Assert.Null(WorldWalk.Next(TerrainOf("0. 0#\n0# 0."), Origin, WalkDir.SE, Jump, null));

    [Theory]
    [InlineData(WalkDir.NE)]
    [InlineData(WalkDir.SE)]
    [InlineData(WalkDir.SW)]
    [InlineData(WalkDir.NW)]
    public void A_jag_slide_is_the_same_rule_turned(WalkDir dir)
    {
        // A 5x5 open square with the tile ahead of the centre walled and one side of it walled too: the slide goes to the other side.
        var (dx, dy) = WorldWalk.Delta(dir);
        var centre = new Tile(2, 2);
        var (wall, closed, open) = (new Tile(2 + dx, 2 + dy), new Tile(2 + dy, 2 + dx), new Tile(2 - dy, 2 - dx));
        var terrain = TerrainOf(Square(5, t => t == wall || t == closed));

        Assert.Equal(new WalkMove(open, 1, Diagonal: false, Slid: true), WorldWalk.Next(terrain, centre, dir, Jump, null));
    }

    // ---- Walking along walls: what holding a direction does, on many random grounds --------------------------------------

    private static string Square(int size, Func<Tile, bool> wall)
        => string.Join('\n', Enumerable.Range(0, size).Select(y => string.Join(' ', Enumerable.Range(0, size).Select(x => wall(new Tile(x, y)) ? "0#" : "0."))));

    /// <summary>
    /// A random 14x14 ground: straight walls along both axes, a few with one-tile jags, corridors one wide, and scattered rocks, at
    /// random heights so that some steps are climbs too high.
    /// </summary>
    private static Terrain RandomGround(int seed)
    {
        const int Size = 14;
        var r = new Random(seed);
        var walls = new HashSet<Tile>();
        for (var w = 0; w < 4; w++)
        {
            var (alongX, at, from, to) = (r.Next(2) == 0, r.Next(Size), r.Next(Size / 2), r.Next(Size / 2, Size));
            var jag = r.Next(3) == 0 ? r.Next(from, to) : -1;
            for (var i = from; i < to; i++)
            {
                var off = jag >= 0 && i >= jag ? 1 : 0;
                walls.Add(alongX ? new Tile(i, at + off) : new Tile(at + off, i));
                if (r.Next(4) == 0)      // a corridor one wide
                    walls.Add(alongX ? new Tile(i, at + off + 2) : new Tile(at + off + 2, i));
            }
        }
        for (var i = 0; i < 10; i++)
            walls.Add(new Tile(r.Next(Size), r.Next(Size)));
        var heights = Enumerable.Range(0, Size).Select(_ => r.Next(5) == 0 ? 3 : 0).ToArray();
        var rows = Enumerable.Range(0, Size).Select(y => string.Join(' ', Enumerable.Range(0, Size)
            .Select(x => $"{(r.Next(8) == 0 ? heights[x] : 0)}{(walls.Contains(new Tile(x, y)) ? '#' : '.')}")));
        return TerrainOf(string.Join('\n', rows));
    }

    [Fact]
    public void Holding_a_direction_along_any_wall_never_steps_back_and_forth()
    {
        var groundsWithJags = 0;
        for (var seed = 1; seed <= 300; seed++)
        {
            var jags = 0;
            var terrain = RandomGround(seed);
            foreach (var start in Tiles(terrain).Where(terrain.Walkable))
                foreach (var dir in Enum.GetValues<WalkDir>())
                {
                    var (at, lastAlongX, trail) = (start, (bool?)null, new List<Tile> { start });
                    for (var step = 0; step < 30 && WorldWalk.Next(terrain, at, dir, Jump, lastAlongX) is { } move; step++)
                    {
                        if (move.Slid && WorldWalk.Delta(dir) is (0, _) or (_, 0))
                            jags++;         // a road direction slid round a jag
                        if (!move.Diagonal)
                            lastAlongX = move.To.Y == at.Y;
                        at = move.To;
                        trail.Add(at);
                    }
                    for (var i = 2; i < trail.Count; i++)
                        Assert.False(trail[i] == trail[i - 2], $"seed {seed}, {dir} from {start}: {string.Join(" ", trail)}");
                    // Never further back along the direction held than it started: every step, slides included, is forward or sideways.
                    var (dx, dy) = WorldWalk.Delta(dir);
                    for (var i = 1; i < trail.Count; i++)
                        Assert.True((trail[i].X - trail[i - 1].X) * dx + (trail[i].Y - trail[i - 1].Y) * dy >= 0, $"seed {seed}, {dir} from {start}: a step back");
                }
            groundsWithJags += jags > 0 ? 1 : 0;
        }
        // The run has to have walked round jags, or a jag rule that never slid would pass it as trivially jitter-free.
        Assert.True(groundsWithJags >= 250, $"only {groundsWithJags} of 300 grounds had a jag slide");
    }

    [Fact]
    public void Walking_reaches_what_the_floods_reach_and_nothing_more()
    {
        for (var seed = 1; seed <= 300; seed++)
        {
            var terrain = RandomGround(seed);
            var start = Tiles(terrain).First(terrain.Walkable);
            var walked = new HashSet<Tile> { start };
            var open = new Stack<Tile>([start]);
            while (open.TryPop(out var tile))
                foreach (var dir in Enum.GetValues<WalkDir>())
                    foreach (bool? axis in new bool?[] { null, true, false })
                        if (WorldWalk.Next(terrain, tile, dir, Jump, axis) is { } move)
                        {
                            Assert.True(move.Diagonal || WorldWalk.Legal(terrain, tile, move.To, Jump), $"seed {seed}: {tile} to {move.To} isn't a legal step");
                            Assert.Equal(1, Math.Max(Math.Abs(move.To.X - tile.X), Math.Abs(move.To.Y - tile.Y)));
                            if (walked.Add(move.To))
                                open.Push(move.To);
                        }

            Assert.Equal(WorldWalk.Reach(terrain, start, Jump).Order(), walked.Order());
        }
    }

    private static IEnumerable<Tile> Tiles(Terrain terrain)
        => Enumerable.Range(0, terrain.Map.Height).SelectMany(y => Enumerable.Range(0, terrain.Map.Width).Select(x => new Tile(x, y)));

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
        var rules = RulesPack.Load([ContentPaths.BaseRules], Town);
        var id = rules.Regions.Keys.First();

        Assert.Same(rules.GroundFor(id), rules.GroundFor(id));
        Assert.Null(rules.GroundFor("nowhere"));
    }

    /// <summary>
    /// Review (rules NOTE 5): the flood from a region's start is the same for every room, so the pack keeps one, next to the ground,
    /// rather than each resume, landing and scene's travel walking the whole region again.
    /// </summary>
    [Fact]
    public void The_pack_shares_one_flood_from_each_regions_start()
    {
        var rules = RulesPack.Load([ContentPaths.BaseRules], ArtManifests.Load(ContentPaths.ContentBase));
        var (id, region) = rules.Regions.First();

        var reach = rules.ReachFor(id)!;
        Assert.Same(reach, rules.ReachFor(id));
        Assert.True(WorldWalk.Reach(rules.GroundFor(id)!.Terrain, region.Def.Spawns.Start, ClassDef.DefaultJump).SetEquals(reach));
        Assert.Null(rules.ReachFor("nowhere"));
    }

    // ---- Routes, for the bots and the tests that play a chapter -------------------------------------------------------------------

    /// <summary>Walks <paramref name="taps"/> from <paramref name="from"/> by <see cref="WorldWalk.Next"/>, checking each is taken as asked.</summary>
    private static Tile Walked(Terrain terrain, Tile from, IEnumerable<WalkDir> taps)
    {
        foreach (var dir in taps)
        {
            var (dx, dy) = WorldWalk.Delta(dir);
            var move = WorldWalk.Next(terrain, from, dir, Jump, null);
            Assert.Equal(new WalkMove(new Tile(from.X + dx, from.Y + dy), move!.Value.Cost, move.Value.Diagonal, false), move);
            from = move.Value.To;
        }
        return from;
    }

    [Fact]
    public void A_route_goes_round_a_wall_by_steps_taken_as_asked()
    {
        var terrain = TerrainOf("0. 0. 0. 0.\n0# 0# 0# 0.\n0. 0. 0. 0.");
        var goal = new Tile(0, 2);

        var taps = WorldWalk.Route(terrain, Origin, t => t == goal, Jump)!.Value;

        Assert.Equal(goal, Walked(terrain, Origin, taps));
        Assert.Equal(8, taps.Length);       // along the top, down the open column, back along the bottom: a diagonal can't cut the wall's corners
    }

    [Fact]
    public void A_route_is_shortest_in_steps_with_diagonals()
    {
        var terrain = TerrainOf("0. 0. 0.\n0. 0. 0.\n0. 0. 0.");

        var taps = WorldWalk.Route(terrain, Origin, t => t == new Tile(2, 2), Jump)!.Value;

        Assert.Equal([WalkDir.S, WalkDir.S], taps);
    }

    [Fact]
    public void A_route_from_the_goal_is_empty()
        => Assert.Empty(WorldWalk.Route(TerrainOf("0. 0."), Origin, t => t == Origin, Jump)!.Value);

    [Fact]
    public void There_is_no_route_up_a_cliff()
        => Assert.Null(WorldWalk.Route(TerrainOf("0. 3."), Origin, t => t == new Tile(1, 0), Jump));

    [Fact]
    public void A_route_keeps_out_of_what_it_avoids_but_may_end_in_it()
    {
        var terrain = TerrainOf("0. 0. 0.\n0. 0. 0.");
        var avoid = (Tile t) => t.X == 1;

        Assert.Null(WorldWalk.Route(terrain, Origin, t => t == new Tile(2, 0), Jump, avoid));
        Assert.Equal(new Tile(1, 1), Walked(terrain, Origin, WorldWalk.Route(terrain, Origin, t => t == new Tile(1, 1), Jump, avoid)!.Value));
    }
}
