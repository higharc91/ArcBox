using System.Numerics;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

/// <summary><see cref="BoardFit"/>: the board at a whole multiple of the art, centred in the room the HUD leaves it.</summary>
public sealed class BoardFitTests
{
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

    [Fact]
    public void To_screen_scales_from_the_origin()
    {
        var fit = new BoardFit(new Vector2(100, 50), 2);

        Assert.Equal(new Vector2(164, 82), fit.ToScreen(new Vector2(32, 16)));
    }
}
