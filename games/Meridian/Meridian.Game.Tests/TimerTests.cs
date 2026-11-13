namespace Meridian.Game.Tests;

public sealed class TimerTests
{
    private static DateTimeOffset? Due(Harness game) => game.Game.Deadline;

    [Fact]
    public void A_turn_has_sixty_seconds_of_inactivity()
    {
        var game = new Harness().Board();
        Assert.Equal(game.H.Now.AddSeconds(60), Due(game));
        var first = game.Actor;
        game.Advance(60);
        Assert.Equal((first + 1) % 3, game.Actor);
    }

    [Fact]
    public void Inactivity_restarts_on_every_accepted_command()
    {
        var game = new Harness().Board();
        game.Advance(40);
        game.Send(game.Actor, "moveTo", new { to = "c2" });
        Assert.Equal(game.H.Now.AddSeconds(60), Due(game));
    }

    [Fact]
    public void A_rejected_command_doesnt_restart_it()
    {
        var game = new Harness().Board();
        game.Advance(40);
        game.Send(game.Actor, "moveTo", new { to = "c99" });
        Assert.Equal(game.H.Now.AddSeconds(20), Due(game));
    }

    [Fact]
    public void Each_wait_has_its_deadline()
    {
        var game = new Harness().Board();
        var waits = new Dictionary<Type, double>();
        var rng = Rng.Seeded(3, 99);
        for (var i = 0; i < 2000 && game.Session.Phase == SessionPhase.Explore && waits.Count < 2; i++)
        {
            if (game.State.Waiting is { } w)
            {
                waits[w.GetType()] = (Due(game)!.Value - game.H.Now).TotalSeconds;
            }
            var seat = BotRunner.Actor(game.State);
            game.Do(ExploreBot.Instance.Choose(game.Pack, game.State, seat, Rules.Legal(game.Pack, game.State, seat), ref rng));
        }
        Assert.Equal(20, waits[typeof(RollPending)]);
        if (waits.TryGetValue(typeof(OrientPending), out var orient))
        {
            Assert.Equal(8, orient);
        }
    }

    [Fact]
    public void A_stale_timer_does_nothing()
    {
        // M2 review focus 1: the room may deliver a timer that fired just before a change.
        var game = new Harness().Board();
        var stale = Timing.Key(game.Session.Clock.Seq);
        game.Send(game.Actor, "moveTo", new { to = "c2" });
        var before = game.Session;
        game.Game.OnTimer(stale);
        Assert.Same(before, game.Session);
    }

    [Fact]
    public void The_clock_key_is_numbered()
    {
        Assert.True(Timing.TryParse("clock:42", out var seq));
        Assert.Equal(42, seq);
        Assert.False(Timing.TryParse("clock:", out _));
        Assert.False(Timing.TryParse("other:1", out _));
    }
}
