namespace Meridian.Logic.Tests.Scenarios;

public sealed class LastCleanSampleTests
{
    private const string Id = LastCleanSample.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static Token? Find(GameState s, string id) => s.Board!.Tokens.FirstOrDefault(t => t.Id == id);

    [Fact]
    public void Everyone_is_infected_and_the_sample_waits_in_a_lab()
    {
        var state = ScenarioRun.Begun(Id);
        Assert.All(state.Crew, c => Assert.Equal(0, state.Board!.Value($"infection-{c.Seat}")));
        if (Find(state, LastCleanSample.Sample) is { } sample)
        {
            Assert.Contains(state.Ship.Get(sample.At!.Value).Tile.Value, new[] { "med_bay", "hydroponics", "containment_lab", "isolation_ward" });
        }
    }

    [Fact]
    public void Synthesize_inject_and_keep_it_until_your_next_turn_to_win()
    {
        var game = WithSample();
        var seat = game.State.Crew[0].Seat;
        game.Put(seat, Find(game.State, LastCleanSample.Sample)!.At!.Value);
        game.State = game.State with { Active = 0, Waiting = null, Turn = game.State.Turn with { Acted = false } };
        game.Rig(Rules.Value(Pack, game.Crew(seat), Trait.Tech), t => t >= 5).Do(new ScenarioAct(seat, "synthesize"));
        Assert.Equal(seat, Find(game.State, LastCleanSample.Dose)!.Carrier);

        game.State = game.State with { Turn = game.State.Turn with { Acted = false } };
        game.Do(new ScenarioAct(seat, "inject"));
        Assert.NotEqual(Phase.Over, game.State.Phase);   // it takes a little while
        do
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        while (game.State.Phase == Phase.Endgame && game.State.ActiveSeat != seat);
        Assert.Equal([seat], game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void A_hit_takes_the_dose_and_cancels_the_injection()
    {
        var game = WithSample();
        var (carrier, thief) = (game.State.Crew[0].Seat, game.State.Crew[1].Seat);
        game.State = game.State.WithBoard(game.State.Board!.WithoutToken(LastCleanSample.Sample)
            .WithToken(new Token(LastCleanSample.Dose, LastCleanSample.Dose, null, Audience.Everyone, Carrier: carrier, State: carrier.Value)));
        var hit = new Attacked(thief.Value, carrier.Value, new DiceRoll([2]), new DiceRoll([0]), "hit");
        var after = new LastCleanSample().On(Pack, game.State, hit, []);
        Assert.Equal(thief, Find(after, LastCleanSample.Dose)!.Carrier);
        Assert.Null(Find(after, LastCleanSample.Dose)!.State);
    }

    /// <summary>Review focus 6: the infection kills an injected seat before its next turn starts. Nobody is cured.</summary>
    [Fact]
    public void An_injection_dies_with_its_seat_at_the_infection_limit()
    {
        var game = WithSample();
        // The round's first seat injects on its turn; the round closes before its next turn can start.
        game.State = game.State with { Active = game.State.First, Waiting = null };
        var seat = game.State.ActiveSeat;
        var board = game.State.Board!.WithoutToken(LastCleanSample.Sample)
            .WithToken(new Token(LastCleanSample.Dose, LastCleanSample.Dose, null, Audience.Everyone, Carrier: seat, State: seat.Value))
            .WithCounter($"infection-{seat}", Pack.ScenarioDef(Id).Number("infectionDeath") - 1);
        game.State = game.State.WithBoard(board);
        var round = game.State.Round;
        while (game.State.Round == round && game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.False(game.Crew(seat).Alive);
        Assert.NotEqual([seat], game.State.Board!.Outcome?.Winners ?? []);
        Assert.Equal(game.Crew(seat).At, Find(game.State, LastCleanSample.Dose)!.At);   // dropped where they fell
    }

    [Fact]
    public void Infection_rises_each_round_and_everyone_dying_means_nobody_wins()
    {
        var game = WithSample();
        var round = game.State.Round;
        while (game.State.Round == round && game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.All(game.State.Crew, c => Assert.Equal(1, game.State.Board!.Value($"infection-{c.Seat}")));

        var dying = WithSample();
        foreach (var crew in dying.State.Crew.ToList())
        {
            dying.Resolve(r => r.Kill(crew.Seat));
        }
        dying.Do(new EndTurn(dying.State.ActiveSeat));
        Assert.Empty(dying.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void A_sample_appears_in_the_first_lab_found_when_none_was_aboard()
    {
        var game = WithSample();
        var state = game.State.WithBoard(game.State.Board!.WithoutToken(LastCleanSample.Sample));
        var lab = new Compartment(new CompartmentId("c99"), new TileId("med_bay"), new Cell(DeckId.Habitat, 4, 4), 0, []);
        var after = new LastCleanSample().On(Pack, state, new CompartmentRevealed(state.Crew[0].Seat, lab), []);
        Assert.Equal(lab.Id, Find(after, LastCleanSample.Sample)!.At);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    /// <summary>An endgame with the sample on the board, placed in a lab-like spot if no lab was found yet.</summary>
    private static Play WithSample()
    {
        var game = Play.From(Pack, ScenarioRun.Begun(Id));
        if (Find(game.State, LastCleanSample.Sample) is null)
        {
            game.State = game.State.WithBoard(game.State.Board!.WithToken(
                new Token(LastCleanSample.Sample, LastCleanSample.Sample, game.State.Crew[0].At, Audience.Everyone)));
        }
        return game;
    }
}
