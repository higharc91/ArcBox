using System.Numerics;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.Tests.Board;

public sealed class CinematicFitTests
{
    [Fact]
    public void Opening_an_area_spotlight_keeps_the_caster_bright_then_includes_its_entire_target_area()
    {
        var caster = new Vector2(100, 100);
        var target = new Vector2(300, 200);
        var plan = new BattleCutIn([new(CutInTier.Full, 1, caster, target, 1, new(64, 32))], BattleCinematics.Full);
        var closeup = plan.At(.4);
        Assert.True(CinematicFit.Focuses(closeup, caster + new Vector2(0, 12), 6));
        Assert.False(CinematicFit.Focuses(closeup, target, 6));
        var impact = plan.At(plan.Lead + .6);
        Assert.True(CinematicFit.Focuses(impact, target + new Vector2(63, 31), 3));
        Assert.False(CinematicFit.Focuses(impact, caster, 3));
    }

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
