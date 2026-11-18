using System.Text.Json;

namespace Meridian.Game.Tests;

/// <summary>Resuming after a server restart (M6 plan, Task 4): waiting for phones, then playing on.</summary>
public sealed class ResumeTests
{
    private static string Json(object value) => JsonSerializer.Serialize(value, ArcJson.Options);
    private static MeridianHostView Tv(Harness game) => (MeridianHostView)game.Game.HostView();

    /// <summary>A game played until <paramref name="stop"/>, its snapshot taken at the last moment that offered one.</summary>
    private static (Harness Original, JsonElement Snapshot) PlayAndSnapshot(string scenario, int players, Func<Harness, bool> stop, int seed = 2)
    {
        var game = new Harness(players, seed, scenario).Board();
        JsonElement? snapshot = game.Game.TakeSnapshot();
        game.PlayUntil(() =>
        {
            snapshot = game.Game.TakeSnapshot() ?? snapshot;
            return stop(game);
        });
        return (game, snapshot!.Value);
    }

    private static void ConnectAll(Harness game, int players)
    {
        for (var p = 0; p < players; p++)
        {
            game.H.SetConnected(p, true);
        }
    }

    [Fact]
    public void A_resumed_game_waits_for_phones_and_nothing_else_moves()
    {
        var (_, snapshot) = PlayAndSnapshot(ThePassenger.Id, 4, g => g.Session.Phase == SessionPhase.Endgame && g.Session.Queue.IsEmpty);
        var game = Harness.Restored(snapshot, 4, seed: 2, ThePassenger.Id);
        Assert.True(game.Session.Resuming);
        Assert.Equal(ClockPurpose.Resume, game.Session.Clock.Purpose);
        Assert.Equal("Reconnecting…", Tv(game).Banner);
        Assert.Equal(("resume", 0, 60), (Tv(game).Wait!.Kind, Tv(game).Wait!.Back!.Value, Tv(game).Wait!.Seconds));

        var before = Json(game.State);
        game.Advance(30);
        Assert.Equal(before, Json(game.State));                         // no bot played, no deadline ran
        Assert.All(game.Session.Seats, s => Assert.False(s.Auto));

        game.H.SetConnected(0, true);
        game.H.SetConnected(2, true);
        Assert.Equal(2, Tv(game).Wait!.Back);                           // a count, never which
        Assert.Null(Tv(game).Wait!.Seat);
        game.H.SetConnected(1, true);
        game.H.SetConnected(3, true);
        Assert.False(game.Session.Resuming);                            // everyone back: play on
        Assert.NotEqual(ClockPurpose.Resume, game.Session.Clock.Purpose);
        Assert.NotEqual("Reconnecting…", Tv(game).Banner);
    }

    [Fact]
    public void Nobody_coming_back_still_ends_the_game_with_bots()
    {
        // Review focus 6.
        var (_, snapshot) = PlayAndSnapshot(DecayingOrbit.Id, 3, g => g.Session.Phase == SessionPhase.Endgame && g.Session.Queue.IsEmpty);
        var game = Harness.Restored(snapshot, 3, seed: 2, DecayingOrbit.Id);
        for (var i = 0; i < 20000 && game.Session.Phase != SessionPhase.Over; i++)
        {
            var due = game.Game.Deadline ?? game.H.Now.AddSeconds(1);
            game.Advance(Math.Max(0.05, (due - game.H.Now).TotalSeconds));
        }
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
    }

    [Fact]
    public void A_seat_a_bot_took_over_is_taken_back_by_its_phone()
    {
        // Review focus 7.
        var (_, snapshot) = PlayAndSnapshot(ThePassenger.Id, 4, g => g.Session.Phase == SessionPhase.Endgame && g.Session.Queue.IsEmpty);
        var game = Harness.Restored(snapshot, 4, seed: 2, ThePassenger.Id);
        game.H.SetConnected(0, true);
        game.H.SetConnected(1, true);
        game.H.SetConnected(2, true);
        game.Advance(60);                                               // seat 3 never came back
        Assert.False(game.Session.Resuming);
        for (var i = 0; i < 400 && !game.Session.Seats[3].Auto; i++)
        {
            game.PlayUntil(() => game.Session.Seats[3].Auto, steps: 50);
            game.Advance(5);
        }
        Assert.True(game.Session.Seats[3].Auto);
        game.H.SetConnected(3, true);
        Assert.False(game.Session.Seats[3].Auto);
    }

