using System.Numerics;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

public sealed class CinematicFitTests
{
    [Fact]
    public void Off_preserves_fit_and_a_held_closeup_centres_the_focus_on_screen_pixels()
    {
        var fit = new BoardFit(new(21, 34), 2);
        Assert.Equal(fit, CinematicFit.Of(fit, null));
        Assert.Equal(fit, CinematicFit.Of(fit, new(1, new(10, 20), 0, false)));
        var held = CinematicFit.Of(fit, new(2, new(10.25f, 20.25f), .35f, true));
        Assert.Equal(4, held.Scale);
        Assert.Equal(new Vector2(960, 568), held.ToScreen(new(10.25f, 20.25f)));
        Assert.Equal(MathF.Round(held.Origin.X), held.Origin.X);
        Assert.Equal(MathF.Round(held.Origin.Y), held.Origin.Y);
    }
}
