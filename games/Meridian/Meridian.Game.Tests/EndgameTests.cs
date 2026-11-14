namespace Meridian.Game.Tests;

public sealed class EndgameTests
{
    private static Harness AtBriefing(string scenario, int players = 4, int seed = 1)
    {
        var game = new Harness(players, seed, scenario).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Briefing);
        Assert.Equal(SessionPhase.Briefing, game.Session.Phase);
        return game;
    }

    private static Harness InEndgame(string scenario, int players = 4, int seed = 1)
    {
        var game = AtBriefing(scenario, players, seed);
        for (var i = 0; i < players; i++)
        {
            game.Send(i, "ready");
        }
        while (!game.Session.Queue.IsEmpty)
        {
            game.Advance(game.Session.Queue[0].Seconds);
        }
        return game;
    }

    [Fact]
    public void The_breach_is_shown_then_everyone_reads_a_briefing()
    {
        var game = AtBriefing(ThePassenger.Id);
        Assert.Equal(Phase.Breach, game.State.Phase);
        Assert.Equal(ClockPurpose.Briefing, game.Session.Clock.Purpose);
        Assert.Equal(game.H.Now.AddSeconds(90), game.Game.Deadline);
    }

    [Fact]
    public void The_endgame_begins_when_everyone_is_ready()
    {
        var game = AtBriefing(ThePassenger.Id);
        for (var i = 0; i < 3; i++)
        {
            game.Send(i, "ready");
            game.Send(i, "ready");   // twice is once
        }
        Assert.Equal(SessionPhase.Briefing, game.Session.Phase);
        game.Send(3, "ready");
        Assert.Equal(SessionPhase.Endgame, game.Session.Phase);
        Assert.Equal(Phase.Endgame, game.State.Phase);
    }

    [Fact]
    public void The_endgame_begins_anyway_after_ninety_seconds()
    {
        var game = AtBriefing(ThePassenger.Id);
        game.Send(0, "ready");
        game.Advance(90);
        Assert.Equal(SessionPhase.Endgame, game.Session.Phase);
    }

    [Fact]
    public void The_pause_is_the_same_whoever_drops()
    {
        // M3 review focus 8: in a hidden-traitor game, the mimic and an innocent get exactly the same pause.
        foreach (var seed in Enumerable.Range(1, 4))
        {
            var game = InEndgame(OneOfUsIsnt.Id, seed: seed);
            var actor = game.Actor;
            game.H.SetConnected(actor, false);
            Assert.Equal(ClockPurpose.Pause, game.Session.Clock.Purpose);
            Assert.Equal(game.H.Now.AddSeconds(60), game.Game.Deadline);
            game.Advance(60);
            Assert.True(game.Session.Seats[actor].Auto);
            Assert.True(game.Session.Seats[actor].PauseUsed);
        }
    }

    [Fact]
    public void A_reconnection_earns_a_fresh_pause_next_time()
    {
        var game = InEndgame(ThePassenger.Id);
        var actor = game.Actor;
        game.H.SetConnected(actor, false);
        game.Advance(60);
        game.H.SetConnected(actor, true);
        Assert.False(game.Session.Seats[actor].PauseUsed);
        Assert.False(game.Session.Seats[actor].Auto);
    }

    [Fact]
    public void A_disconnected_voter_is_cast_for_by_the_bot_after_the_pause()
    {
        // M3 review focus 5.
        var game = InEndgame(OneOfUsIsnt.Id);
        var seats = game.State.Crew.Select(c => c.Seat).ToEqArray();
        game.Game.Load(game.Session with
        {
            Game = game.State with { Waiting = new VotePending(seats[0], "mimic", seats, seats.Select(s => s.Value).ToEqArray()) },
        });
        game.H.SetConnected(1, false);
        Assert.Equal(ClockPurpose.Pause, game.Session.Clock.Purpose);
        for (var i = 0; i < 4; i++)
        {
            if (i != 1)
            {
                game.Send(i, "vote", new { choice = seats[1].Value });
            }
        }
        Assert.Equal([seats[1]], ((VotePending)game.State.Waiting!).Owed);
        game.Advance(60);
        game.Advance(0.8);
        Assert.IsNotType<VotePending>(game.State.Waiting);
    }

    [Fact]
    public void A_vote_deadline_makes_the_rest_abstain()
    {
        var game = InEndgame(OneOfUsIsnt.Id);
        var seats = game.State.Crew.Select(c => c.Seat).ToEqArray();
        game.Game.Load(game.Session with
        {
            Game = game.State with { Waiting = new VotePending(seats[0], "mimic", seats, seats.Select(s => s.Value).ToEqArray()) },
        });
        game.Send(0, "vote", new { choice = seats[2].Value });
        game.Advance(30);
        Assert.IsNotType<VotePending>(game.State.Waiting);
    }
}
