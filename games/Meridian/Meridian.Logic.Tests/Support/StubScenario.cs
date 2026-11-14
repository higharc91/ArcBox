namespace Meridian.Logic.Tests;

/// <summary>
/// A scenario for testing the contract before the real ones exist: everyone is crew; a "progress" counter rises by one at
/// each round's end, and reaching <see cref="Target"/> wins; the round limit goes to the ship.
/// </summary>
internal sealed class StubScenario(string id = "stub", int target = 99, Func<GameState, bool>? canRun = null) : IScenario
{
    public int Target { get; } = target;
    public ScenarioInfo Info { get; } = new(id, "test", 3, 6);

    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => canRun?.Invoke(state) ?? true;

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
        => state.WithBoard(new ScenarioBoard(Info.Id,
            state.Crew.Select(c => new RoleCard(c.Seat, "crew", "crew", Audience.Everyone)).ToEqArray(), [],
            [new Counter("progress", 0, Audience.Everyone)], [], RoundLimit: 1, StartRound: 0));

    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
        => e is RoundClosed ? state.WithBoard(state.Board!.WithCounter("progress", state.Board.Value("progress") + 1)) : state;

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat) => [];
    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events) => state;

    public Outcome? Evaluate(RulesPack pack, GameState state)
        => state.Board!.Value("progress") >= Target ? new Outcome(state.Crew.Select(c => c.Seat).ToEqArray(), "The crew did it.") : null;

    public Outcome AtLimit(RulesPack pack, GameState state) => new([], "The ship kept them.");

    public IBotPolicy Bot(string role) => ExploreBot.Instance;

    public static ScenarioDef Def(string id = "stub", int limit = 3)
        => new(id, $"Stub {id}", limit, [new RoleDef("crew", "crew", "Crew", "Do the thing.")]);

    /// <summary>The test pack choosing from these stubs, with a Breach table entry for the test Anomaly.</summary>
    public static RulesPack Pack(params (StubScenario Scenario, int Limit)[] stubs)
    {
        var ids = stubs.Select(s => s.Scenario.Info.Id).ToEqArray();
        var table = new BreachTable(ids, [new BreachEntry(new CardId("humming_shard"), ids)]);
        return TestPack.Load().WithScenarios(new ScenarioSet(stubs.Select(s => (IScenario)s.Scenario)), table,
                                             stubs.Select(s => Def(s.Scenario.Info.Id, s.Limit)).ToList());
    }
}
