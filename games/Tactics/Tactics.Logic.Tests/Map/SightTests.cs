using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class SightTests
{
    private static Terrain TerrainOf(string ascii) => new(AsciiMap.Parse(ascii),
        ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    private static SightResult Trace(string ascii, Tile from, Tile to, bool ranged = true, params Tile[] units)
        => Sight.Trace(TerrainOf(ascii), from, to, units.ToImmutableSortedSet(), ranged);

    [Fact]
    public void Open_ground_no_cover()
    {
        var result = Trace("0. 0. 0. 0. 0.", new Tile(0, 0), new Tile(4, 0));

        Assert.Equal(0, result.RaysBlocked);
        Assert.Equal(Cover.None, result.Cover);
        Assert.True(result.HasLineOfSight);
    }

    [Fact]
    public void Obscured_tiles_block_every_ray_through_them_or_at_either_end()
    {
        var terrain = TerrainOf("0. 0. 0. 0. 0.");
        var none = ImmutableSortedSet<Tile>.Empty;
        var smoke = ImmutableSortedSet.Create(new Tile(2, 0));

        Assert.Equal(new SightResult(3, Cover.Total), Sight.Trace(terrain, new Tile(0, 0), new Tile(4, 0), none, true, smoke));
        Assert.False(Sight.Trace(terrain, new Tile(0, 0), new Tile(2, 0), none, true, smoke).HasLineOfSight);   // into it
        Assert.False(Sight.Trace(terrain, new Tile(2, 0), new Tile(4, 0), none, true, smoke).HasLineOfSight);   // out of it
        Assert.True(Sight.Trace(terrain, new Tile(3, 0), new Tile(4, 0), none, true, smoke).HasLineOfSight);    // beside it
    }

    [Fact]
    public void Adjacent_tiles_have_no_columns_between_them()
    {
        var result = Trace("0. 0.", new Tile(0, 0), new Tile(1, 0));
        Assert.Equal(new SightResult(0, Cover.None), result);
    }

    [Fact]
    public void Low_wall_one_level_gives_half()
    {
        // Only the feet ray dips below the wall top (1024) inside the wall's column.
        var result = Trace("0. 0. 1. 0. 0.", new Tile(0, 0), new Tile(4, 0));

        Assert.Equal(1, result.RaysBlocked);
        Assert.Equal(Cover.Half, result.Cover);
        Assert.True(result.HasLineOfSight);
    }

    [Fact]
    public void Two_level_wall_gives_three_quarters()
    {
        // From height 1 over a height-2 wall to a target on the ground: feet and torso rays are blocked, the head ray clears it.
        var result = Trace("1. 1. 2. 0. 0.", new Tile(0, 0), new Tile(4, 0));

        Assert.Equal(2, result.RaysBlocked);
        Assert.Equal(Cover.ThreeQuarters, result.Cover);
        Assert.True(result.HasLineOfSight);
    }

    [Fact]
    public void Tall_wall_blocks_sight()
    {
        var result = Trace("0. 0. 4. 0. 0.", new Tile(0, 0), new Tile(4, 0));

        Assert.Equal(3, result.RaysBlocked);
        Assert.Equal(Cover.Total, result.Cover);
        Assert.False(result.HasLineOfSight);
    }

    [Fact]
    public void High_ground_sees_over_wall()
    {
        var result = Trace("3. 2. 0. 0. 0.", new Tile(0, 0), new Tile(4, 0));

        Assert.Equal(new SightResult(0, Cover.None), result);
        Assert.False(Trace("0. 2. 0. 0. 0.", new Tile(0, 0), new Tile(4, 0)).HasLineOfSight); // the same wall from the ground blocks
    }

    [Fact]
    public void Blocking_prop_blocks_unless_the_ray_clears_its_top()
    {
        // A 'C' prop on the ground reaches level 2 (2048); a flat shot never clears it, a shot from height 2 does.
        Assert.False(Trace("0. 0. 0C 0. 0.", new Tile(0, 0), new Tile(4, 0)).HasLineOfSight);
        Assert.Equal(new SightResult(0, Cover.None), Trace("2. 0C 0. 0. 0.", new Tile(0, 0), new Tile(4, 0)));
    }

    [Fact]
    public void Grazing_corner_is_not_blocked()
    {
        // The ray passes exactly through the shared corner of the two diagonal neighbours at height 2560 + (1024 - 2560) / 2
        // (feet ray: 1408), above the height-1 wall corner (1024): nothing is blocked.
        var result = Trace("1. 1.\n0. 0.", new Tile(0, 0), new Tile(1, 1));

        Assert.Equal(new SightResult(0, Cover.None), result);
    }

    [Fact]
    public void A_ray_through_a_corner_checks_both_diagonal_neighbours()
    {
        // Same geometry, but a height-3 wall at (1, 0) touches the ray only at its corner: it still counts as a column.
        Assert.False(Trace("1. 3.\n0. 0.", new Tile(0, 0), new Tile(1, 1)).HasLineOfSight);
        Assert.False(Trace("1. 0.\n3. 0.", new Tile(0, 0), new Tile(1, 1)).HasLineOfSight);
    }

    [Fact]
    public void Diagonal_line_uses_the_supercover_columns()
    {
        // The (0,0)->(4,2) line crosses (1,0), (1,1), (2,1), (3,1), (3,2): a tall wall at (2,1) blocks.
        var result = Trace("0. 0. 0. 0. 0.\n0. 0. 5. 0. 0.\n0. 0. 0. 0. 0.", new Tile(0, 0), new Tile(4, 2));
        Assert.False(result.HasLineOfSight);

        // A tall wall at (2,0), off the line, does not.
        var clear = Trace("0. 0. 5. 0. 0.\n0. 0. 0. 0. 0.\n0. 0. 0. 0. 0.", new Tile(0, 0), new Tile(4, 2));
        Assert.Equal(new SightResult(0, Cover.None), clear);
    }

    [Fact]
    public void Unit_between_gives_half_only_for_ranged()
    {
        var ranged = Trace("0. 0. 0. 0. 0.", new Tile(0, 0), new Tile(4, 0), ranged: true, new Tile(2, 0));
        var melee = Trace("0. 0. 0. 0. 0.", new Tile(0, 0), new Tile(4, 0), ranged: false, new Tile(2, 0));

        Assert.Equal(Cover.Half, ranged.Cover);
        Assert.Equal(0, ranged.RaysBlocked);
        Assert.Equal(Cover.None, melee.Cover);
    }

    [Fact]
    public void The_two_end_units_do_not_give_cover()
    {
        var result = Trace("0. 0. 0.", new Tile(0, 0), new Tile(2, 0), ranged: true, new Tile(0, 0), new Tile(2, 0));
        Assert.Equal(Cover.None, result.Cover);
    }

    [Fact]
    public void Crate_adjacent_to_target_gives_half()
    {
        var result = Trace("0. 0. 0. 0c 0.", new Tile(0, 0), new Tile(4, 0));

        Assert.Equal(0, result.RaysBlocked);
        Assert.Equal(Cover.Half, result.Cover);
    }

    [Fact]
    public void Crate_behind_or_beside_the_target_gives_no_cover()
    {
        Assert.Equal(Cover.None, Trace("0. 0. 0. 0. 0c", new Tile(0, 0), new Tile(3, 0)).Cover);
        Assert.Equal(Cover.None, Trace("0. 0. 0. 0.\n0. 0. 0. 0c", new Tile(0, 0), new Tile(3, 0)).Cover);
    }

    [Fact]
    public void Final_cover_is_the_best_of_rays_units_and_props()
    {
        // Low wall (Half from rays) plus a unit (Half) stays Half; a total-cover prop beside the target on the attacker side wins.
        Assert.Equal(Cover.Half, Trace("0. 0. 1. 0. 0.", new Tile(0, 0), new Tile(4, 0), true, new Tile(1, 0)).Cover);
        Assert.Equal(Cover.Total, Trace("2. 0. 0. 0C 0.", new Tile(0, 0), new Tile(4, 0)).Cover);
    }
}
