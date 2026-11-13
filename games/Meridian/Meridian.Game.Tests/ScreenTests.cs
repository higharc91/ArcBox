using System.Text;

namespace Meridian.Game.Tests;

public sealed class ScreenTests
{
    /// <summary>Plays bots through a whole game, calling <paramref name="check"/> on every phone's view after every step.</summary>
    private static void EveryView(Action<Harness, int, ControllerView> check, int players = 4, int seed = 1)
    {
        var game = new Harness(players, seed);
        void All() { for (var p = 0; p < players; p++) check(game, p, game.H.PlayerView(p)); }
        All();
        game.Board();
        var rng = Rng.Seeded((ulong)seed, 99);
        for (var i = 0; i < 3000 && game.Session.Phase == SessionPhase.Explore; i++)
        {
            All();
            if (!game.Session.Queue.IsEmpty)
            {
                game.Presented(game.Session.Queue[0].Seq);
                continue;
            }
            var seat = BotRunner.Actor(game.State);
            game.Do(ExploreBot.Instance.Choose(game.Pack, game.State, seat, Rules.Legal(game.Pack, game.State, seat), ref rng));
        }
        All();
    }

    [Fact]
    public void Every_screen_is_under_16_kb()
        => EveryView((_, _, view) =>
        {
            if (view is CustomView c)
            {
                var size = Encoding.UTF8.GetByteCount(c.Data.GetRawText());
                Assert.True(size < 16 * 1024, $"{c.Screen}: {size} bytes");
            }
        });

    [Fact]
    public void Turn_offers_exactly_what_Legal_allows()
        => EveryView((game, p, view) =>
        {
            if (view is not CustomView { Screen: "turn" })
            {
                return;
            }
            var me = game.Seat(p);
            var turn = game.Data<TurnScreen>(p);
            var offered = new List<Command>();
            offered.AddRange(turn.Reach.Select(r => new MoveTo(me, new CompartmentId(r.Id))));
            offered.AddRange(turn.Opens.Select(o => new Open(me, new CompartmentId(o.At), o.Side)));
            offered.AddRange(turn.Items.Where(i => i.Usable).Select(i => new UseItem(me, i.Id)));
            if (turn.Room is not null)
            {
                offered.Add(new UseCompartment(me));
            }
            offered.Add(new EndTurn(me));
            var legal = Rules.Legal(game.Pack, game.State, me).Where(c => c is not Meridian.Logic.Timeout);
            Assert.Equal(legal.ToHashSet(), offered.ToHashSet());
        });

    [Fact]
    public void Exactly_one_phone_has_a_decision_at_a_time()
        => EveryView((game, p, view) =>
        {
            if (p != 0 || game.Session.Phase != SessionPhase.Explore || !game.Session.Queue.IsEmpty)
            {
                return;
            }
            var deciding = Enumerable.Range(0, game.H.Players.Count).Count(i => game.Screen(i) is "turn" or "orient" or "roll" or "card" or "trade");
            Assert.Equal(1, deciding);
        });

    [Fact]
    public void Screens_follow_the_phase()
    {
        var game = new Harness();
        Assert.Equal("crewPick", game.Screen(0));
        game.Board();
        var actor = game.Actor;
        Assert.Equal("turn", game.Screen(actor));
        Assert.Equal("sheet", game.Screen((actor + 1) % 3));
        game.PlayBots();
        Assert.Equal("over", game.Screen(0));
    }
}
