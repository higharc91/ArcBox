using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using Xunit;
using static Tactics.Presentation.Tests.World.StageScript;

namespace Tactics.Presentation.Tests.World;

/// <summary><see cref="StageDirector"/>: into a fight in the world, through it and out of it, and a TV coming back at any moment.</summary>
public sealed class StageDirectorTests
{
    private static readonly UnitId H1 = new("hero1"), H2 = new("hero2"), E1 = new("enemy1");

    private static (StageScript Script, StageDirector Director) Started(Tile? p1 = null, Tile? p2 = null)
    {
        var script = new StageScript();
        var director = script.Director();
        director.Receive(Walking());
        director.Receive(Fight(Battle()), script.Walkers(p1 ?? new Tile(12, 12), p2 ?? new Tile(12, 13)));
        return (script, director);
    }

    /// <summary>Plays a started fight through its walk-in and its last blow, to the results with <paramref name="after"/> as the party.</summary>
    private static void ToResults(StageDirector director, ExploreView after, ImmutableArray<UnitView>? final = null)
    {
        Run(director, StageTuning.FormingMost + 0.1);
        director.Receive(Fight(Battle(lastSeq: 9)));
        director.Receive(Results(Battle(lastSeq: 10, units: final), after));
        director.LastBlowPlayed();
        Run(director, StageTuning.GridOut + 0.05);
    }

    [Fact]
    public void Heroes_walk_from_where_they_stand_to_their_slots()
    {
        var (_, director) = Started();

        Assert.Equal(StageStep.Forming, director.Stage);
        Assert.True(director.InPlace);
        Assert.False(director.WalkersShown);
        Assert.Equal(new Vector2(12.5f - Origin.X, 12.5f), director.Places[H1].Map + new Vector2(0, Origin.Y));
        Assert.Equal(BeatScriptWalk, director.Poses[H1].Anim);

        Run(director, director.Intro!.WalkEnd / 2);
        var halfway = director.Places[H1].Map.X;
        Assert.InRange(halfway, 12.5f - Origin.X + 1, Hero1.X + 0.5f - 1);

        Run(director, director.Intro.WalkEnd / 2 + 0.01);
        Assert.False(director.Places.ContainsKey(H1));     // on its slot: drawn on its own tile
        Assert.False(director.Poses.ContainsKey(H1));
        Assert.Equal(StageStep.Forming, director.Stage);
    }

    private const string BeatScriptWalk = "walk";     // BeatScript.WalkAnim, as the walk track poses it

    [Fact]
    public void The_walk_in_is_under_3_seconds()
    {
        var (_, director) = Started();
        var formUp = new TacticsTiming(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero).FormUp.TotalSeconds;
        var forming = 0.0;

        Run(director, 3, each: () => forming += director.Stage == StageStep.Forming ? 1 / 60.0 : 0);

        Assert.Equal(StageStep.Fighting, director.Stage);
        Assert.True(forming <= StageTuning.FormingMost + 1 / 60.0, $"{forming} s");
        Assert.True(StageTuning.FormingMost < formUp);
        Assert.Equal(2.0, director.Intro!.End, 6);     // 5 steps in 1.2 s, then three stepping in 0.2 s apart for 0.4 s
    }

    [Fact]
    public void A_far_party_fades_in_and_the_camera_cuts_in_the_dark()
    {
        var (_, director) = Started(p1: new Tile(12, 12), p2: new Tile(2, 13));

        Assert.True(director.Intro!.Fades);
        Assert.Equal(0, director.CameraCuts);
        Assert.Equal(0, director.Fade);
        Run(director, StageTuning.FadeDown - 0.02);
        Assert.Equal(0, director.CameraCuts);
        Assert.True(director.Fade > 0.9f);
        Run(director, 0.04);
        Assert.Equal(1, director.CameraCuts);
        Run(director, StageTuning.FadeUp);
        Assert.Equal(0, director.Fade, 3);
        Assert.DoesNotContain(director.Places.Keys, k => k == H1 || k == H2);
    }