    [Fact]
    public void A_restart_mid_vote_resumes_from_the_turns_start_with_no_ballot_owed()
    {
        // Review focus 4: the vote was under way when the server stopped; the snapshot is from the turn's start.
        // Not every game calls a vote: take the first seed whose game does.
        var seed = Enumerable.Range(1, 20).First(s => PlayAndSnapshot(OneOfUsIsnt.Id, 4, g => g.State.Waiting is VotePending, s)
                                                       .Original.State.Waiting is VotePending);
        var (_, snapshot) = PlayAndSnapshot(OneOfUsIsnt.Id, 4, g => g.State.Waiting is VotePending, seed);
        var game = Harness.Restored(snapshot, 4, seed, OneOfUsIsnt.Id);
        Assert.IsNotType<VotePending>(game.State.Waiting);
        ConnectAll(game, 4);
        Assert.All(Enumerable.Range(0, 4), p => Assert.NotEqual("vote", game.Screen(p)));
        game.PlayBots();
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
    }

    [Fact]
    public void A_restart_mid_briefing_resumes_the_briefing()
    {
        // Stopped as the briefing begins; the snapshot is that phase change's.
        var (original, snapshot) = PlayAndSnapshot(TillerSaysNo.Id, 3, g => g.Session.Phase == SessionPhase.Briefing);
        original.Send(0, "ready");   // one phone read its briefing, then the server stopped
        Assert.Single(original.Session.Ready);
        var game = Harness.Restored(snapshot, 3, seed: 2, TillerSaysNo.Id);
        Assert.Equal(SessionPhase.Briefing, game.Session.Phase);
        ConnectAll(game, 3);
        Assert.Equal(ClockPurpose.Briefing, game.Session.Clock.Purpose);
        Assert.All(Enumerable.Range(0, 3), p => Assert.Equal("briefing", game.Screen(p)));
        game.PlayBots();
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
    }

    [Fact]
    public void A_restart_mid_presentation_presents_again_after_resuming()
    {
        var (_, snapshot) = PlayAndSnapshot(SalvageRights.Id, 3, g => g.Session is { Phase: SessionPhase.Explore, Queue.IsEmpty: false }
                                                                       && g.State.Round >= 2);
        var game = Harness.Restored(snapshot, 3, seed: 2, SalvageRights.Id);
        ConnectAll(game, 3);
        if (!game.Session.Queue.IsEmpty)
        {
            Assert.Equal(ClockPurpose.Present, game.Session.Clock.Purpose);
            Assert.Equal(game.Session.Queue[0].Seq, Tv(game).Now!.Seq);
        }
        game.PlayBots();
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
    }

    public static TheoryData<string, int> Games()
    {
        var data = new TheoryData<string, int>();
        foreach (var scenario in AllScenarios.List)
        {
            for (var players = 3; players <= 6; players++)
            {
                data.Add(scenario, players);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Games))]
    public void The_first_views_after_a_restore_keep_every_secret(string scenario, int players)
    {
        // Review focus 5: the M3 secrecy sweep, straight after a restore, then after everyone is back.
        var (_, snapshot) = PlayAndSnapshot(scenario, players, g => g.Session.Phase == SessionPhase.Endgame && g.Session.Queue.IsEmpty);
        var game = Harness.Restored(snapshot, players, seed: 2, scenario);
        Assert.True(SecrecyTests.Check(game));
        ConnectAll(game, players);
        Assert.True(SecrecyTests.Check(game));
    }
}
