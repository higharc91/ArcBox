namespace Meridian.Logic.Tests.Scenarios;

public sealed class ColdSleepTests
{
    private const string Id = ColdSleep.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static SeatId Waker(GameState s) => s.Board!.Roles.Single(r => r.Role == ColdSleep.Waker).Seat;
    private static IEnumerable<Token> Sleepers(GameState s) => s.Board!.Tokens.Where(t => t.Kind == ColdSleep.Sleeper);
    private static IEnumerable<Token> Relays(GameState s) => s.Board!.Tokens.Where(t => t.Kind == ColdSleep.Relay);
    private static CompartmentId Airlock(GameState s) => Primitives.FirstOf(s, "airlock")!.Value;

    [Fact]
    public void The_finder_wakes_the_cryo_crew_and_relays_sit_on_engineering()
    {
        var state = ScenarioRun.Begun(Id);
        Assert.Equal(state.Breach!.Seat, Waker(state));
        var relays = Relays(state).ToList();
        Assert.InRange(relays.Count, 1, 3);
        Assert.All(relays, r => Assert.Equal(DeckId.Engineering, state.Ship.Get(r.At!.Value).At.Deck));
        Assert.All(relays, r => Assert.Equal("powered", r.State));
        Assert.All(Sleepers(state), s => Assert.Equal(Waker(state), s.Controller));
        Assert.Single(Sleepers(state));
    }

    [Fact]
    public void One_sleeper_wakes_per_powered_relay_each_round()
    {
        var game = Begin();
        var before = Sleepers(game.State).Count();
        var powered = Relays(game.State).Count(r => r.State == "powered");
        EndRound(game);
        Assert.Equal(before + powered, Sleepers(game.State).Count());
    }

    [Fact]
    public void Rerouting_a_relay_stops_its_sleepers()
    {
        var game = Begin();
        var crew = game.State.Crew.First(c => c.Seat != Waker(game.State) && c.Aboard).Seat;
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null };
        var relay = Relays(game.State).First();
        game.Put(crew, relay.At!.Value);
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Tech), t => t >= 4).Do(new ScenarioAct(crew, "reroute"));
        Assert.Equal("rerouted", Relays(game.State).First(r => r.Id == relay.Id).State);
        var before = Sleepers(game.State).Count();
        var powered = Relays(game.State).Count(r => r.State == "powered");
        EndRound(game);
        Assert.Equal(before + powered, Sleepers(game.State).Count());
    }

    [Fact]
    public void With_the_power_cut_stunned_sleepers_sleep_again_and_none_awake_wins_for_the_crew()
    {
        var game = Begin();
        var board = game.State.Board!;
        foreach (var relay in Relays(game.State).ToList())
        {
            board = board.WithToken(relay with { State = "rerouted" });
        }
        foreach (var sleeper in Sleepers(game.State).ToList())
        {
            board = board.WithToken(sleeper with { Stunned = true });
        }
        game.State = game.State.WithBoard(board);
        EndRound(game);
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.DoesNotContain(Waker(game.State), game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void Sleepers_filling_the_airlock_at_a_rounds_end_overrun_the_ship()
    {
        var game = Begin();
        var waker = Waker(game.State);
        var board = game.State.Board!;
        for (var i = 0; i < ColdSleep.Overrun(game.State); i++)
        {
            board = board.WithToken(new Token($"sleeper-x{i}", ColdSleep.Sleeper, Airlock(game.State), Audience.Everyone, Controller: waker,
                                              Stats: new MonsterStats(3, 2, 1)));
        }
        game.State = game.State.WithBoard(board);
        EndRound(game);
        Assert.Equal([waker], game.State.Board!.Outcome!.Winners);
    }

    /// <summary>Review focus 6: the power cut and the Airlock overrun at the same round's end. The crew's objective wins.</summary>
    [Fact]
    public void A_completed_objective_beats_an_overrun_at_the_same_moment()
    {
        var game = Begin();
        var waker = Waker(game.State);
        var board = game.State.Board!;
        foreach (var relay in Relays(game.State).ToList())
        {
            board = board.WithToken(relay with { State = "rerouted" });
        }
        foreach (var sleeper in Sleepers(game.State).ToList())
        {
            board = board.WithoutToken(sleeper.Id);
        }
        for (var i = 0; i < ColdSleep.Overrun(game.State); i++)
        {
            board = board.WithToken(new Token($"sleeper-x{i}", ColdSleep.Sleeper, Airlock(game.State), Audience.Everyone, Controller: waker,
                                              Stats: new MonsterStats(3, 2, 1), Stunned: true));
        }
        game.State = game.State.WithBoard(board);
        EndRound(game);
        Assert.DoesNotContain(waker, game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void The_round_limit_goes_to_the_waker_and_it_needs_a_cryo_bay()
    {
        var game = Begin();
        game.State = game.State.WithBoard(game.State.Board! with { RoundLimit = 1 });
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal([Waker(game.State)], game.State.Board!.Outcome!.Winners);

        var state = ScenarioRun.Begun(Id);
        var without = state with
        {
            TilePile = state.TilePile.Where(t => t.Value != "cryo_bay").ToEqArray(),
            Ship = state.Ship with { Compartments = state.Ship.Compartments.Where(c => c.Tile.Value != "cryo_bay").ToEqArray() },
        };
        Assert.False(new ColdSleep().CanRun(Pack, without, state.Breach!));
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    private static Play Begin() => Play.From(Pack, ScenarioRun.Begun(Id));

    /// <summary>Every seat ends its turn until the round closes.</summary>
    private static void EndRound(Play game)
    {
        var round = game.State.Round;
        while (game.State.Round == round && game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
    }
}