    [Fact]
    public void Enemies_are_hidden_until_they_step_out()
    {
        var (_, director) = Started();
        var step = director.Intro!.Others.Single(o => o.Unit == E1);

        Assert.Contains(E1, director.Hidden);
        Run(director, step.Start + 0.01);
        Assert.DoesNotContain(E1, director.Hidden);
        var place = director.Places[E1];
        Assert.True(place.Map.X > Goblin1.X + 0.5f, "it comes from a tile out, away from the party");
        Run(director, StageTuning.StepInSeconds);
        Assert.False(director.Places.ContainsKey(E1));
        Assert.Contains(new UnitId("enemy2"), director.Intro.Others.Select(o => o.Unit));
    }

    [Fact]
    public void Beats_wait_for_the_walk_in()
    {
        var (_, director) = Started();
        var end = director.Intro!.End;
        var (ran, first) = (false, double.NaN);
        var clock = 0.0;

        Run(director, 3, each: () =>
        {
            clock += 1 / 60.0;
            if (director.BeatsRun && !ran)
                (ran, first) = (true, clock);
        });

        Assert.True(ran);
        Assert.InRange(first, end - 1e-6, end + 1 / 60.0 + 1e-6);
        Assert.True(director.BeatsRun);
    }

    [Fact]
    public void A_late_join_skips_the_walk_in()
    {
        var script = new StageScript();
        var late = script.Director();
        late.Receive(Fight(Battle(lastSeq: 3)));
        var opening = script.Director();
        opening.Receive(Fight(Battle(lastSeq: 2)), script.Walkers(new Tile(12, 12), new Tile(12, 13)));
        var resumed = script.Director();
        resumed.Receive(Fight(Battle(lastSeq: 1), phase: "Resuming"));

        Assert.Equal(StageStep.Fighting, late.Stage);
        Assert.True(late.BeatsRun);
        Assert.Equal(1, late.GridAlpha);
        Assert.Empty(late.Hidden);
        Assert.Equal(1, late.CameraCuts);
        Assert.NotNull(late.CameraTarget);
        Assert.Equal(StageStep.Forming, opening.Stage);
        Assert.Equal(StageStep.Fighting, resumed.Stage);
    }

    [Fact]
    public void The_camera_isnt_clamped_in_a_fight()
    {
        // A stamp at the region's corner: walking, the camera keeps its margin from the edge; in the fight it centres on the stage.
        var script = new StageScript();
        var director = script.Director();
        director.Receive(Fight(Battle(lastSeq: 5, origin: new Tile(0, 0))));
        var target = director.CameraTarget!.Value;
        Assert.Equal(new Vector2(4, 3), target.Map);

        var (held, followed) = (new WorldCamera(40, 30), new WorldCamera(40, 30));
        held.Snap(new Vector2(20, 15), 0);
        followed.Snap(new Vector2(20, 15), 0);
        for (var i = 0; i < 240; i++)
        {
            held.Hold(target.Map, target.Height, 1 / 60.0);
            followed.Follow(target.Map, target.Height, Vector2.Zero, 1 / 60.0);
        }

        Assert.True(Vector2.Distance(target.Map, held.Centre) < 0.05f, $"held at {held.Centre}");
        Assert.True(Vector2.Distance(target.Map, followed.Centre) > 2, $"followed to {followed.Centre}");
    }

    [Fact]
    public void The_last_blow_plays_before_the_grid_fades()
    {
        var (_, director) = Started();
        Run(director, StageTuning.FormingMost + 0.1);

        director.Receive(Results(Battle(lastSeq: 6), Explore(epoch: 2, arrived: "fellIn")));
        Assert.Equal(StageStep.Fighting, director.Stage);
        Assert.Equal(1, director.GridAlpha);
        Run(director, 1);
        Assert.Equal(StageStep.Fighting, director.Stage);       // the last blow is still playing

        director.LastBlowPlayed();
        Assert.Equal(StageStep.Fading, director.Stage);
        Run(director, StageTuning.GridOut / 2);
        Assert.InRange(director.GridAlpha, 0.4f, 0.6f);
        Run(director, StageTuning.GridOut / 2 + 0.02);
        Assert.Equal(StageStep.Results, director.Stage);
        Assert.Equal(0, director.GridAlpha);
    }

    [Fact]
    public void A_blow_played_before_the_results_doesnt_skip_them()
    {
        var (_, director) = Started();
        Run(director, StageTuning.FormingMost + 0.1);

        director.LastBlowPlayed();
        Assert.Equal(StageStep.Fighting, director.Stage);
    }

