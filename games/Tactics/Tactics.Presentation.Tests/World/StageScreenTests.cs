using System.Collections.Immutable;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using Xunit;
using static Tactics.Presentation.Tests.World.StageScript;

namespace Tactics.Presentation.Tests.World;

/// <summary>
/// What the TV's screen does in a fight in the world, from the pure pieces the scene draws: <see cref="PhaseScreen.Of(TacticsHostView, StageDirector?)"/>
/// and <see cref="WorldCover.Look"/>, fed one session's views through a <see cref="StageDirector"/>, as <c>TacticsScene</c> feeds them.
/// </summary>
public sealed class StageScreenTests
{
    private static readonly ImmutableArray<ResultLine> Lines = [new("Ana", 120, []), new("Bo", 120, ["Bruised"])];

    /// <summary>The cover of a world that has opened, for the screen as the scene reads it.</summary>
    private static CoverLook Look(PhaseScreen screen)
    {
        var cover = new WorldCover();
        cover.Frame(1, WorldStage.Open, 1);
        var walking = screen.World is not null;
        return cover.Look(walking, screen.Board is not null || screen.InPlace, screen.OverWorld);
    }

    private static (StageDirector Director, TacticsHostView Results) ToResults(StageScript script, ExploreView after)
    {
        var director = script.Director();
        director.Receive(Walking());
        director.Receive(Fight(Battle()), script.Walkers(new Tile(12, 12), new Tile(12, 13)));
        Run(director, StageTuning.FormingMost + 0.1);
        var results = Results(Battle(lastSeq: 9), after) with { Results = Lines };
        director.Receive(results);
        director.LastBlowPlayed();
        Run(director, StageTuning.GridOut + 0.05);
        return (director, results);
    }

    // Review of 5c, finding 1: the results of a fight in the world lost their header and table, and the walk snapped to the new epoch.
    [Fact]
    public void Results_in_the_world_keep_their_header_and_table_and_the_old_epoch()
    {
        var script = new StageScript();
        var (director, results) = ToResults(script, Explore(epoch: 2, at: new Tile(20, 13), arrived: "fellIn"));

        var screen = PhaseScreen.Of(results, director);
        var look = Look(screen);

        Assert.True(screen.OverWorld);
        Assert.True(screen.InPlace);
        Assert.Equal(2, screen.Rows.Length);
        Assert.Equal("Results · Battle won", screen.Meta);
        Assert.True(look.HeaderAndTable);
        Assert.Equal(1, look.Card);
        Assert.False(look.Backdrop);
        Assert.False(look.PanelsShown);
        Assert.Equal(1, screen.World!.Epoch);       // the walk still has the party where the fight was

        var walking = Walking(Explore(epoch: 2, at: new Tile(20, 13), arrived: "fellIn"));
        director.Receive(walking);
        var after = PhaseScreen.Of(walking, director);
        Assert.Equal(StageStep.FallingIn, director.Stage);
        Assert.Equal(2, after.World!.Epoch);
        Assert.False(after.InPlace);
    }

    [Fact]
    public void A_loss_holds_the_old_epoch_until_the_dark()
    {
        var script = new StageScript();
        var woke = Explore(epoch: 2, at: new Tile(3, 3), arrived: "woke");
        var (director, _) = ToResults(script, woke);

        director.Receive(Walking(woke));
        Assert.Equal(1, PhaseScreen.Of(Walking(woke), director).World!.Epoch);
        Assert.True(PhaseScreen.Of(Walking(woke), director).InPlace);
        Run(director, StageTuning.FadeDown + 0.02);
        Assert.Equal(2, PhaseScreen.Of(Walking(woke), director).World!.Epoch);
        Assert.False(PhaseScreen.Of(Walking(woke), director).InPlace);
    }

    // The board in the world has the screen as a fight does: the world's panels would cover the battle's HUD.
    [Fact]
    public void A_fight_in_the_world_has_no_header_table_or_world_panels()
    {
        var script = new StageScript();
        var director = script.Director();
        var fight = Fight(Battle(lastSeq: 5));
        director.Receive(fight);

        var screen = PhaseScreen.Of(fight, director);
        var look = Look(screen);

        Assert.True(screen.InPlace);
        Assert.False(screen.OverWorld);
        Assert.NotNull(screen.Board);
        Assert.NotNull(screen.World);
        Assert.False(look.HeaderAndTable);
        Assert.False(look.PanelsShown);
        Assert.False(look.Backdrop);
        Assert.StartsWith("tactics-battle-stage_map", screen.Shot, StringComparison.Ordinal);
    }

    [Fact]
    public void While_the_world_loads_the_plain_board_hides_it()
    {
        var script = new StageScript();
        var director = script.Director(WorldState.Loading);
        var fight = Fight(Battle(lastSeq: 5));
        director.Receive(fight);

        var screen = PhaseScreen.Of(fight, director);
        var cover = new WorldCover();
        cover.Frame(0.1, WorldStage.Building, 0);
        var look = cover.Look(screen.World is not null, true, screen.OverWorld);

        Assert.False(screen.InPlace);
        Assert.NotNull(screen.World);     // it goes on loading behind the board
        Assert.True(look.Backdrop);
        Assert.Equal(1, look.Card);
        Assert.False(look.HeaderAndTable);
        Assert.False(look.PanelsShown);
    }

