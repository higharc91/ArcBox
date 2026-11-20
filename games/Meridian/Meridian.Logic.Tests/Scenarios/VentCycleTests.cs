namespace Meridian.Logic.Tests.Scenarios;

public sealed class VentCycleTests
{
    private const string Id = VentCycle.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static IEnumerable<Token> Suits(GameState s) => s.Board!.Tokens.Where(t => t.Kind == VentCycle.Suit);
    private static CompartmentId Airlock(GameState s) => Primitives.FirstOf(s, "airlock")!.Value;

    [Fact]
    public void There_are_suits_for_all_but_one_scattered_far_from_the_airlock()
    {
        foreach (var players in new[] { 3, 6 })
        {
            var state = ScenarioRun.Begun(Id, players);
            Assert.Equal(players - 1, Suits(state).Count());
            var away = Primitives.Steps(state, Airlock(state));
            var farEnough = away.Count(x => x.Value >= 3) >= players - 1;
            Assert.All(Suits(state), s => Assert.True(!farEnough || away[s.At!.Value] >= 3, $"a suit {away[s.At!.Value]} steps from the Airlock"));
        }
    }

    [Fact]
    public void Suit_up_cycle_out_and_you_escape_with_your_suit()
    {
        var game = Begin();
        var seat = game.State.Crew[0].Seat;
        game.Put(seat, Suits(game.State).First().At!.Value);
        game.State = game.State with { Active = 0, Waiting = null, Turn = game.State.Turn with { Acted = false } };
        game.Do(new ScenarioAct(seat, "suitUp"));
        var suit = Suits(game.State).Single(s => s.Carrier == seat);

        game.Put(seat, Airlock(game.State));
        game.State = game.State with { Turn = game.State.Turn with { Acted = false } };
        game.Do(new ScenarioAct(seat, "cycleOut"));
        Assert.True(game.Crew(seat).Escaped);
        Assert.Equal(seat, Suits(game.State).Single(s => s.Id == suit.Id).Carrier);   // it left the ship with them
        Assert.Equal(1, game.State.Board!.Value("escaped"));
    }

    [Fact]
    public void The_decks_vent_in_order_and_vacuum_hurts_only_the_unsuited()
    {
        var game = Begin();
        for (var i = 0; i < Pack.ScenarioDef(Id).Number("cycle"); i++)
        {
            EndRound(game);
        }
        Assert.Equal(1, game.State.Board!.Value("vented"));
        Assert.Contains(game.Events, e => e is ScenarioNotice { Fx: "vent" });

        // Two crew on Engineering, one suited: only the other loses a step at the end of their turn.
        var engineering = game.State.Ship.Compartments.First(c => c.At.Deck == DeckId.Engineering).Id;
        var (bare, suited) = (game.State.Crew[0].Seat, game.State.Crew[1].Seat);
        game.Put(bare, engineering);
        game.Put(suited, engineering);
        game.State = game.State.WithBoard(game.State.Board!.WithToken(Suits(game.State).First() with { At = null, Carrier = suited }));
        var steps = (Bare: Total(game.Crew(bare)), Suited: Total(game.Crew(suited)));
        foreach (var seat in new[] { bare, suited })
        {
            game.State = game.State with { Active = game.State.IndexOf(seat), Waiting = null };
            game.Do(new EndTurn(seat));
        }
        Assert.Equal(steps.Bare - 1, Total(game.Crew(bare)));
        Assert.Equal(steps.Suited, Total(game.Crew(suited)));
    }

    [Fact]
    public void A_hit_takes_the_losers_suit()
    {
        var game = Begin();
        var (wearer, thief) = (game.State.Crew[0].Seat, game.State.Crew[1].Seat);
        game.State = game.State.WithBoard(game.State.Board!.WithToken(Suits(game.State).First() with { At = null, Carrier = wearer }));
        var hit = new Attacked(thief.Value, wearer.Value, new DiceRoll([2]), new DiceRoll([0]), "hit");
        var after = new VentCycle().On(Pack, game.State, hit, []);
        Assert.Equal(thief, Suits(after).First().Carrier);
    }

    [Fact]
    public void When_command_vents_everyone_aboard_is_lost_and_only_the_escaped_win()
    {
        var game = Begin();
        var escaped = game.State.Crew[0].Seat;
        game.State = game.State.WithCrew(game.Crew(escaped) with { Escaped = true })
            .WithBoard(game.State.Board!.WithCounter("vented", 2).WithCounter("nextVent", 1));
        EndRound(game);
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.Equal([escaped], game.State.Board!.Outcome!.Winners);
        Assert.All(game.State.Crew.Where(c => c.Seat != escaped), c => Assert.False(c.Alive));
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    private static int Total(CrewState c) => c.Steps.Brawn + c.Steps.Agility;

    private static Play Begin() => Play.From(Pack, ScenarioRun.Begun(Id));

    private static void EndRound(Play game)
    {
        var round = game.State.Round;
        while (game.State.Round == round && game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
    }
}