    [Fact]
    public void Results_keep_the_world_on_screen()
    {
        var (_, director) = Started();
        ToResults(director, Explore(epoch: 2, at: new Tile(17, 12), arrived: "fellIn"));

        Assert.Equal(StageStep.Results, director.Stage);
        Assert.True(director.InPlace);
        Assert.False(director.WalkersShown);
        Assert.NotNull(director.CameraTarget);

        var screen = PhaseScreen.Of(Results(Battle(lastSeq: 10), Explore(epoch: 2, at: new Tile(17, 12), arrived: "fellIn")));
        Assert.True(screen.InPlace);
        Assert.NotNull(screen.World);
        Assert.Equal("Results · Battle won", screen.Meta);
    }

    [Fact]
    public void The_epoch_waits_until_results_are_over()
    {
        var (_, director) = Started();
        Assert.Equal(1, director.Walk!.Epoch);
        var after = Explore(epoch: 2, at: new Tile(17, 12), arrived: "fellIn");

        Run(director, StageTuning.FormingMost + 0.1);
        director.Receive(Results(Battle(lastSeq: 10), after));
        Assert.Equal(1, director.Walk!.Epoch);
        director.LastBlowPlayed();
        Run(director, 2);
        Assert.Equal(StageStep.Results, director.Stage);
        Assert.Equal(1, director.Walk!.Epoch);

        director.Receive(Walking(after));
        Assert.Equal(2, director.Walk!.Epoch);
    }

    [Fact]
    public void A_win_falls_in_from_where_they_stood()
    {
        var (script, director) = Started();
        var final = Units(hero1: new Tile(3, 2), hero2: new Tile(5, 1));     // the leader comes from (19, 12) to the view's (20, 13)
        var after = Explore(epoch: 2, at: new Tile(20, 13), arrived: "fellIn");
        ToResults(director, after, final);

        director.Receive(Walking(after));

        Assert.Equal(StageStep.FallingIn, director.Stage);
        Assert.False(director.InPlace);
        Assert.True(director.WalkersShown);
        Assert.Null(director.CameraTarget);
        Assert.Equal(new Vector2(21.5f, 11.5f), director.Walkers!["p2"].Map);     // hero2's tile, in the world
        Assert.Equal(new Vector2(19.5f, 12.5f), director.Walkers["p1"].Map);
        Assert.Equal(new Vector2(21.5f, 11.5f), director.Seed["p2"].Map);
        Assert.False(director.Seed.ContainsKey("p1"));     // the leader is the view's own

        Run(director, StageTuning.FallIn / 2);
        Assert.Equal(StageStep.FallingIn, director.Stage);
        Run(director, StageTuning.FallIn / 2 + 0.02);
        Assert.Equal(StageStep.Walking, director.Stage);
        Assert.Null(director.Walkers);
        Assert.Equal(after, director.Walk);
        director.Receive(Walking(after));
        Assert.Equal(StageStep.Walking, director.Stage);
        var fall = FallIn.Plan(Battle(units: final), script.Stage, after, script.Terrain);
        Assert.Equal(new Vector2(20.5f, 13.5f), fall.At(StageTuning.FallIn)["p1"].Map);
        Assert.Equal(new Vector2(20f, 13f), fall.At(StageTuning.FallIn / 2)["p1"].Map);
        _ = script;
    }

    [Fact]
    public void A_loss_fades_to_the_recovery_spawn()
    {
        var (_, director) = Started();
        var woke = Explore(epoch: 2, at: new Tile(3, 3), arrived: "woke");
        ToResults(director, woke);
        var cuts = director.CameraCuts;

        director.Receive(Walking(woke));
        Assert.Equal(StageStep.Waking, director.Stage);
        Assert.Equal(1, director.Walk!.Epoch);
        Assert.True(director.InPlace);

        Run(director, StageTuning.FadeDown - 0.02);
        Assert.Equal(1, director.Walk!.Epoch);
        Assert.True(director.Fade > 0.9f);
        Run(director, 0.04);
        Assert.Equal(2, director.Walk!.Epoch);
        Assert.Equal(cuts + 1, director.CameraCuts);
        Assert.False(director.InPlace);
        Assert.True(director.WalkersShown);
        Assert.True(director.Fade > 0.9f);
        Run(director, StageTuning.FadeUp);
        Assert.Equal(StageStep.Walking, director.Stage);
        Assert.Equal(0, director.Fade);
        Assert.Empty(director.Seed);
    }

