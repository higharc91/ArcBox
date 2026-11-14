namespace Meridian.Logic.Tests.Endgame;

public sealed class ScenarioContractTests
{
    private static Play Begun(StubScenario stub, int limit)
    {
        var game = Play.Game(StubScenario.Pack((stub, limit))).Pile(CardKind.Anomaly, ["humming_shard"]);
        game.State = game.State with { AnomaliesDrawn = 8 };
        game.Draw(CardKind.Anomaly);
        game.Do(new Begin(game.State.ActiveSeat));
        return game;
    }

    private static void Round(Play game)
    {
        for (var i = 0; i < 3 && game.State.Phase == Phase.Endgame; i++)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
    }

    [Fact]
    public void The_round_limit_ends_the_game_for_the_side_it_names()
    {
        var game = Begun(new StubScenario("stub"), limit: 2);
        Round(game);
        Assert.Equal(Phase.Endgame, game.State.Phase);
        Round(game);
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.Equal("The ship kept them.", game.State.Board!.Outcome!.Message);
        Assert.Empty(game.Legal(Play.S1));
    }

    [Fact]
    public void An_objective_completed_on_the_last_round_beats_the_limit()
    {
        // M3 review focus 4: progress reaches 2 at the same round end that reaches the limit.
        var game = Begun(new StubScenario("stub", target: 2), limit: 2);
        Round(game);
        Round(game);
        Assert.Equal("The crew did it.", game.State.Board!.Outcome!.Message);
        Assert.Equal(3, game.State.Board.Outcome.Winners.Count);
        Assert.IsType<GameOver>(game.Events[^1]);
    }

    [Fact]
    public void The_registry_refuses_a_listed_scenario_with_no_class()
    {
        var pack = StubScenario.Pack((new StubScenario("alpha"), 3));
        var broken = pack.WithScenarios(ScenarioSet.Empty);
        Assert.Contains(ScenarioRegistry.Check(broken), e => e.Message.Contains("no scenario class"));
    }

    [Fact]
    public void The_registry_refuses_a_role_with_no_bot()
    {
        var pack = StubScenario.Pack((new StubScenario("alpha"), 3));
        var extraRole = pack.WithScenarios(pack.Scenarios, defs: [StubScenario.Def("alpha") with
        {
            Roles = [new RoleDef("crew", "crew", "Crew", "x"), new RoleDef("ghost", "ghost", "Ghost", "x")],
        }]);
        Assert.Empty(ScenarioRegistry.Check(pack));
        Assert.Empty(ScenarioRegistry.Check(extraRole));   // the stub gives every role a bot
    }

    [Fact]
    public void A_briefing_over_120_words_is_refused()
    {
        var pack = StubScenario.Pack((new StubScenario("alpha"), 3));
        var wordy = pack.WithScenarios(pack.Scenarios, defs: [StubScenario.Def("alpha") with
        {
            Roles = [new RoleDef("crew", "crew", "Crew", string.Join(' ', Enumerable.Repeat("word", 121)))],
        }]);
        Assert.Contains(Validation.Check(wordy), e => e.Message.Contains("121 words"));
    }

    /// <summary>
    /// On paper (M3 plan, Task 3): every scenario of spec §4.5, M7's included, against the primitives it needs. A scenario
    /// needing one that doesn't exist fails here, long before M7 tries to build it.
    /// </summary>
    [Fact]
    public void Every_planned_scenario_can_be_built_from_the_primitives()
    {
        string[] built = ["role", "hidden-role", "monster", "token", "hidden-token", "counter", "hidden-counter", "objective",
                          "action", "hazard", "vote", "escape", "carried", "combat", "round-limit"];
        var needs = new Dictionary<string, string[]>
        {
            ["the_passenger"] = ["role", "monster", "counter", "action", "hazard", "combat"],
            ["company_asset"] = ["role", "monster", "token", "action", "combat"],
            ["captain_on_deck"] = ["role", "counter", "token", "action", "hazard"],
            ["cold_sleep"] = ["role", "monster", "token", "action", "hazard", "combat"],
            ["one_of_us_isnt"] = ["hidden-role", "token", "counter", "action", "vote", "combat"],
            ["scuttle_charge"] = ["hidden-role", "hidden-token", "action"],
            ["tiller_says_no"] = ["token", "counter", "action", "hazard"],
            ["hull_rot"] = ["token", "hazard", "action", "carried"],
            ["decaying_orbit"] = ["token", "counter", "action", "escape", "hazard"],
            ["vent_cycle"] = ["token", "hazard", "escape", "carried"],
            ["salvage_rights"] = ["carried", "combat"],
            ["the_last_clean_sample"] = ["hidden-counter", "action", "combat"],
        };
        Assert.Equal(12, needs.Count);
        foreach (var (scenario, needed) in needs)
        {
            Assert.All(needed, n => Assert.Contains(n, built));
        }
    }
}
