using System.Text;

namespace Meridian.Game.Tests;

public sealed class ScreenTests
{
    /// <summary>Plays bots through a whole game, calling <paramref name="check"/> on every phone's view after every step.</summary>
    private static void EveryView(Action<Harness, int, ControllerView> check, int players = 4, int seed = 1, string? scenario = null)
    {
        var game = new Harness(players, seed, scenario);
        void All() { for (var p = 0; p < players; p++) check(game, p, game.H.PlayerView(p)); }
        All();
        game.Board();
        game.PlayUntil(() => { All(); return false; }, steps: 6000, seed: (ulong)seed);
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

    [Theory]
    [InlineData(null)]
    [InlineData(ThePassenger.Id)]
    [InlineData(OneOfUsIsnt.Id)]
    public void Turn_offers_exactly_what_Legal_allows(string? scenario)
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
            offered.AddRange(turn.Attacks.Select(a => new Attack(me, a.Id)));
            offered.AddRange(turn.Actions.Select(a => new ScenarioAct(me, a.Id, a.Data)));
            offered.AddRange(turn.Monsters.SelectMany(m => m.Moves.Select(to => (Command)new MonsterMove(me, m.Id, new CompartmentId(to)))
                .Concat(m.Targets.Select(t => new MonsterAttack(me, m.Id, new SeatId(t.Id))))));
            offered.Add(new EndTurn(me));
            var legal = Rules.Legal(game.Pack, game.State, me).Where(c => c is not Meridian.Logic.Timeout);
            Assert.Equal(legal.ToHashSet(), offered.ToHashSet());
        }, scenario: scenario);

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

public sealed class BriefingScreenTests
{
    [Fact]
    public void Every_role_gets_a_briefing_of_the_same_shape()
    {
        // A hidden-traitor game: the mimic's briefing screen must look exactly like everyone else's.
        var game = new Harness(4, 1, OneOfUsIsnt.Id).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Briefing);
        var screens = Enumerable.Range(0, 4).Select(p => (CustomView)game.H.PlayerView(p)).ToList();
        Assert.All(screens, s => Assert.Equal("briefing", s.Screen));
        var shapes = screens.Select(s => string.Join(",", s.Data.EnumerateObject().Select(o => o.Name))).Distinct();
        Assert.Single(shapes);
        Assert.All(screens.Select((_, p) => game.Data<BriefingScreen>(p).You),
                   b => Assert.InRange(b.Briefing.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 40, 120));
        Assert.Equal(game.H.Now.AddSeconds(90), screens[0].DeadlineUtc);
    }

    [Fact]
    public void A_phone_never_sees_another_seats_secret_role()
    {
        var game = new Harness(4, 1, OneOfUsIsnt.Id).Board();
        game.PlayUntil(() => game.Session.Phase == SessionPhase.Endgame);
        var mimic = game.State.Board!.Roles.Single(r => r.Role == OneOfUsIsnt.Mimic).Seat;
        for (var p = 0; p < 4; p++)
        {
            if (game.Seat(p) != mimic && game.H.PlayerView(p) is CustomView view)
            {
                // The word itself appears in the crew's own briefing ("name the mimic"); a mimic role must not.
                Assert.DoesNotContain("\"role\":\"mimic\"", view.Data.GetRawText());
            }
        }
    }
}