    [Fact]
    public void Followers_seeded_after_an_epoch_arent_stacked()
    {
        // hero2 ended on the leader's new tile, so it's put beside it; neither stands on the other.
        var script = new StageScript();
        var director = script.Director();
        director.Receive(Fight(Battle()), script.Walkers(new Tile(12, 12), new Tile(12, 13)));
        var after = Explore(epoch: 2, at: new Tile(19, 13), arrived: "fellIn");
        ToResults(director, after, Units(hero1: new Tile(3, 3), hero2: new Tile(3, 3)));
        director.Receive(Walking(after));

        var seed = director.Seed["p2"];
        Assert.NotEqual(new Vector2(19.5f, 13.5f), seed.Map);
        Assert.True(script.Terrain.Rules.Walkable(new Tile((int)seed.Map.X, (int)seed.Map.Y)));

        var track = new WalkTrack();
        track.Receive(director.Walk!);
        track.Seed(director.Seed);
        track.Advance(0.5);
        var places = track.Places(script.Terrain);
        Assert.NotEqual(Tiled(places["p1"].Map), Tiled(places["p2"].Map));
        Assert.Equal(seed.Map, places["p2"].Map);
    }

    private static Tile Tiled(Vector2 map) => new((int)MathF.Floor(map.X), (int)MathF.Floor(map.Y));

    [Fact]
    public void A_second_fight_on_the_same_map_starts_clean()
    {
        var (script, director) = Started();
        var woke = Explore(epoch: 2, at: new Tile(3, 3), arrived: "woke");
        ToResults(director, woke);
        director.Receive(Walking(woke));
        Run(director, 2);
        Assert.Equal(StageStep.Walking, director.Stage);

        director.Receive(Walking(Explore(epoch: 2, at: new Tile(12, 12), seq: 9)));
        director.Receive(Fight(Battle(), Explore(epoch: 2, at: new Tile(12, 12), seq: 9)), script.Walkers(new Tile(12, 12), new Tile(12, 13)));

        Assert.Equal(StageStep.Forming, director.Stage);
        Assert.Equal(0, director.GridAlpha);
        Assert.Contains(E1, director.Hidden);
        Assert.False(director.BeatsRun);
        Assert.Empty(director.Seed);
        Assert.Equal(2, director.Walk!.Epoch);
        Run(director, StageTuning.FormingMost + 0.1);
        director.LastBlowPlayed();       // a stale call from the last fight's results
        Assert.Equal(StageStep.Fighting, director.Stage);
    }

    [Fact]
    public void A_retry_straight_from_the_results_starts_clean()
    {
        var (script, director) = Started();
        ToResults(director, Explore(epoch: 2, at: new Tile(3, 3), arrived: "woke"));

        director.Receive(Fight(Battle(), Explore(epoch: 2, at: new Tile(3, 3))), script.Walkers(new Tile(12, 12), new Tile(12, 13)));

        Assert.Equal(StageStep.Forming, director.Stage);
    }

    [Fact]
    public void A_world_not_ready_means_the_plain_board()
    {
        var script = new StageScript();
        var director = script.Director(WorldState.Loading);
        director.Receive(Fight(Battle()), script.Walkers(new Tile(12, 12), new Tile(12, 13)));

        Assert.Equal(StageStep.Fighting, director.Stage);
        Assert.False(director.InPlace);
        Assert.True(director.BeatsRun);
        Assert.Equal(1, director.GridAlpha);
        Assert.Null(director.CameraTarget);

        // Another region's world is no better.
        var other = script.Director(WorldState.Loading);
        other.SetWorld(WorldState.Ready, new BoardTerrain(StageWorld.Map("elsewhere", 4, 4, (_, _) => StageWorld.Flat(0)), null));
        other.Receive(Fight(Battle()));
        Assert.False(other.InPlace);
    }

