using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="BoardFit"/>: the board at a whole multiple of the art, centred in the room the HUD leaves it.</summary>
public sealed class BoardFitTests
{
    private const int MaxHeight = 9;    // AlterTerrainBlock's limit (Effects/Blocks/MapBlocks.cs)

    [Fact]
    public void Takes_the_largest_whole_scale_that_fits()
    {
        Assert.Equal(2, BoardFit.Of(new Vector2(-400, 0), new Vector2(400, 450)).Scale);
        Assert.Equal(1, BoardFit.Of(new Vector2(-600, 0), new Vector2(600, 450)).Scale);
        Assert.Equal(4, BoardFit.Of(new Vector2(-100, 0), new Vector2(100, 100)).Scale);
    }

    [Fact]
    public void A_board_too_big_for_whole_scales_shrinks_to_fit()
        => Assert.Equal(0.5f, BoardFit.Of(new Vector2(0, 0), new Vector2(BoardFit.Area.Width * 2, 10)).Scale);

    [Fact]
    public void Centres_the_board_in_the_area()
    {
        var fit = BoardFit.Of(new Vector2(-100, 10), new Vector2(300, 210));
        var min = fit.ToScreen(new Vector2(-100, 10));
        var max = fit.ToScreen(new Vector2(300, 210));

        Assert.Equal(BoardFit.Area.X + BoardFit.Area.Width / 2, (min.X + max.X) / 2);
        Assert.Equal(BoardFit.Area.Y + BoardFit.Area.Height / 2, (min.Y + max.Y) / 2);
    }

    [Fact]
    public void Sits_on_whole_pixels_so_nearest_filtering_stays_crisp()
    {
        var fit = BoardFit.Of(new Vector2(-101, 11), new Vector2(300, 212));

        Assert.Equal(MathF.Round(fit.Origin.X), fit.Origin.X);
        Assert.Equal(MathF.Round(fit.Origin.Y), fit.Origin.Y);
    }

    private static readonly Tile BackCorner = new(0, 0);

    private static BoardFit FitOf(string map, BoardChanges? changes = null)
        => BoardFit.For(new BoardTerrain(Pack.Map(map), changes), Pack.Art);

    private static BoardPlan PlanOf(string map, BoardChanges? changes = null)
        => BoardPlan.Build(new BoardTerrain(Pack.Map(map), changes), Pack.Art);

    private static BoardChanges Raise(Tile tile, int delta) => new([], [new HeightChange(tile, delta)], []);

    [Theory]
    [InlineData("crypt_hall", 2)]       // one Stone Spire on the wall: the board used to halve
    [InlineData("warlord_bridge", 4)]   // two raises, likewise
    [InlineData("crystal_cave", 4)]
    [InlineData("river_crossing", 1)]   // a small raise, which used to recentre it
    public void A_raised_back_corner_leaves_the_scale_and_offset_alone(string map, int delta)
    {
        var raised = Raise(BackCorner, delta);

        Assert.True(PlanOf(map, raised).Min.Y < PlanOf(map).Min.Y);   // the board really did grow past its old bounds
        Assert.Equal(FitOf(map), FitOf(map, raised));
    }

    [Theory]
    [InlineData("river_crossing")]
    [InlineData("forest_ruins")]
    public void A_zone_on_the_back_corner_leaves_the_scale_and_offset_alone(string map)
    {
        var fire = new BoardChanges([new ZoneView(1, "fire", [BackCorner])], [], []);

        Assert.True(PlanOf(map, fire).Min.Y < PlanOf(map).Min.Y);     // its flames poke above the corner
        Assert.Equal(FitOf(map), FitOf(map, fire));
    }

    [Fact]
    public void Sinking_the_bridge_leaves_the_scale_and_offset_alone()
    {
        ImmutableArray<Tile> bridge = [new Tile(5, 5), new Tile(6, 5)];

        Assert.Equal(FitOf("warlord_bridge"), FitOf("warlord_bridge", new BoardChanges([], [.. bridge.Select(t => new HeightChange(t, -2))], bridge)));
    }

    [Theory]
    [MemberData(nameof(MapIds))]
    public void The_rules_maximum_height_on_the_back_corner_clips_rather_than_rescales(string map)
    {
        var height = Pack.Map(map)[BackCorner].Height;

        var fit = FitOf(map, Raise(BackCorner, MaxHeight - height));

        Assert.Equal(FitOf(map), fit);
        Assert.Equal(2, fit.Scale);
    }

    [Theory]
    [MemberData(nameof(MapIds))]
    public void The_rules_maximum_height_stays_on_screen_from_five_tiles_in(string map)
        => Assert.True(FitOf(map).ToScreen(BoardProjection.Corner(5, 0, MaxHeight)).Y >= 0);

    public static TheoryData<string> MapIds() => new(Pack.Maps);

    [Fact]
    public void To_screen_scales_from_the_origin()
    {
        var fit = new BoardFit(new Vector2(100, 50), 2);

        Assert.Equal(new Vector2(164, 82), fit.ToScreen(new Vector2(32, 16)));
    }
}
