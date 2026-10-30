namespace Tactics.Logic.Tests;

public sealed class AsciiMapTests
{
    [Fact]
    public void Parses_a_3x2_map_heights_flags_and_props()
    {
        var map = AsciiMap.Parse("""
            0. 1~ 2#
            3^ 4x 5C
            """);

        Assert.Equal("ascii", map.Id);
        Assert.Equal(3, map.Width);
        Assert.Equal(2, map.Height);

        Assert.Equal(new TileInfo(0, 0, TileFlags.None), map[new Tile(0, 0)]);
        Assert.Equal(new TileInfo(1, 0, TileFlags.Water | TileFlags.Difficult), map[new Tile(1, 0)]);
        Assert.Equal(new TileInfo(2, 0, TileFlags.Impassable), map[new Tile(2, 0)]);
        Assert.Equal(new TileInfo(3, 0, TileFlags.Difficult), map[new Tile(0, 1)]);
        Assert.Equal(new TileInfo(4, 0, TileFlags.Deep), map[new Tile(1, 1)]);
        Assert.Equal(new TileInfo(5, 0, TileFlags.None), map[new Tile(2, 1)]);

        var prop = Assert.Single(map.Props);
        Assert.Equal(new PropPlacement("block", new Tile(2, 1), true, Cover.Total), prop);
    }

    [Fact]
    public void Crate_is_a_walkable_half_cover_prop()
    {
        var map = AsciiMap.Parse("0c 0.");
        var prop = Assert.Single(map.Props);
        Assert.Equal(new PropPlacement("crate", new Tile(0, 0), false, Cover.Half), prop);
    }

    [Fact]
    public void In_bounds_is_false_outside_the_grid()
    {
        var map = AsciiMap.Parse("0. 0.\n0. 0.");
        Assert.True(map.InBounds(new Tile(1, 1)));
        Assert.False(map.InBounds(new Tile(2, 0)));
        Assert.False(map.InBounds(new Tile(0, -1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => map[new Tile(5, 5)]);
    }

    [Fact]
    public void Ragged_rows_and_bad_cells_are_rejected()
    {
        Assert.Throws<FormatException>(() => AsciiMap.Parse("0. 0.\n0."));
        Assert.Throws<FormatException>(() => AsciiMap.Parse("0? 0."));
        Assert.Throws<FormatException>(() => AsciiMap.Parse(".0 0."));
    }
}