    [Fact]
    public void The_stage_takes_over_when_the_world_is_ready()
    {
        var script = new StageScript();
        var director = script.Director(WorldState.Loading);
        director.Receive(Fight(Battle()));

        director.SetWorld(WorldState.Ready, script.Terrain);

        Assert.Equal(StageStep.Fighting, director.Stage);
        Assert.True(director.InPlace);
        Assert.Equal(1, director.CameraCuts);
        Assert.Equal(1, director.Fade);
        Run(director, StageTuning.Takeover / 2);
        Assert.InRange(director.Fade, 0.4f, 0.6f);
        Run(director, StageTuning.Takeover / 2 + 0.02);
        Assert.Equal(0, director.Fade);
        Assert.True(director.BeatsRun);
    }

    [Fact]
    public void A_world_that_cant_be_drawn_leaves_the_plain_board()
    {
        var (_, director) = Started();
        Assert.Equal(StageStep.Forming, director.Stage);

        director.SetWorld(WorldState.Failed);

        Assert.Equal(StageStep.Fighting, director.Stage);
        Assert.False(director.InPlace);
        Assert.True(director.BeatsRun);
        director.SetWorld(WorldState.Failed);
        director.Receive(Fight(Battle(lastSeq: 4)));
        Assert.False(director.InPlace);
    }

    [Fact]
    public void Plain_battles_are_untouched()
    {
        var script = new StageScript();
        var director = script.Director();
        director.Receive(Walking());
        director.Receive(Fight(Battle(staged: false)));

        Assert.Equal(StageStep.Off, director.Stage);
        Assert.False(director.InPlace);
        Assert.True(director.BeatsRun);
        Assert.Equal(1, director.GridAlpha);
        Assert.Empty(director.Hidden);
        Assert.Empty(director.Places);
        Assert.Null(director.CameraTarget);
        Assert.Equal(0, director.Fade);
        Assert.Null(director.Where);

        director.Receive(Results(Battle(lastSeq: 9, staged: false), Explore()) with { LastBattle = Battle(lastSeq: 9, staged: false) });
        Assert.Equal(StageStep.Walking, director.Stage);
        Assert.False(PhaseScreen.Of(Fight(Battle(staged: false))).InPlace);
    }

    [Fact]
    public void A_reconnect_at_the_results_shows_the_world_where_the_party_is()
    {
        var script = new StageScript();
        var director = script.Director();
        var after = Explore(epoch: 2, at: new Tile(3, 3), arrived: "woke");

        director.Receive(Results(Battle(lastSeq: 30), after));

        Assert.Equal(StageStep.Results, director.Stage);
        Assert.False(director.InPlace);
        Assert.True(director.WalkersShown);
        Assert.Equal(after, director.Walk);
        director.Receive(Walking(after));
        Assert.Equal(StageStep.Walking, director.Stage);
    }

    [Fact]
    public void A_fight_called_off_mid_walk_in_falls_in()
    {
        var (_, director) = Started();
        Run(director, 0.5);

        director.Receive(Walking(Explore(epoch: 2, at: new Tile(12, 12), arrived: "fellIn")));

        Assert.Equal(StageStep.FallingIn, director.Stage);
        Assert.True(director.BeatsRun);
        Assert.Empty(director.Hidden);
    }

    // ---- Properties ----------------------------------------------------------------------------------------------------

    private static int Rank(StageStep step) => step switch
    {
        StageStep.Off => 0,
        StageStep.Forming => 1,
        StageStep.Fighting => 2,
        StageStep.Fading => 3,
        StageStep.Results => 4,
        StageStep.FallingIn or StageStep.Waking => 5,
        _ => 6,
    };