    // Review of 5c, finding 6: a stamped fight whose map the TV can't find is a plain one, on the screen as on the stage.
    [Fact]
    public void A_fight_whose_map_is_missing_is_plain_on_the_screen_too()
    {
        var script = new StageScript();
        var director = new StageDirector(_ => null);
        director.SetWorld(WorldState.Ready, script.Terrain);
        var fight = Fight(Battle(lastSeq: 5));
        director.Receive(fight);

        var screen = PhaseScreen.Of(fight, director);

        Assert.Equal(StageStep.Off, director.Stage);
        Assert.False(director.InWorld);
        Assert.False(screen.InPlace);
        Assert.Null(screen.World);
        var results = Results(Battle(lastSeq: 9), Explore(epoch: 2, arrived: "fellIn"));
        director.Receive(results);
        Assert.False(PhaseScreen.Of(results, director).OverWorld);
        Assert.Null(PhaseScreen.Of(results, director).World);
    }

    [Fact]
    public void A_plain_fight_and_its_results_are_as_before()
    {
        var script = new StageScript();
        var director = script.Director();
        director.Receive(Walking());
        var fight = Fight(Battle(staged: false));
        director.Receive(fight);
        var results = Results(Battle(lastSeq: 9, staged: false), Explore()) with { Results = Lines };

        static PhaseScreen Rowless(PhaseScreen screen) => screen with { Rows = [] };
        Assert.Equal(Rowless(PhaseScreen.Of(fight)), Rowless(PhaseScreen.Of(fight, director)));
        director.Receive(results);
        Assert.Equal(Rowless(PhaseScreen.Of(results)), Rowless(PhaseScreen.Of(results, director)));
        Assert.Equal(PhaseScreen.Of(results).Rows, PhaseScreen.Of(results, director).Rows);
        Assert.False(PhaseScreen.Of(results, director).OverWorld);
    }

    [Fact]
    public void Walking_screens_follow_the_stage_walk()
    {
        var script = new StageScript();
        var director = script.Director();
        var walking = Walking();
        director.Receive(walking);

        var screen = PhaseScreen.Of(walking, director);
        Assert.Equal(walking.Explore, screen.World);
        Assert.False(screen.InPlace);
        Assert.Equal(PhaseScreen.Of(walking).Walk, screen.Walk);
    }

    [Fact]
    public void Results_while_resuming_go_over_the_world_too()
    {
        var script = new StageScript();
        var director = script.Director();
        var resuming = Fight(Battle(lastSeq: 7), phase: "Resuming");
        director.Receive(resuming);

        var screen = PhaseScreen.Of(resuming, director);
        Assert.True(screen.OverWorld);
        Assert.True(screen.InPlace);
        Assert.True(Look(screen).HeaderAndTable);
    }

    // The hardware check (local-windows-checks §5.7): at the results over the world a unit's bar poked out bright above the table, its Downed
    // counts showed through it, and the table ran about 200 px empty past its rows. The results come while the last blow still plays, so the
    // marks go with the table, before the stage itself is at its results; and the table is only as tall as its rows.
    [Fact]
    public void Results_over_the_world_hide_the_units_marks_and_fit_the_table_to_its_rows()
    {
        var script = new StageScript();
        var director = script.Director();
        director.Receive(Walking());
        director.Receive(Fight(Battle()), script.Walkers(new Tile(12, 12), new Tile(12, 13)));
        Run(director, StageTuning.FormingMost + 0.1);
        var results = Results(Battle(lastSeq: 9), Explore(epoch: 2, at: new Tile(20, 13), arrived: "fellIn")) with { Results = Lines };
        director.Receive(results);
        var screen = PhaseScreen.Of(results, director);

        Assert.Equal(StageStep.Fighting, director.Stage);       // the last blow is still playing
        Assert.True(screen.OverWorld);
        Assert.True(MarkRule.Shown(director.Stage, covered: false));
        Assert.False(MarkRule.Shown(director.Stage, screen.OverWorld));
        director.LastBlowPlayed();
        var steps = new HashSet<StageStep>();
        Run(director, StageTuning.GridOut + 0.2, each: () =>
        {
            steps.Add(director.Stage);
            Assert.False(MarkRule.Shown(director.Stage, PhaseScreen.Of(results, director).OverWorld));
        });
        Assert.Equal([StageStep.Fading, StageStep.Results], steps.Order());

        Assert.False(screen.TableFills);
        Assert.Equal(0, screen.EmptySlots);
        var plain = PhaseScreen.Of(results);     // a fight on the plain board: its results have the screen to themselves
        Assert.True(plain.TableFills);
        Assert.Equal(PhaseScreen.TableSlots - Lines.Length, plain.EmptySlots);
    }
}
