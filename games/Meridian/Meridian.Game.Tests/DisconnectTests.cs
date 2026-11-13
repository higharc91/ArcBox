namespace Meridian.Game.Tests;

public sealed class DisconnectTests
{
    [Fact]
    public void A_dropped_active_seat_is_played_after_the_grace()
    {
        var game = new Harness().Board();
        var actor = game.Actor;
        game.H.SetConnected(actor, false);
        Assert.Equal(ClockPurpose.Grace, game.Session.Clock.Purpose);
        Assert.Equal(game.H.Now.AddSeconds(20), game.Game.Deadline);
        game.Advance(20);
        Assert.True(game.Session.Seats[actor].Auto);
        Assert.Equal(ClockPurpose.Bot, game.Session.Clock.Purpose);
        var moves = game.State.MovesLeft;
        game.Advance(0.8);
        Assert.True(game.State.MovesLeft < moves || game.Actor != actor || !game.Session.Queue.IsEmpty);
    }

    [Fact]
    public void Dropping_doesnt_reset_someone_elses_clock()
    {
        var game = new Harness().Board();
        game.Advance(30);
        game.H.SetConnected((game.Actor + 1) % 3, false);
        Assert.Equal(ClockPurpose.Deadline, game.Session.Clock.Purpose);
        Assert.Equal(game.H.Now.AddSeconds(30), game.Game.Deadline);
    }

    [Fact]
    public void A_dropped_seat_mid_wait_is_answered_by_the_bot()
    {
        // M2 review focus 3: get to a wait (a roll, a rotation or a choice), then drop its owner.
        var game = new Harness().Board();
        var rng = Rng.Seeded(2, 99);
        for (var i = 0; i < 2000 && game.State.Waiting is null && game.Session.Phase == SessionPhase.Explore; i++)
        {
            if (!game.Session.Queue.IsEmpty)
            {
                game.Presented(game.Session.Queue[0].Seq);
                continue;
            }
            var s = BotRunner.Actor(game.State);
            game.Do(ExploreBot.Instance.Choose(game.Pack, game.State, s, Rules.Legal(game.Pack, game.State, s), ref rng));
        }
        while (!game.Session.Queue.IsEmpty)
        {
            game.Presented(game.Session.Queue[0].Seq);
        }
        var wait = Assert.IsAssignableFrom<Pending>(game.State.Waiting);
        game.H.SetConnected(game.Index(wait.From), false);
        game.Advance(20.8);
        Assert.NotEqual(wait, game.State.Waiting);
    }

    [Fact]
    public void Reconnecting_takes_the_next_decision_back()
    {
        // M2 review focus 4.
        var game = new Harness().Board();
        var actor = game.Actor;
        game.H.SetConnected(actor, false);
        game.Advance(20);
        game.H.SetConnected(actor, true);
        Assert.False(game.Session.Seats[actor].Auto);
        if (game.Session.Queue.IsEmpty && game.Actor == actor)
        {
            Assert.Equal(ClockPurpose.Deadline, game.Session.Clock.Purpose);
            Assert.True(game.Game.Deadline >= game.H.Now.AddSeconds(8));
        }
    }

    [Fact]
    public void A_stale_bot_timer_does_nothing_after_the_player_returns()
    {
        var game = new Harness().Board();
        var actor = game.Actor;
        game.H.SetConnected(actor, false);
        game.Advance(20);
        var botTimer = Timing.Key(game.Session.Clock.Seq);
        game.H.SetConnected(actor, true);
        var before = game.Session;
        game.Game.OnTimer(botTimer);
        Assert.Same(before, game.Session);
    }

    [Fact]
    public void Everyone_dropped_still_reaches_the_breach()
    {
        // M2 review focus 5: no phones, no TV, only timers.
        var game = new Harness(players: 4, seed: 3).Board();
        for (var i = 0; i < 4; i++)
        {
            game.H.SetConnected(i, false);
        }
        for (var t = 0; t < 4 * 3600 && game.Session.Phase == SessionPhase.Explore; t++)
        {
            game.Advance(1);
        }
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
        Assert.All(game.Session.Seats, s => Assert.True(s.Auto));
    }

    [Fact]
    public void A_connected_seat_is_never_played_by_the_bot()
    {
        var game = new Harness().Board();
        for (var t = 0; t < 600 && game.Session.Phase == SessionPhase.Explore; t++)
        {
            game.Advance(1);
            Assert.NotEqual(ClockPurpose.Bot, game.Session.Clock.Purpose);
        }
        Assert.All(game.Session.Seats, s => Assert.False(s.Auto));
    }
}
