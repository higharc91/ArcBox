namespace Meridian.Logic.Tests.Content;

/// <summary>The Breach table for all twelve scenarios (M7 plan, Task 10).</summary>
public sealed class BreachTableTests
{
    private static readonly RulesPack Pack = BasePack.Pack;

    [Fact]
    public void Each_anomaly_leads_to_three_scenarios_and_each_scenario_appears_three_times()
    {
        Assert.All(Pack.Breach.Table, row => Assert.Equal(3, row.Scenarios.Distinct().Count()));
        var counts = Pack.Breach.Table.SelectMany(r => r.Scenarios).GroupBy(s => s).ToDictionary(g => g.Key, g => g.Count());
        Assert.Equal(ScenarioRegistry.Default.All.Select(s => s.Info.Id).Order(), counts.Keys.Order());
        Assert.All(counts, c => Assert.Equal(3, c.Value));
    }

    [Fact]
    public void Every_row_spans_two_archetypes()
        => Assert.All(Pack.Breach.Table, row =>
            Assert.True(row.Scenarios.Select(s => Pack.Scenarios.Get(s).Info.Archetype).Distinct().Count() >= 2, $"{row.Anomaly}"));

    /// <summary>
    /// Every row has a scenario that can run on the barest ship, the start layout alone, at every player count, so the
    /// fallback is rarely needed (spec §9).
    /// </summary>
    [Fact]
    public void Every_row_has_a_scenario_that_can_always_run()
    {
        foreach (var players in new[] { 3, 4, 5, 6 })
        {
            var state = Rules.NewGame(Pack, BotRunner.Seats(Pack, players), 1);
            var breach = new BreachInfo(state.Crew[0].Seat, null, state.Crew[0].At, Forced: false, Roll: null);
            Assert.All(Pack.Breach.Table, row =>
                Assert.Contains(row.Scenarios, s => Pack.Scenarios.Get(s).CanRun(Pack, state, breach)));
        }
    }
}