    /// <summary>One session's views, in order, with how long after the one before each comes: walking, a fight, its results and the walk after.</summary>
    private static List<(double Gap, TacticsHostView View)> Session(Random random, int fights)
    {
        var views = new List<(double, TacticsHostView)>();
        var epoch = 1;
        for (var fight = 0; fight < fights; fight++)
        {
            var before = Explore(epoch: epoch, at: new Tile(12, 12), seq: fight);
            views.Add((random.NextDouble() * 2, Walking(before)));
            var staged = random.Next(5) != 0;
            var beats = random.Next(1, 30);
            for (var seq = 1; seq <= beats; seq++)
                views.Add((seq == 1 ? 0.1 : random.NextDouble() * 1.5, Fight(Battle(lastSeq: seq, staged: staged), before, random.Next(25) == 0 ? "Resuming" : "Battle")));
            var lost = random.Next(2) == 0;
            var after = staged ? Explore(epoch: ++epoch, at: lost ? new Tile(3, 3) : new Tile(17, 12), arrived: lost ? "woke" : "fellIn") : before;
            views.Add((random.NextDouble(), Results(Battle(lastSeq: beats + 1, staged: staged), after, lost ? "Lost" : "Won")));
            views.Add((random.NextDouble() * 6, Walking(after)));
        }
        return views;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void The_director_never_runs_over_its_maxima_and_never_goes_back(int seed)
    {
        var random = new Random(seed);
        var script = new StageScript();
        var director = script.Director(random.Next(4) == 0 ? WorldState.Loading : WorldState.Ready);
        var (step, since, rank) = (director.Stage, 0.0, Rank(director.Stage));
        var most = new Dictionary<StageStep, double>
        {
            [StageStep.Forming] = StageTuning.FormingMost, [StageStep.Fading] = StageTuning.GridOut,
            [StageStep.FallingIn] = StageTuning.FallIn, [StageStep.Waking] = StageTuning.FadeDown + StageTuning.FadeUp,
        };
        void Check(bool newBattle)
        {
            Assert.InRange(director.Fade, 0, 1);
            Assert.InRange(director.GridAlpha, 0, 1);
            if (director.Stage != step)
                (step, since) = (director.Stage, 0);
            if (most.TryGetValue(step, out var limit))
                Assert.True(since <= limit + 1e-6, $"{step} for {since:F3} s, over {limit} s");
            var now = Rank(director.Stage);
            Assert.True(now >= rank || newBattle, $"went back from rank {rank} to {director.Stage}");
            rank = now;
        }

        foreach (var (gap, view) in Session(random, random.Next(1, 4)))
        {
            for (var left = gap; left > 1e-9;)
            {
                var frame = Math.Min(left, random.NextDouble() * 0.1);
                director.Tick(frame);
                (left, since) = (left - frame, since + frame);
                Check(false);
                if (random.Next(400) == 0)
                {
                    director.SetWorld(random.Next(3) == 0 ? WorldState.Failed : WorldState.Ready, script.Terrain);
                    Check(false);
                }
            }
            var was = director.Stage;
            director.Receive(view, script.Walkers(new Tile(random.Next(40), random.Next(30)), new Tile(12, 13)));
            var newBattle = view.Phase is "Battle" or "Resuming" && was is not (StageStep.Forming or StageStep.Fighting);
            Check(newBattle);
            if (view.Phase == "Results")
            {
                director.LastBlowPlayed();
                Check(false);
            }
        }
        Run(director, 3);
        Assert.Equal(StageStep.Walking, director.Stage);
        Assert.False(director.InPlace);
        Assert.Equal(0, director.Fade);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void A_reconnect_at_any_moment_lands_in_the_right_stage(int seed)
    {
        var random = new Random(seed);
        var script = new StageScript();
        var views = Session(random, 2);
        var at = random.Next(views.Count);
        var view = views[at].View;
        var director = script.Director();

        director.Receive(view);

        var staged = (view.Battle ?? view.LastBattle)?.Stage is not null;
        var expected = view.Phase switch
        {
            "Battle" when !staged => StageStep.Off,
            "Battle" => view.Battle!.Beats[^1].Seq > 2 ? StageStep.Fighting : StageStep.Forming,
            "Resuming" => staged ? StageStep.Fighting : StageStep.Off,
            "Results" => staged ? StageStep.Results : StageStep.Walking,
            _ => StageStep.Walking,
        };
        Assert.Equal(expected, director.Stage);
        Assert.Equal(view.Explore, director.Walk);
        Assert.Equal(staged && view.Phase is "Battle" or "Resuming", director.InPlace);
        if (director.Stage == StageStep.Fighting)
            Assert.Equal(1, director.GridAlpha);

        // From there the rest of the session plays to the walk after the last fight, with no beat held up past the walk-in.
        foreach (var (gap, next) in views.Skip(at + 1))
        {
            Run(director, gap, 0.05);
            director.Receive(next, script.Walkers(new Tile(12, 12), new Tile(12, 13)));
            if (next.Phase == "Results")
                director.LastBlowPlayed();
        }
        Run(director, 3);
        Assert.Equal(StageStep.Walking, director.Stage);
        Assert.Equal(views[^1].View.Explore, director.Walk);
    }

    public static TheoryData<int> Seeds => [.. Enumerable.Range(1, 80)];
}
