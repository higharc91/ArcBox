namespace Meridian.Logic.Tests.Endgame;

public sealed class BreachSelectionTests
{
    /// <summary>A game with the ninth Anomaly about to be drawn by s1 in the airlock: drawing it fires the Breach.</summary>
    private static Play AtTheBreach(RulesPack pack)
    {
        var game = Play.Game(pack).Pile(CardKind.Anomaly, ["humming_shard"]);
        game.State = game.State with { AnomaliesDrawn = 8 };
        return game.Draw(CardKind.Anomaly);
    }

    [Fact]
    public void The_breach_sets_up_a_scenario_from_the_table()
    {
        var game = AtTheBreach(StubScenario.Pack((new StubScenario("alpha"), 3)));
        Assert.Equal(Phase.Breach, game.State.Phase);
        Assert.Equal("alpha", game.State.Board!.Scenario);
        Assert.Equal(3, game.State.Board.RoundLimit);
        Assert.Contains(new ScenarioChosen("alpha", "Stub alpha"), game.Events);
        Assert.Equal(Play.S2, game.State.ActiveSeat);   // the seat after the one who fired it
        Assert.Equal([new Begin(Play.S2)], game.Legal(Play.S2));
        Assert.Empty(game.Legal(Play.S1));
    }

    [Fact]
    public void A_scenario_that_cant_run_is_passed_over()
    {
        var game = AtTheBreach(StubScenario.Pack((new StubScenario("alpha", canRun: _ => false), 3), (new StubScenario("beta"), 3)));
        Assert.Equal("beta", game.State.Board!.Scenario);
    }

    [Fact]
    public void With_nothing_runnable_the_breach_falls_back_to_decaying_orbit()
    {
        // M3 review focus 6.
        var pack = StubScenario.Pack((new StubScenario("alpha", canRun: _ => false), 3),
                                     (new StubScenario(ScenarioRegistry.Fallback, canRun: _ => false), 3));
        Assert.Equal(ScenarioRegistry.Fallback, AtTheBreach(pack).State.Board!.Scenario);
    }

    [Fact]
    public void A_forced_scenario_wins_over_the_table()
    {
        var pack = StubScenario.Pack((new StubScenario("alpha"), 3), (new StubScenario("beta"), 3)).WithForcedScenario("beta");
        Assert.Equal("beta", AtTheBreach(pack).State.Board!.Scenario);
    }

    [Fact]
    public void With_no_scenarios_the_board_stays_empty_as_in_m2()
        => Assert.Null(AtTheBreach(TestPack.Load()).State.Board);

    [Fact]
    public void Begin_starts_the_endgame_with_a_fresh_turn()
    {
        var game = AtTheBreach(StubScenario.Pack((new StubScenario("alpha"), 3)));
        game.Do(new Begin(Play.S2));
        Assert.Equal(Phase.Endgame, game.State.Phase);
        Assert.Equal(game.State.Round, game.State.Board!.StartRound);
        Assert.Equal(Rules.Value(game.Pack, game.Crew(Play.S2), Trait.Agility), game.State.MovesLeft);
        Assert.Contains(new EndTurn(Play.S2), game.Legal(Play.S2));
        Assert.Contains(new TurnStarted(Play.S2, game.State.Round, game.State.MovesLeft), game.Events);
    }
}
