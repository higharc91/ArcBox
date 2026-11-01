using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using Xunit;
using static Tactics.Presentation.Tests.World.StageScript;

namespace Tactics.Presentation.Tests.World;

/// <summary>What the scene decides for a fight in the world, in pure C#: the walk's seed, the layout, the frame and the block repaints (5c review 2).</summary>
public sealed class StageSceneTests
{
    private static StageDirector WonAndFallingIn(StageScript script, ExploreView after)
    {
        var director = script.Director();
        director.Receive(Walking());
        director.Receive(Fight(Battle()), script.Walkers(new Tile(12, 12), new Tile(12, 13)));
        Run(director, StageTuning.FormingMost + 0.1);
        director.Receive(Results(Battle(lastSeq: 9, units: Units(hero1: new Tile(3, 2), hero2: new Tile(5, 1))), after));
        director.LastBlowPlayed();
        Run(director, 1);
        director.Receive(Walking(after));
        return director;
    }

    // 5c review 2, item 1: the followers were put back at an old fall-in's tiles on any later epoch, a trip to another region included.
    [Fact]
    public void The_seed_goes_once_with_its_own_epoch_and_never_after_a_trip()
    {
        var script = new StageScript();
        var before = Explore();
        var after = Explore(epoch: 2, at: new Tile(20, 13), arrived: "fellIn");
        var director = WonAndFallingIn(script, after);

        var first = director.NextWalk(before, StageScript.Region);
        Assert.Same(after, first!.View);
        Assert.True(first.Seed!.ContainsKey("p2"));
        Assert.Null(director.NextWalk(after, StageScript.Region));      // nothing new
        Run(director, 1);
        Assert.Equal(StageStep.Walking, director.Stage);
        Assert.Null(director.Walkers);

        // Off to another region, then back: new epochs, and no seed for either.
        var away = Explore(epoch: 3) with { Region = "elsewhere" };
        director.Receive(Walking(away));
        var there = director.NextWalk(after, "elsewhere");
        Assert.Same(away, there!.View);
        Assert.Null(there.Seed);
        var back = Explore(epoch: 4);
        director.Receive(Walking(back));
        Assert.Null(director.NextWalk(away, StageScript.Region)!.Seed);
        Assert.Empty(director.Seed);
    }

    [Fact]
    public void A_walk_that_never_saw_the_fall_in_epoch_gets_no_seed_for_a_later_one()
    {
        var script = new StageScript();
        var director = WonAndFallingIn(script, Explore(epoch: 2, at: new Tile(20, 13), arrived: "fellIn"));
        Run(director, 1);

        var later = Explore(epoch: 3, at: new Tile(5, 5));
        director.Receive(Walking(later));

        var next = director.NextWalk(Explore(), StageScript.Region);
        Assert.Same(later, next!.View);
        Assert.Null(next.Seed);
        Assert.Null(director.NextWalk(Explore(), "elsewhere"));        // the world drawn is another region's
    }

    // 5c review 2, item 2: a screen shown again when the world took a fight over fed the player its older battle, which rewound the board.
    [Fact]
    public void A_screen_shown_again_for_the_world_doesnt_feed_the_board()
    {
        var script = new StageScript();
        var director = script.Director();
        var fight = Fight(Battle(lastSeq: 5));
        director.Receive(fight);
        var screen = PhaseScreen.Of(fight, director);

        Assert.True(StageLayout.Of(screen, walking: true, worldBuilt: true, playerShows: true).FeedBoard);
        Assert.False(StageLayout.Of(screen, walking: true, worldBuilt: true, playerShows: true, again: true).FeedBoard);
        var plain = PhaseScreen.Of(Fight(Battle(lastSeq: 5, staged: false)));
        Assert.False(StageLayout.Of(plain, walking: false, worldBuilt: false, playerShows: true, again: true).FeedBoard);
        Assert.True(StageLayout.Of(plain, walking: false, worldBuilt: false, playerShows: true).PlainBoard);
    }

