using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class AreasTests
{
    private static readonly Terrain Open = new(
        AsciiMap.Parse(string.Join('\n', Enumerable.Range(0, 9).Select(_ => string.Join(' ', Enumerable.Repeat("0.", 9))))),
        ImmutableSortedDictionary<Tile, int>.Empty, ImmutableSortedSet<Tile>.Empty);

    private static ImmutableArray<Tile> Tiles(AreaDef area, Tile caster, Tile aim) => Areas.Tiles(area, caster, aim, Open);

    [Fact]
    public void Burst_2_is_13_tiles()
    {
        var tiles = Tiles(new AreaDef(AreaShape.Burst, 2), new Tile(0, 0), new Tile(4, 4));

        Assert.Equal(13, tiles.Length);
        Assert.All(tiles, t => Assert.True(Reach.Distance(t, new Tile(4, 4)) <= 2));
        Assert.Equal(tiles.OrderBy(t => t).ToArray(), tiles.ToArray());
    }

    [Fact]
    public void Burst_is_cut_off_at_the_map_edge()
        => Assert.Equal(6, Tiles(new AreaDef(AreaShape.Burst, 2), new Tile(4, 4), new Tile(0, 0)).Length);

    [Fact]
    public void Cone_2_east_is_4_tiles()
    {
        var tiles = Tiles(new AreaDef(AreaShape.Cone, 2), new Tile(2, 4), new Tile(6, 4));

        Assert.Equal([new Tile(3, 4), new Tile(4, 3), new Tile(4, 4), new Tile(4, 5)], tiles);
    }

    [Fact]
    public void Cone_3_south_widens_by_one_each_side_per_step()
    {
        var tiles = Tiles(new AreaDef(AreaShape.Cone, 3), new Tile(4, 1), new Tile(4, 8));

        Assert.Equal(9, tiles.Length);
        Assert.Contains(new Tile(2, 4), tiles);
        Assert.Contains(new Tile(6, 4), tiles);
        Assert.DoesNotContain(new Tile(4, 1), tiles);
    }

    [Fact]
    public void Dominant_axis_decides_the_direction_and_ties_go_to_x()
    {
        // Aim is mostly north.
        Assert.Equal([new Tile(4, 2), new Tile(4, 3)], Tiles(new AreaDef(AreaShape.Line, 2), new Tile(4, 4), new Tile(5, 0)));
        // Perfect diagonal: X axis wins (east).
        Assert.Equal([new Tile(5, 4), new Tile(6, 4)], Tiles(new AreaDef(AreaShape.Line, 2), new Tile(4, 4), new Tile(6, 6)));
    }

    [Fact]
    public void Line_3_north_is_3_tiles()
    {
        var tiles = Tiles(new AreaDef(AreaShape.Line, 3), new Tile(4, 6), new Tile(4, 0));

        Assert.Equal([new Tile(4, 3), new Tile(4, 4), new Tile(4, 5)], tiles);
    }

    [Fact]
    public void Line_is_cut_off_at_the_map_edge()
        => Assert.Equal(2, Tiles(new AreaDef(AreaShape.Line, 5), new Tile(4, 2), new Tile(4, 0)).Length);

    [Fact]
    public void Line_and_cone_aimed_at_the_caster_cover_nothing()
    {
        Assert.Empty(Tiles(new AreaDef(AreaShape.Line, 3), new Tile(4, 4), new Tile(4, 4)));
        Assert.Empty(Tiles(new AreaDef(AreaShape.Cone, 3), new Tile(4, 4), new Tile(4, 4)));
    }

    [Fact]
    public void Cross_1_is_5_tiles()
    {
        var tiles = Tiles(new AreaDef(AreaShape.Cross, 1), new Tile(0, 0), new Tile(4, 4));

        Assert.Equal([new Tile(3, 4), new Tile(4, 3), new Tile(4, 4), new Tile(4, 5), new Tile(5, 4)], tiles);
    }

    [Fact]
    public void Ring_2_is_8_tiles()
    {
        var tiles = Tiles(new AreaDef(AreaShape.Ring, 2), new Tile(0, 0), new Tile(4, 4));

        Assert.Equal(8, tiles.Length);
        Assert.All(tiles, t => Assert.Equal(2, Reach.Distance(t, new Tile(4, 4))));
    }

    [Fact]
    public void Single_is_the_aim_and_Self_is_the_caster()
    {
        Assert.Equal([new Tile(6, 6)], Tiles(new AreaDef(AreaShape.Single), new Tile(1, 1), new Tile(6, 6)));
        Assert.Equal([new Tile(1, 1)], Tiles(new AreaDef(AreaShape.Self), new Tile(1, 1), new Tile(6, 6)));
    }

    [Fact]
    public void Unit_wide_shapes_cover_no_tiles()
    {
        Assert.Empty(Tiles(new AreaDef(AreaShape.AllAllies), new Tile(1, 1), new Tile(6, 6)));
        Assert.Empty(Tiles(new AreaDef(AreaShape.AllEnemies), new Tile(1, 1), new Tile(6, 6)));
    }
}
