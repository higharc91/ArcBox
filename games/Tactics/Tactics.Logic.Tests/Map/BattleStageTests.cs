using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class BattleStageTests
{
    /// <summary>A 4-wide, 3-deep stage at (10, 20), 2 levels up: not square, so a swapped x and y can't pass.</summary>
    private static readonly BattleStage Stage = new(Map: "ford", Origin: new Tile(10, 20), Height: 2, Width: 4, Depth: 3);

    private static WorldRegion RegionWith(params RegionStamp[] stamps)
        => WorldRegion.From(new RegionDef("r", "R", 1, [64, 64], ["ground"], [], [.. stamps], [],
            new RegionSpawns(new Tile(0, 0), new Tile(1, 0)), []));

    private static BattleMap MapOf(string id, int width, int depth)
        => BattleMap.FromBytes(id, width, depth, new byte[width * depth * 3], AsciiMap.Materials, [], ImmutableSortedDictionary<string, ImmutableArray<Tile>>.Empty);

    [Fact]
    public void Tiles_convert_both_ways_with_the_offset()
    {
        Assert.Equal(new Tile(10, 20), Stage.ToWorld(new Tile(0, 0)));
        Assert.Equal(new Tile(13, 22), Stage.ToWorld(new Tile(3, 2)));
        Assert.Equal(new Tile(1, 2), Stage.ToBattle(new Tile(11, 22)));
        Assert.Equal(new Tile(-1, -20), Stage.ToBattle(new Tile(9, 0)));
    }

    /// <summary>Random stages and tiles, inside and out: each conversion undoes the other, and nothing else moves a tile.</summary>
    [Fact]
    public void Converting_there_and_back_is_the_same_tile_for_any_stage()
    {
        var random = new Random(5);
        for (var i = 0; i < 2000; i++)
        {
            var stage = new BattleStage(Map: "m", Origin: new Tile(random.Next(-50, 500), random.Next(-50, 500)), Height: random.Next(0, 9),
                Width: random.Next(1, 30), Depth: random.Next(1, 30));
            var t = new Tile(random.Next(-100, 600), random.Next(-100, 600));

            Assert.Equal(t, stage.ToBattle(stage.ToWorld(t)));
            Assert.Equal(t, stage.ToWorld(stage.ToBattle(t)));
            Assert.Equal((t.X - stage.Origin.X, t.Y - stage.Origin.Y), (stage.ToBattle(t).X, stage.ToBattle(t).Y));
        }
    }

    [Fact]
    public void Holds_covers_exactly_the_stamp()
    {
        var held = from x in Enumerable.Range(0, 30) from y in Enumerable.Range(10, 20) where Stage.Holds(new Tile(x, y)) select new Tile(x, y);

        Assert.Equal([.. from x in Enumerable.Range(10, 4) from y in Enumerable.Range(20, 3) select new Tile(x, y)], held.OrderBy(t => t));
    }

    /// <summary>Holds says yes exactly when the battle tile under it is on a map of the stage's size, for random stages.</summary>
    [Fact]
    public void Holds_agrees_with_the_battle_tile_for_any_stage()
    {
        var random = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var (width, depth) = (random.Next(1, 16), random.Next(1, 16));
            var stage = new BattleStage(Map: "m", Origin: new Tile(random.Next(0, 40), random.Next(0, 40)), Height: 0, Width: width, Depth: depth);
            var map = MapOf("m", width, depth);
            for (var x = stage.Origin.X - 2; x < stage.Origin.X + width + 2; x++)
                for (var y = stage.Origin.Y - 2; y < stage.Origin.Y + depth + 2; y++)
                    Assert.Equal(map.InBounds(stage.ToBattle(new Tile(x, y))), stage.Holds(new Tile(x, y)));
        }
    }

    [Fact]
    public void A_stage_is_its_stamp_and_its_maps_size()
    {
        var region = RegionWith(new RegionStamp("other", new Tile(1, 1), 0), new RegionStamp("ford", new Tile(10, 20), 2));

        Assert.Equal(Stage, BattleStage.Of(region, MapOf("ford", 4, 3)));
    }

    [Fact]
    public void A_map_with_no_stamp_has_no_stage()
    {
        Assert.Null(BattleStage.Of(RegionWith(new RegionStamp("ford", new Tile(10, 20), 2)), MapOf("elsewhere", 4, 3)));
        Assert.Null(BattleStage.Of(RegionWith(), MapOf("ford", 4, 3)));
    }

    [Fact]
    public void Two_encounters_on_one_map_have_one_stage()
    {
        var rules = BasePackTests.Rules;
        var region = rules.RegionFor("ashen_ford")!;
        var (assault, crossing) = (rules.Encounters["ford_assault"], rules.Encounters["river_crossing"]);

        Assert.Equal(assault.Map, crossing.Map);
        var stage = BattleStage.Of(region, rules.MapFor(assault.Map)!);
        Assert.NotNull(stage);
        Assert.Equal(stage, BattleStage.Of(region, rules.MapFor(crossing.Map)!));
    }

    /// <summary>
    /// On every stamp in the pack: every map tile goes to a world tile the stage holds and back again, the world there is that map
    /// tile raised by the stamp's height, and the stage holds no other tile of the region.
    /// </summary>
    [Fact]
    public void Every_stamp_in_the_pack_agrees_with_its_map_on_every_tile()
    {
        var rules = BasePackTests.Rules;
        var stamps = 0;
        foreach (var region in rules.Regions.Values)
            foreach (var stamp in region.Def.Stamps)
            {
                var map = rules.MapFor(stamp.Map)!;
                var stage = BattleStage.Of(region, map)!.Value;
                stamps++;
                for (var x = 0; x < map.Width; x++)
                    for (var y = 0; y < map.Height; y++)
                    {
                        var battle = new Tile(x, y);
                        var world = stage.ToWorld(battle);
                        Assert.True(stage.Holds(world));
                        Assert.Equal(battle, stage.ToBattle(world));
                        var (onMap, inWorld) = (map[battle], region.TileAt(world)!);
                        Assert.Equal(onMap.Height + stage.Height, inWorld.Height);
                        Assert.Equal(map.Materials[onMap.Material], region.Def.Materials[inWorld.Material]);
                        Assert.Equal(onMap.Flags, inWorld.Flags);
                    }

                var held = 0;
                for (var x = 0; x < region.Def.Size[0]; x++)
                    for (var y = 0; y < region.Def.Size[1]; y++)
                    {
                        var world = new Tile(x, y);
                        Assert.Equal(map.InBounds(stage.ToBattle(world)), stage.Holds(world));
                        held += stage.Holds(world) ? 1 : 0;
                    }
                Assert.Equal(map.Width * map.Height, held);
            }
        Assert.NotEqual(0, stamps);
    }

    // ---- Standable --------------------------------------------------------------------------------------------------

    private static Terrain Ground(string ascii) => new(AsciiMap.Parse(ascii), ImmutableSortedDictionary<Tile, int>.Empty, []);

    [Fact]
    public void Standable_keeps_a_legal_tile()
        => Assert.Equal(new Tile(1, 1), BattleStage.Standable(Ground("0. 0. 0.\n0. 0. 0.\n0. 0. 0."), new Tile(1, 1), [new Tile(0, 0)]));

    [Fact]
    public void Standable_finds_the_nearest_legal_tile()
    {
        // (2, 2) is a wall in a ring of walls, deep water and a blocking prop. The nearest open tiles are 2 steps away, (0, 2) and
        // (2, 4) among them, and of those the lowest tile goes: (0, 2), as (1, 1) and (1, 3) are walls too.
        var ground = Ground("""
            0. 0. 0. 0. 0.
            0. 0# 0# 0# 0.
            0. 0x 0# 0C 0.
            0. 0# 0# 0# 0.
            0. 0. 0. 0. 0.
            """);

        Assert.Equal(new Tile(0, 2), BattleStage.Standable(ground, new Tile(2, 2), [new Tile(4, 4)]));
    }

    [Fact]
    public void Standable_prefers_one_step_to_two()
        => Assert.Equal(new Tile(2, 1), BattleStage.Standable(Ground("""
            0. 0. 0. 0. 0.
            0. 0# 0. 0# 0.
            0. 0# 0# 0# 0.
            """), new Tile(2, 2), []));

    [Fact]
    public void Standable_looks_as_far_as_its_reach_and_no_further()
    {
        var row = (int open) => Ground(string.Join(' ', Enumerable.Range(0, 9).Select(x => x == open ? "0." : "0#")));

        Assert.Equal(new Tile(BattleStage.StandReach, 0), BattleStage.Standable(row(BattleStage.StandReach), new Tile(0, 0), []));
        Assert.Null(BattleStage.Standable(row(BattleStage.StandReach + 1), new Tile(0, 0), []));
    }

    [Fact]
    public void Standable_falls_back_when_nothing_near_is_legal()
    {
        var ground = Ground(string.Join('\n', Enumerable.Range(0, 9).Select(y => string.Join(' ', Enumerable.Range(0, 9).Select(x => x < 5 && y < 5 ? "0#" : "0.")))));

        // The first fallback is a wall too, so the second goes.
        Assert.Equal(new Tile(8, 8), BattleStage.Standable(ground, new Tile(0, 0), [new Tile(1, 1), new Tile(8, 8), new Tile(7, 7)]));
        Assert.Null(BattleStage.Standable(ground, new Tile(0, 0), [new Tile(1, 1)]));
        Assert.Null(BattleStage.Standable(ground, new Tile(0, 0), default));
    }

    /// <summary>
    /// The far bank: the nearest walkable tile is one the rule turns down (in the game, one the region's start can't reach, or inside an
    /// armed stamp), so the next nearest that it allows goes, and a fallback it turns down is passed over like a wall.
    /// </summary>
    [Fact]
    public void Standable_passes_over_tiles_the_rule_turns_down()
    {
        // (2, 0) is in the river. (3, 0) is one step east, but the rule refuses everything east of the river; (0, 0) is two steps west.
        var ground = Ground("0. 0x 0x 0.");
        var westOnly = (Tile t) => t.X < 2;

        Assert.Equal(new Tile(3, 0), BattleStage.Standable(ground, new Tile(2, 0), []));
        Assert.Equal(new Tile(0, 0), BattleStage.Standable(ground, new Tile(2, 0), [], westOnly));

        // Too far to look for, so it comes down to the fallbacks, and the first of them is refused.
        var wide = Ground("0. 0x 0x 0x 0x 0.");
        Assert.Equal(new Tile(0, 0), BattleStage.Standable(wide, new Tile(5, 0), [new Tile(5, 0), new Tile(0, 0)], t => t.X == 0));
        Assert.Null(BattleStage.Standable(wide, new Tile(5, 0), [new Tile(5, 0)], t => t.X == 0));
    }

    /// <summary>The tile asked for is kept only if the rule allows it, even when it's walkable.</summary>
    [Fact]
    public void Standable_tests_the_tile_asked_for_against_the_rule()
        => Assert.Equal(new Tile(1, 0), BattleStage.Standable(Ground("0. 0. 0."), new Tile(2, 0), [], t => t.X < 2));

    [Fact]
    public void Standable_never_picks_a_tile_off_the_ground()
        => Assert.Equal(new Tile(0, 1), BattleStage.Standable(Ground("0#\n0."), new Tile(0, 0), []));
}
