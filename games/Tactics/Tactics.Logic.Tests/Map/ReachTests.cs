using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class ReachTests
{
    private static Terrain TerrainOf(string ascii) => new(AsciiMap.Parse(ascii),
        ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    [Fact]
    public void Melee_blocked_by_height_3_without_reach()
    {
        var terrain = TerrainOf("0. 3. 2. 0.");

        Assert.False(Reach.Melee(terrain, new Tile(0, 0), new Tile(1, 0), reachWeapon: false));
        Assert.True(Reach.Melee(terrain, new Tile(0, 0), new Tile(1, 0), reachWeapon: true));
        Assert.True(Reach.Melee(terrain, new Tile(2, 0), new Tile(3, 0), reachWeapon: false)); // diff 2
        Assert.False(Reach.Melee(terrain, new Tile(1, 0), new Tile(0, 0), reachWeapon: false)); // from above, too
    }

    [Fact]
    public void Melee_needs_4_adjacent_tiles()
    {
        var terrain = TerrainOf("0. 0.\n0. 0.");

        Assert.True(Reach.Melee(terrain, new Tile(0, 0), new Tile(0, 1), false));
        Assert.False(Reach.Melee(terrain, new Tile(0, 0), new Tile(1, 1), false));
        Assert.False(Reach.Melee(terrain, new Tile(0, 0), new Tile(0, 0), false));
    }

    [Fact]
    public void Reach_weapon_hits_2_straight()
    {
        var terrain = TerrainOf("0. 0. 0.\n0. 0. 0.\n0. 0. 0.");

        Assert.True(Reach.Melee(terrain, new Tile(0, 0), new Tile(2, 0), reachWeapon: true));
        Assert.True(Reach.Melee(terrain, new Tile(0, 0), new Tile(0, 2), reachWeapon: true));
        Assert.False(Reach.Melee(terrain, new Tile(0, 0), new Tile(2, 0), reachWeapon: false));
        Assert.False(Reach.Melee(terrain, new Tile(0, 0), new Tile(2, 1), reachWeapon: true));
        Assert.False(Reach.Melee(terrain, new Tile(0, 0), new Tile(2, 2), reachWeapon: true));
    }

    [Fact]
    public void High_ground_bonus_needs_2_levels()
    {
        var terrain = TerrainOf("0. 1. 2. 3.");

        Assert.Equal(0, Reach.HighGroundBonus(terrain, new Tile(1, 0), new Tile(0, 0)));
        Assert.Equal(2, Reach.HighGroundBonus(terrain, new Tile(2, 0), new Tile(0, 0)));
        Assert.Equal(2, Reach.HighGroundBonus(terrain, new Tile(3, 0), new Tile(0, 0)));
        Assert.Equal(0, Reach.HighGroundBonus(terrain, new Tile(0, 0), new Tile(3, 0)));
        Assert.Equal(0, Reach.HighGroundBonus(terrain, new Tile(1, 0), new Tile(1, 0)));
    }

    [Fact]
    public void RangeBonus_floor_half_height()
    {
        var terrain = TerrainOf("0. 1. 2. 3. 5.");

        Assert.Equal(0, Reach.RangeBonus(terrain, new Tile(1, 0), new Tile(0, 0)));
        Assert.Equal(1, Reach.RangeBonus(terrain, new Tile(2, 0), new Tile(0, 0)));
        Assert.Equal(1, Reach.RangeBonus(terrain, new Tile(3, 0), new Tile(0, 0)));
        Assert.Equal(2, Reach.RangeBonus(terrain, new Tile(4, 0), new Tile(1, 0)));
        Assert.Equal(0, Reach.RangeBonus(terrain, new Tile(0, 0), new Tile(4, 0)));
    }

    [Fact]
    public void Distance_is_manhattan()
        => Assert.Equal(7, Reach.Distance(new Tile(1, 5), new Tile(4, 1)));

    [Fact]
    public void OppositeOf_is_the_tile_across_the_target_only_for_4_adjacent()
    {
        Assert.Equal(new Tile(4, 3), Reach.OppositeOf(new Tile(2, 3), new Tile(3, 3)));
        Assert.Equal(new Tile(3, 1), Reach.OppositeOf(new Tile(3, 3), new Tile(3, 2)));
        Assert.Null(Reach.OppositeOf(new Tile(1, 1), new Tile(2, 2)));
        Assert.Null(Reach.OppositeOf(new Tile(1, 1), new Tile(3, 1)));
        Assert.Null(Reach.OppositeOf(new Tile(1, 1), new Tile(1, 1)));
    }
}
