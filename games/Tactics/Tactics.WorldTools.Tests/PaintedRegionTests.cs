using Tactics.Logic;

namespace Tactics.WorldTools.Tests;

public sealed class PaintedRegionTests
{
    private const string Grass = "grass/lush";

    private static readonly Tile A = new(1, 2), B = new(3, 4);

    private static PaintedRegion Painted() => new("r", "Region", 1, 8, 8)
    {
        Materials = new Dictionary<Tile, string> { [A] = Grass, [B] = "water/clear" },
        Starts = [A],
        Recoveries = [B],
    };

    [Fact]
    public void The_three_layers_become_one_tile()
    {
        var painted = Painted() with
        {
            Heights = new Dictionary<Tile, int> { [A] = 3 },
            Flags = new Dictionary<Tile, TileFlags> { [A] = TileFlags.Water | TileFlags.Deep },
        };

        var (source, problems) = painted.ToSource();

        Assert.Empty(problems);
        Assert.Equal(new TileInfoNamed(3, Grass, TileFlags.Water | TileFlags.Deep), source.Get(A));
    }

    [Fact]
    public void A_tile_with_no_height_or_flags_is_plain_ground_at_0()
        => Assert.Equal(new TileInfoNamed(0, "water/clear", TileFlags.None), Painted().ToSource().Source.Get(B));

    [Fact]
    public void A_region_keeps_its_names_size_and_markers()
    {
        var painted = Painted() with
        {
            Beats = [new BeatSpot("ch1_old_maud", B)],
            Stamps = [new RegionStamp("ford", A, 2)],
        };

        var source = painted.ToSource().Source;

        Assert.Equal(("r", "Region", 1, 8, 8), (source.Id, source.Name, source.Chapter, source.Width, source.Height));
        Assert.Equal((A, B), (source.Start, source.Recovery));
        Assert.Equal([new BeatSpot("ch1_old_maud", B)], source.Beats);
        Assert.Equal([new RegionStamp("ford", A, 2)], source.Stamps);
    }

    [Fact]
    public void A_stamp_keeps_the_painted_tiles_under_it()
    {
        // If a stamp copied its map here, a tile painted over would be put back and the export could never see the change.
        var source = (Painted() with { Stamps = [new RegionStamp("ford", A, 0)] }).ToSource().Source;

        Assert.Equal(Grass, source.Get(A)!.Material);
        Assert.Equal(2, source.Tiles.Count);
    }

    [Fact]
    public void A_height_with_no_material_is_a_problem_naming_the_first_tile()
    {
        var painted = Painted() with { Heights = new Dictionary<Tile, int> { [new Tile(6, 5)] = 1, [new Tile(5, 5)] = 1 } };

        Assert.Equal(["The tile (5, 5) has a height but no material."], painted.ToSource().Problems);
    }

    [Fact]
    public void Flags_with_no_material_are_a_problem_naming_the_first_tile()
    {
        var painted = Painted() with { Flags = new Dictionary<Tile, TileFlags> { [new Tile(6, 5)] = TileFlags.Water } };

        Assert.Equal(["The tile (6, 5) has flags but no material."], painted.ToSource().Problems);
    }

    [Fact]
    public void A_scene_with_no_start_is_a_problem()
        => Assert.Equal(["The scene has no start spawn."], (Painted() with { Starts = [] }).ToSource().Problems);

    [Fact]
    public void A_scene_with_no_recovery_is_a_problem()
        => Assert.Equal(["The scene has no recovery spawn."], (Painted() with { Recoveries = [] }).ToSource().Problems);

    [Fact]
    public void A_scene_with_two_starts_is_a_problem()
        => Assert.Equal(["The scene has 2 start spawns, at (1, 2) and (3, 4), but needs one."],
            (Painted() with { Starts = [A, B] }).ToSource().Problems);

    [Fact]
    public void A_scene_with_three_recoveries_is_a_problem()
        => Assert.Equal(["The scene has 3 recovery spawns, at (1, 2), (3, 4) and (1, 2), but needs one."],
            (Painted() with { Recoveries = [A, B, A] }).ToSource().Problems);

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(31.9, 32, 0, 1)]
    [InlineData(16, 48, 0, 1)]
    [InlineData(320, 63.5, 10, 1)]
    [InlineData(-0.5, -32, -1, -1)]
    [InlineData(-32.5, 0, -2, 0)]
    public void A_marker_stands_on_the_tile_its_position_falls_in(double x, double y, int tileX, int tileY)
        => Assert.Equal(new Tile(tileX, tileY), PaintedRegion.TileAt(x, y));
}
