namespace Meridian.Game.Tests;

public sealed class PresentationTests
{
    private static Harness Presenting()
    {
        var game = new Harness().Board();
        Assert.True(game.PlayUntilPresenting());
        return game;
    }

    [Fact]
    public void Play_waits_while_the_tv_presents()
    {
        var game = Presenting();
        var front = game.Session.Queue[0];
        Assert.Equal(ClockPurpose.Present, game.Session.Clock.Purpose);
        Assert.Equal(game.H.Now + TimeSpan.FromSeconds(front.Seconds), game.Game.Deadline);
        var before = game.Session;
        game.Send(game.Actor, "endTurn");   // input is held
        Assert.Same(before, game.Session);
    }

    [Fact]
    public void The_signal_ends_the_wait_early()
    {
        var game = Presenting();
        var count = game.Session.Queue.Count;
        game.Presented(game.Session.Queue[0].Seq);
        Assert.Equal(count - 1, game.Session.Queue.Count);
    }

    [Fact]
    public void The_fallback_ends_it_without_a_tv()
    {
        // M2 review focus 6.
        var game = Presenting();
        var count = game.Session.Queue.Count;
        game.Advance(game.Session.Queue[0].Seconds);
        Assert.True(game.Session.Queue.Count < count);
    }

    [Fact]
    public void A_stale_signal_is_ignored()
    {
        // M2 review focus 7.
        var game = Presenting();
        var before = game.Session;
        game.Presented(game.Session.Queue[0].Seq + 100);
        game.Presented(game.Session.Queue[0].Seq - 1);
        game.H.HostSignal("somethingElse", new { seq = game.Session.Queue[0].Seq });
        Assert.Same(before, game.Session);
    }

    [Fact]
    public void Deadlines_start_after_the_queue_empties()
    {
        var game = Presenting();
        while (!game.Session.Queue.IsEmpty)
        {
            game.Presented(game.Session.Queue[0].Seq);
        }
        if (game.Session.Phase == SessionPhase.Explore)
        {
            Assert.Equal(ClockPurpose.Deadline, game.Session.Clock.Purpose);
            Assert.True(game.Game.Deadline > game.H.Now.AddSeconds(7));
        }
    }

    [Fact]
    public void Events_are_numbered_in_order()
    {
        var game = new Harness().Board();
        var seen = new List<int>();
        var rng = Rng.Seeded(1, 99);
        for (var i = 0; i < 300 && game.Session.Phase == SessionPhase.Explore; i++)
        {
            if (!game.Session.Queue.IsEmpty)
            {
                seen.Add(game.Session.Queue[0].Seq);
                game.Presented(game.Session.Queue[0].Seq);
                continue;
            }
            var seat = BotRunner.Actor(game.State);
            game.Do(ExploreBot.Instance.Choose(game.Pack, game.State, seat, Rules.Legal(game.Pack, game.State, seat), ref rng));
        }
        Assert.NotEmpty(seen);
        Assert.Equal(Enumerable.Range(1, seen.Count), seen);
    }

    [Fact]
    public void A_whole_game_reaches_the_breach_with_no_tv()
    {
        var game = new Harness(players: 5, seed: 9).Board();
        game.PlayBots(5000);
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
        Assert.NotNull(game.H.Results);
    }
}