    [Fact]
    public void The_layout_puts_the_board_on_the_world_only_when_it_can()
    {
        var script = new StageScript();
        var director = script.Director();
        var fight = Fight(Battle(lastSeq: 5));
        director.Receive(fight);
        var screen = PhaseScreen.Of(fight, director);

        var inPlace = StageLayout.Of(screen, walking: true, worldBuilt: true, playerShows: false);
        Assert.Equal(new StageLayout(true, false, true, true, true), inPlace);
        Assert.False(StageLayout.Of(screen, walking: false, worldBuilt: true, playerShows: false).InPlace);
        Assert.False(StageLayout.Of(screen, walking: true, worldBuilt: false, playerShows: false).InPlace);

        var results = Results(Battle(lastSeq: 9), Explore(epoch: 2, arrived: "fellIn"));
        director.Receive(results);
        var over = PhaseScreen.Of(results, director);
        Assert.Equal(new StageLayout(true, false, false, false, false), StageLayout.Of(over, true, true, playerShows: true));
        Assert.False(StageLayout.Of(over, true, true, playerShows: false).InPlace);      // nothing left to draw
    }

    [Fact]
    public void What_the_director_is_told_of_the_world()
    {
        Assert.Equal(WorldState.Ready, StageLayout.WorldOf(new WorldSeen(true, true, true, false, false)));
        Assert.Equal(WorldState.Loading, StageLayout.WorldOf(new WorldSeen(true, true, false, false, false)));
        Assert.Equal(WorldState.Loading, StageLayout.WorldOf(new WorldSeen(true, false, true, false, false)));
        Assert.Equal(WorldState.Failed, StageLayout.WorldOf(new WorldSeen(true, false, false, true, false)));
        Assert.Equal(WorldState.Failed, StageLayout.WorldOf(new WorldSeen(false, false, false, false, true)));
        Assert.Equal(WorldState.Loading, StageLayout.WorldOf(new WorldSeen(false, false, false, false, false)));
        Assert.Equal(WorldState.Ready, StageLayout.WorldOf(new WorldSeen(true, true, true, false, true)));     // a new world after one was given up
    }

    [Fact]
    public void A_stage_frame_puts_the_walk_in_over_the_beat_and_the_shake_on_the_hud_only()
    {
        var script = new StageScript();
        var director = script.Director();
        director.Receive(Fight(Battle()), script.Walkers(new Tile(12, 12), new Tile(12, 13)));
        Run(director, 0.3);
        var mine = new UnitPlace(new Vector2(6.5f, 4.5f), 0);
        var beat = new BeatFrame(ImmutableDictionary<UnitId, UnitPlace>.Empty.Add(new UnitId("enemy2"), mine).Add(new UnitId("hero1"), mine),
            ImmutableDictionary<UnitId, UnitPose>.Empty, [], Vector2.Zero);
        var camera = new BoardFit(new Vector2(100, 50), 2);

        var frame = StageFrame.Of(beat, director, script.Stage, camera, new Vector2(3, -1));

        Assert.Equal(director.Places[new UnitId("hero1")], frame.Places[new UnitId("hero1")]);    // the walk-in has the hero
        Assert.Equal(mine, frame.Places[new UnitId("enemy2")]);
        Assert.Contains(new UnitId("enemy1"), frame.Hidden);
        Assert.Equal(BoardFit.Stage(script.Stage, camera.Origin, 2), frame.HudFit);
        Assert.Equal(BoardFit.Stage(script.Stage, camera.Origin - new Vector2(6, -2), 2), frame.Fit);
    }

    // 5c review 2, item 3: a repaint asked for while one ran replaced the one waiting, so a heal could be lost.
    [Fact]
    public void Waiting_repaints_merge_their_tiles_on_the_newest_ground()
    {
        var script = new StageScript();
        var plan = StagePlan.Build(script.Ground, script.Stage, null, Board.Pack.Art);
        var raised = BoardChanges.None with { Heights = [new HeightChange(new Tile(1, 1), 2)] };
        var queue = new RepaintQueue();
        Assert.Null(queue.Take());

        queue.Ask(plan, [new Tile(17, 11)], raised);
        queue.Ask(plan, [new Tile(20, 13)], null);      // the heal, behind it

        var job = queue.Take()!;
        Assert.Equal([new Tile(17, 11), new Tile(20, 13)], job.Tiles);
        Assert.Null(job.Changes);
        Assert.False(queue.Waiting);
        Assert.Null(queue.Take());
    }
}
