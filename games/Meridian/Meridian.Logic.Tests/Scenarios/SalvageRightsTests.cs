namespace Meridian.Logic.Tests.Scenarios;

public sealed class SalvageRightsTests
{
    private const string Id = SalvageRights.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);
    private static Play Begin() => Play.From(Pack, ScenarioRun.Begun(Id));
    private static Token Prize(GameState s) => s.Board!.Tokens.Single(t => t.Id == "prize");
    private static CompartmentId Airlock(GameState s) => s.Ship.Compartments.First(c => c.Tile.Value == "airlock").Id;

    [Fact]
    public void The_finder_carries_the_Prize_and_everyone_is_on_their_own()
    {
        var state = ScenarioRun.Begun(Id);
        Assert.Equal(state.Breach!.Seat, Prize(state).Carrier);
        Assert.DoesNotContain(Prize(state).Card!.Value, state.CrewOf(Prize(state).Carrier!.Value).Items);   // an object, not gear
        Assert.Equal(state.Crew.Count, state.Board!.Roles.Select(r => r.Side).Distinct().Count());
    }

    [Fact]
    public void Ending_a_turn_in_the_airlock_with_the_Prize_wins_alone()
    {
        var game = Begin();
        var carrier = Prize(game.State).Carrier!.Value;
        game.State = game.State with { Active = game.State.IndexOf(carrier), Waiting = null, Turn = new TurnFlags() };
        game.Put(carrier, Airlock(game.State)).Do(new EndTurn(carrier));
        Assert.Equal([carrier], game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void The_carrier_is_slowed()
    {
        var game = Begin();
        var carrier = Prize(game.State).Carrier!.Value;
        while (game.State.ActiveSeat != carrier)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal(Math.Max(1, Rules.Value(Pack, game.Crew(carrier), Trait.Agility) - 2), game.State.MovesLeft);
    }

    [Fact]
    public void A_hit_on_the_carrier_takes_the_Prize()
    {
        var game = Begin();
        var carrier = Prize(game.State).Carrier!.Value;
        var thief = game.State.Crew.First(c => c.Seat != carrier).Seat;
        game.State = game.State with { Active = game.State.IndexOf(thief), Waiting = null, Turn = new TurnFlags() };
        game.Put(thief, game.Crew(carrier).At);
        var a = Rules.Value(Pack, game.Crew(thief), Trait.Brawn) + 0;
        var d = Rules.Value(Pack, game.Crew(carrier), Trait.Brawn);
        game.RigFight(a, d, (x, y) => x > y).Do(new Attack(thief, carrier.Value));
        Assert.Equal(thief, Prize(game.State).Carrier);
        Assert.Empty(Invariants.Check(Pack, game.State));
    }

    [Fact]
    public void A_dead_carriers_Prize_can_be_picked_up()
    {
        var game = Begin();
        var carrier = Prize(game.State).Carrier!.Value;
        var picker = game.State.Crew.First(c => c.Seat != carrier).Seat;
        // A real death, inside a step, so the scenario sees it: a failed check with both physical traits at step 1.
        var conduit = new CardId("sparking_conduit");
        game.State = game.State with
        {
            Active = game.State.IndexOf(carrier), Waiting = null, Turn = new TurnFlags(),
            Incidents = new CardPile(game.State.Incidents.Draw.Remove(conduit), game.State.Incidents.Discard.Remove(conduit)),
        };
        game.Steps(carrier, new TraitSteps(1, 1, 4, 4)).Check(carrier, "sparking_conduit");
        game.Rig(game.Dice, t => t <= 1).Do(new Roll(carrier));
        Assert.False(game.Crew(carrier).Alive);
        Assert.Null(Prize(game.State).Carrier);
        game.State = game.State with { Active = game.State.IndexOf(picker), Waiting = null, Turn = new TurnFlags() };
        game.Put(picker, Prize(game.State).At!.Value).Do(new ScenarioAct(picker, "takePrize"));
        Assert.Equal(picker, Prize(game.State).Carrier);
        Assert.Empty(Invariants.Check(Pack, game.State));
    }

    [Fact]
    public void The_round_limit_goes_to_nobody()
    {
        var game = Begin();
        game.State = game.State.WithBoard(game.State.Board! with { RoundLimit = 1 });
        game.State = game.State.WithCrew(game.Crew(Prize(game.State).Carrier!.Value) with { At = game.State.Ship.Compartments.First(c => c.Tile.Value == "command_landing").Id });
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Empty(game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);
}
