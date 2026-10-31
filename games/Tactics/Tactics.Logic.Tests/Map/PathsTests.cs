using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class PathsTests
{
    private static Terrain TerrainOf(string ascii) => new(AsciiMap.Parse(ascii),
        ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    private static Mover MoverOf(int move, int jump = 2, Tile[]? allies = null, Tile[]? enemies = null) => new(move, jump,
        (allies ?? []).ToImmutableSortedSet(), (enemies ?? []).ToImmutableSortedSet());

    private static string OpenMap(int w, int h)
        => string.Join('\n', Enumerable.Range(0, h).Select(_ => string.Join(' ', Enumerable.Repeat("0.", w))));

    [Fact]
    public void Flat_move_4_reaches_diamond_of_radius_4()
    {
        var map = Paths.From(TerrainOf(OpenMap(9, 9)), new Tile(4, 4), MoverOf(4));

        Assert.Equal(41, map.Reachable.Count);
        foreach (var t in map.Reachable)
            Assert.True(Math.Abs(t.X - 4) + Math.Abs(t.Y - 4) <= 4);
        Assert.Equal(4, map.CostTo(new Tile(0, 4)));
        Assert.Equal(int.MaxValue, map.CostTo(new Tile(0, 0)));
    }

    [Fact]
    public void Origin_is_reachable_at_cost_zero_with_an_empty_path()
    {
        var map = Paths.From(TerrainOf("0. 0."), new Tile(0, 0), MoverOf(4));
        Assert.Contains(new Tile(0, 0), map.Reachable);
        Assert.Equal(0, map.CostTo(new Tile(0, 0)));
        Assert.Empty(map.PathTo(new Tile(0, 0)));
    }

    [Fact]
    public void Climb_2_costs_2_and_climb_3_is_blocked_at_jump_2()
    {
        var terrain = TerrainOf("0. 2. 0. 3.");
        Assert.Equal(2, Paths.StepCost(terrain, new Tile(0, 0), new Tile(1, 0), 2));
        Assert.Equal(int.MaxValue, Paths.StepCost(terrain, new Tile(2, 0), new Tile(3, 0), 2));
        Assert.Equal(3, Paths.StepCost(terrain, new Tile(2, 0), new Tile(3, 0), 3));

        var map = Paths.From(terrain, new Tile(0, 0), MoverOf(8, jump: 2));
        Assert.Equal(2, map.CostTo(new Tile(1, 0)));
        Assert.Equal(3, map.CostTo(new Tile(2, 0)));
        Assert.Equal(int.MaxValue, map.CostTo(new Tile(3, 0)));
        Assert.DoesNotContain(new Tile(3, 0), map.Reachable);
    }

    [Fact]
    public void Climb_1_costs_1()
    {
        var terrain = TerrainOf("0. 1.");
        Assert.Equal(1, Paths.StepCost(terrain, new Tile(0, 0), new Tile(1, 0), 2));
    }

    [Fact]
    public void Drop_any_height_is_free()
    {
        var terrain = TerrainOf("9. 0. 5.");
        Assert.Equal(1, Paths.StepCost(terrain, new Tile(0, 0), new Tile(1, 0), 0));
        var map = Paths.From(terrain, new Tile(0, 0), MoverOf(1, jump: 0));
        Assert.Equal(1, map.CostTo(new Tile(1, 0)));
    }

    [Fact]
    public void Difficult_costs_2()
    {
        var terrain = TerrainOf("0. 0^ 0~ 0.");
        Assert.Equal(2, Paths.StepCost(terrain, new Tile(0, 0), new Tile(1, 0), 2));
        var map = Paths.From(terrain, new Tile(0, 0), MoverOf(4));
        Assert.Equal(2, map.CostTo(new Tile(1, 0)));
        Assert.Equal(4, map.CostTo(new Tile(2, 0)));
        Assert.Equal(int.MaxValue, map.CostTo(new Tile(3, 0)));
    }

    [Fact]
    public void Difficult_zone_and_height_delta_are_honoured()
    {
        var map0 = AsciiMap.Parse("0. 0. 0.");
        var terrain = new Terrain(map0,
            ImmutableSortedDictionary<Tile, int>.Empty.Add(new Tile(2, 0), 3),
            ImmutableSortedSet<Tile>.Empty.Add(new Tile(1, 0)));
        Assert.True(terrain.Difficult(new Tile(1, 0)));
        Assert.Equal(3, terrain.HeightAt(new Tile(2, 0)));
        Assert.Equal(2, Paths.StepCost(terrain, new Tile(0, 0), new Tile(1, 0), 2));
        Assert.Equal(int.MaxValue, Paths.StepCost(terrain, new Tile(1, 0), new Tile(2, 0), 2));
    }

    [Fact]
    public void Impassable_deep_blocking_props_and_out_of_bounds_are_illegal_but_crates_are_walkable()
    {
        var terrain = TerrainOf("0. 0# 0x 0C 0c");
        Assert.False(terrain.Walkable(new Tile(1, 0)));
        Assert.False(terrain.Walkable(new Tile(2, 0)));
        Assert.False(terrain.Walkable(new Tile(3, 0)));
        Assert.True(terrain.Walkable(new Tile(4, 0)));
        Assert.False(terrain.Walkable(new Tile(-1, 0)));
        Assert.True(terrain.Deep(new Tile(2, 0)));
        Assert.Equal("block", terrain.PropAt(new Tile(3, 0))?.Id);
        Assert.Equal("crate", terrain.PropAt(new Tile(4, 0))?.Id);
        Assert.Null(terrain.PropAt(new Tile(0, 0)));
        Assert.Equal(int.MaxValue, Paths.StepCost(terrain, new Tile(0, 0), new Tile(-1, 0), 2));
        Assert.Equal(int.MaxValue, Paths.StepCost(terrain, new Tile(0, 0), new Tile(1, 0), 2));
    }

    [Fact]
    public void Can_pass_through_ally_but_not_end_there()
    {
        var terrain = TerrainOf("0. 0. 0.");
        var ally = new Tile(1, 0);
        var map = Paths.From(terrain, new Tile(0, 0), MoverOf(4, allies: [ally]));

        Assert.DoesNotContain(ally, map.Reachable);
        Assert.Contains(new Tile(2, 0), map.Reachable);
        Assert.Equal(2, map.CostTo(new Tile(2, 0)));
        Assert.Equal([ally, new Tile(2, 0)], map.PathTo(new Tile(2, 0)));
        Assert.Empty(map.PathTo(ally));
        Assert.Equal(int.MaxValue, map.CostTo(ally));
    }

    [Fact]
    public void Enemy_blocks()
    {
        var terrain = TerrainOf("0. 0. 0.");
        var map = Paths.From(terrain, new Tile(0, 0), MoverOf(4, enemies: [new Tile(1, 0)]));

        Assert.Equal([new Tile(0, 0)], map.Reachable);
        Assert.Equal(int.MaxValue, map.CostTo(new Tile(2, 0)));
        Assert.Empty(map.PathTo(new Tile(2, 0)));
    }

    [Fact]
    public void Path_is_deterministic()
    {
        var terrain = TerrainOf(OpenMap(3, 3));
        var first = Paths.From(terrain, new Tile(0, 0), MoverOf(4)).PathTo(new Tile(2, 2));
        var second = Paths.From(terrain, new Tile(0, 0), MoverOf(4)).PathTo(new Tile(2, 2));

        Assert.Equal(first, second);
        Assert.Equal(4, first.Length);
        Assert.Equal(new Tile(2, 2), first[^1]);
        // Equal-cost ties go to the lower ordinal tile (X, then Y): down the left edge, then across.
        Assert.Equal([new Tile(0, 1), new Tile(0, 2), new Tile(1, 2), new Tile(2, 2)], first);
    }

    [Fact]
    public void Path_from_a_non_corner_origin_on_a_non_square_map_walks_back_to_it()
    {
        var terrain = TerrainOf(OpenMap(6, 3));
        var map = Paths.From(terrain, new Tile(4, 1), MoverOf(4));
        Assert.Equal([new Tile(3, 1), new Tile(2, 1), new Tile(1, 1)], map.PathTo(new Tile(1, 1)));
        Assert.Equal([new Tile(4, 2)], map.PathTo(new Tile(4, 2)));
    }

    [Fact]
    public void Reachable_on_a_non_square_map_is_the_tiles_it_can_end_on()
    {
        var map = Paths.From(TerrainOf(OpenMap(6, 3)), new Tile(4, 1), MoverOf(1));
        Assert.Equal([new Tile(3, 1), new Tile(4, 0), new Tile(4, 1), new Tile(4, 2), new Tile(5, 1)], map.Reachable);
        Assert.All(map.Reachable, t => Assert.NotEqual(int.MaxValue, map.CostTo(t)));
    }

    [Fact]
    public void Reachable_is_listed_in_ordinal_tile_order()
    {
        var map = Paths.From(TerrainOf(OpenMap(5, 5)), new Tile(2, 2), MoverOf(3));
        Assert.Equal(map.Reachable.OrderBy(t => t).ToArray(), map.Reachable.ToArray());
    }

    [Fact]
    public void Path_cost_matches_sum_of_step_costs()
    {
        var terrain = TerrainOf("0. 1. 3^ 3. 1~\n0. 0^ 2. 0. 0.");
        var origin = new Tile(0, 0);
        var map = Paths.From(terrain, origin, MoverOf(10, jump: 2));
        foreach (var t in map.Reachable)
        {
            var sum = 0;
            var from = origin;
            foreach (var step in map.PathTo(t))
            {
                sum += Paths.StepCost(terrain, from, step, 2);
                from = step;
            }
            Assert.Equal(map.CostTo(t), sum);
        }
    }
}
