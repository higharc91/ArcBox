namespace Meridian.Logic.Tests.Scenarios;

public sealed class ScuttleChargeTests
{
    private const string Id = ScuttleCharge.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static SeatId Saboteur(GameState s) => s.Board!.Roles.Single(r => r.Role == ScuttleCharge.Saboteur).Seat;
    private static IEnumerable<Token> Charges(GameState s) => s.Board!.Tokens.Where(t => t.Kind == ScuttleCharge.Charge);

    [Fact]
    public void One_secret_saboteur_carries_charges_only_they_can_count()
    {
        var state = ScenarioRun.Begun(Id, players: 5);
        var saboteur = Saboteur(state);
        Assert.All(state.Board!.Roles, r => Assert.Equal(Audience.OfSeat(r.Seat), r.Audience));
        Assert.Equal(Audience.OfSeat(saboteur), state.Board.CounterOf("kit")!.Audience);
        Assert.Equal(4, ScuttleCharge.Needed(Pack, state));
        Assert.Equal(5, state.Board.Value("kit"));   // one spare
        Assert.DoesNotContain(Visibility.For(state, null).Board!.Counters, c => c.Id == "kit");
    }

    [Fact]
    public void Only_the_saboteur_alone_may_plant_and_the_charge_is_theirs_alone_to_see()
    {
        var game = AloneSaboteur();
        var saboteur = Saboteur(game.State);
        Assert.Contains(new ScenarioAct(saboteur, "plant"), game.Legal(saboteur));
        var events = game.Do(new ScenarioAct(saboteur, "plant"));
        Assert.Empty(events);   // nothing happened that anyone could see
        var charge = Assert.Single(Charges(game.State));
        Assert.Equal(Audience.OfSeat(saboteur), charge.Audience);
        Assert.DoesNotContain(Visibility.For(game.State, null).Board!.Tokens, t => t.Kind == ScuttleCharge.Charge);
        Assert.All(game.State.Crew.Where(c => c.Seat != saboteur),
                   c => Assert.DoesNotContain(Visibility.For(game.State, c.Seat).Board!.Tokens, t => t.Kind == ScuttleCharge.Charge));

        // Company in the room: no planting.
        var crew = game.State.Crew.First(c => c.Seat != saboteur && c.Aboard).Seat;
        var other = game.State.Ship.Compartments.First(c => c.Id != game.Crew(saboteur).At).Id;
        game.Put(saboteur, other);
        game.Put(crew, other);
        game.State = game.State with { Turn = game.State.Turn with { Acted = false } };
        Assert.DoesNotContain(new ScenarioAct(saboteur, "plant"), game.Legal(saboteur));
        Assert.DoesNotContain(new ScenarioAct(crew, "plant"), game.Legal(crew));
    }

    [Fact]
    public void A_search_finds_a_charge_for_everyone_and_a_disarm_removes_it()
    {
        var game = AloneSaboteur();
        var saboteur = Saboteur(game.State);
        game.Do(new ScenarioAct(saboteur, "plant"));
        var at = game.Crew(saboteur).At;
        var crew = game.State.Crew.First(c => c.Seat != saboteur && c.Aboard).Seat;
        game.Put(crew, at);
        game.State = game.State with { Active = game.State.IndexOf(crew), Turn = game.State.Turn with { Acted = false } };
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Nerve), t => t >= 3).Do(new ScenarioAct(crew, "search"));
        Assert.Contains(Visibility.For(game.State, null).Board!.Tokens, t => t.Kind == ScuttleCharge.Charge);
        Assert.Contains(game.Events, e => e is ScenarioNotice { Fx: "sparks" });

        game.State = game.State with { Turn = game.State.Turn with { Acted = false } };
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Tech), t => t >= 4).Do(new ScenarioAct(crew, "disarm"));
        Assert.Empty(Charges(game.State));
        Assert.Equal(1, game.State.Board!.Value("disarmed"));
    }

    [Fact]
    public void The_armed_count_goes_public_at_the_rounds_end_and_enough_scuttles_the_ship()
    {
        var game = Play.From(Pack, ScenarioRun.Begun(Id));
        var saboteur = Saboteur(game.State);
        var places = game.State.Ship.Compartments.Take(ScuttleCharge.Needed(Pack, game.State)).ToList();
        var board = game.State.Board!;
        for (var i = 0; i < places.Count; i++)
        {
            board = board.WithToken(new Token($"charge-{i + 1}", ScuttleCharge.Charge, places[i].Id, Audience.OfSeat(saboteur)));
        }
        game.State = game.State.WithBoard(board);
        Assert.Equal(0, game.State.Board!.Value("armed"));   // nothing public until the round ends
        var round = game.State.Round;
        while (game.State.Round == round && game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal([saboteur], game.State.Board!.Outcome!.Winners);
        Assert.Equal(places.Count, game.State.Board.Value("armed"));
    }

    [Fact]
    public void The_crew_win_at_the_round_limit_or_when_the_saboteur_dies()
    {
        var game = Play.From(Pack, ScenarioRun.Begun(Id));
        game.State = game.State.WithBoard(game.State.Board! with { RoundLimit = 1 });
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.DoesNotContain(Saboteur(game.State), game.State.Board!.Outcome!.Winners);

        var killed = Play.From(Pack, ScenarioRun.Begun(Id));
        killed.Resolve(r => r.Kill(Saboteur(killed.State)));
        killed.Do(new EndTurn(killed.State.ActiveSeat));
        Assert.Equal(Phase.Over, killed.State.Phase);
        Assert.DoesNotContain(Saboteur(killed.State), killed.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    /// <summary>The saboteur's turn, alone in a compartment nobody else is in.</summary>
    private static Play AloneSaboteur()
    {
        var game = Play.From(Pack, ScenarioRun.Begun(Id));
        var saboteur = Saboteur(game.State);
        var empty = game.State.Ship.Compartments.First(c => game.State.Crew.All(x => x.At != c.Id)).Id;
        game.Put(saboteur, empty);
        game.State = game.State with { Active = game.State.IndexOf(saboteur), Waiting = null, Turn = game.State.Turn with { Acted = false } };
        return game;
    }

    [Fact]
    public void A_search_sweeps_the_neighbouring_compartments_too()
    {
        var game = AloneSaboteur();
        var saboteur = Saboteur(game.State);
        game.Do(new ScenarioAct(saboteur, "plant"));
        var planted = game.Crew(saboteur).At;
        var next = game.State.Ship.Adjacent(game.State.Ship.Get(planted)).First().Id;
        var crew = game.State.Crew.First(c => c.Seat != saboteur && c.Aboard).Seat;
        game.Put(crew, next);
        game.State = game.State with { Active = game.State.IndexOf(crew), Turn = game.State.Turn with { Acted = false } };
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Nerve), t => t >= 3).Do(new ScenarioAct(crew, "search"));
        Assert.Contains(Visibility.For(game.State, null).Board!.Tokens, t => t.Kind == ScuttleCharge.Charge && t.At == planted);
    }
}
