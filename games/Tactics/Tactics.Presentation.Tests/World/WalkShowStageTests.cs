using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;
using Xunit;
using static Tactics.Presentation.Tests.World.WalkFeed;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="WalkShow"/> with a fight in the world: the stage has the camera, hides the party, and lets it fall in.</summary>
public sealed class WalkShowStageTests
{
    private static readonly Lazy<UnitArt> Art = new(() => UnitArt.Load(Pack.Root));
    private static readonly BoardTerrain Flat = Ground(160, 120);
    private static readonly Tile Start = new(80, 60);

    private static WalkShow Show() => new(Flat, Art.Value, null);

    [Fact]
    public void The_stage_holds_the_camera_and_cuts_when_told()
    {
        var show = Show();
        show.Receive(new Script(Start).Now(false));
        var first = show.Frame(Frame);
        var target = new StageCamera(new Vector2(100, 70), 2);

        var glide = show.Frame(Frame, stage: new WalkStage(target, 0, null, true, Vector2.Zero));
        Assert.False(glide.Cut);
        Assert.NotEqual(first.Fit, glide.Fit);
        Assert.Empty(glide.Units);

        var cut = show.Frame(Frame, stage: new WalkStage(target, 1, null, true, Vector2.Zero));
        Assert.True(cut.Cut);
        Assert.NotEqual(glide.Fit, cut.Fit);
        var after = show.Frame(Frame, stage: new WalkStage(target, 1, null, true, Vector2.Zero));
        Assert.False(after.Cut);
        Assert.Equal(cut.Fit, after.Fit);       // it's there: holding doesn't move it
    }

    [Fact]
    public void A_shake_moves_the_whole_world()
    {
        var show = Show();
        show.Receive(new Script(Start).Now(false));
        var target = new StageCamera(new Vector2(80.5f, 60.5f), 0);
        var still = show.Frame(Frame, stage: new WalkStage(target, 1, null, true, Vector2.Zero));

        var shaken = show.Frame(0, stage: new WalkStage(target, 1, null, true, new Vector2(2, -1)));

        Assert.Equal(still.Fit.Origin + new Vector2(2, -1) * still.Fit.Scale, shaken.Fit.Origin);
    }

    [Fact]
    public void Falling_in_draws_the_walkers_where_the_stage_says_and_glides_across_the_new_epoch()
    {
        var show = Show();
        show.Receive(new Script(Start).Now(false));
        show.Frame(Frame);
        var falling = new Dictionary<string, UnitPlace> { ["b"] = new(new Vector2(83.5f, 61.5f), 0) };

        show.Receive(View(new Tile(84, 62), epoch: 2));
        var frame = show.Frame(Frame, stage: new WalkStage(null, 0, falling, false, Vector2.Zero));

        Assert.False(frame.Cut);       // the camera glides to the party falling in
        var b = Assert.Single(frame.Units, u => u.Id.Value == "b");
        Assert.Equal(WorldCamera.OnGround(falling["b"].Feet), b.Feet);
        Assert.Contains(frame.Units, u => u.Id.Value == "a");
    }

    [Fact]
    public void With_no_stage_the_walk_is_as_it_was()
    {
        var (plain, staged) = (Show(), Show());
        var script = new Script(Start);
        plain.Receive(script.Now(false));
        staged.Receive(script.Now(false));
        for (var i = 0; i < 30; i++)
        {
            var (a, b) = (plain.Frame(Frame), staged.Frame(Frame, stage: new WalkStage(null, 0, null, false, Vector2.Zero)));
            Assert.Equal(a.Fit, b.Fit);
            Assert.Equal(a.Units.Select(u => u.Feet), b.Units.Select(u => u.Feet));
        }
    }
}
