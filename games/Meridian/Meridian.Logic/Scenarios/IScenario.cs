namespace Meridian.Logic;

/// <summary>A scenario's fixed facts. Its numbers and text are in its content file (<see cref="ScenarioDef"/>).</summary>
public sealed record ScenarioInfo(string Id, string Archetype, int MinPlayers, int MaxPlayers);

/// <summary>
/// One scenario (spec §4.2). Every method is pure: it takes a state and returns one, drawing randomness from the state's
/// <see cref="Rng"/>. A scenario keeps its data in <see cref="GameState.Board"/>, never in fields, so one instance serves
/// every room.
/// </summary>
public interface IScenario
{
    ScenarioInfo Info { get; }

    /// <summary>Can this scenario run on this ship with these players? Checked when the Breach fires.</summary>
    bool CanRun(RulesPack pack, GameState state, BreachInfo breach);

    /// <summary>
    /// Deals roles and places the scenario's tokens, counters and objectives. Returns the state with a board whose
    /// <see cref="ScenarioBoard.Scenario"/> is this scenario's id. It may also set who acts first in the endgame.
    /// </summary>
    GameState Setup(RulesPack pack, GameState state, BreachInfo breach);

    /// <summary>Reacts to something that happened (a round ended, someone died, a check resolved), adding any events.</summary>
    GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events);

    /// <summary>The scenario actions a seat may take now (plan Task 4).</summary>
    IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat);

    /// <summary>Resolves one of its actions (plan Task 4).</summary>
    GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events);

    /// <summary>Has a side won? Checked after every command and at each round's end, before the round limit.</summary>
    Outcome? Evaluate(RulesPack pack, GameState state);

    /// <summary>Who wins when the round limit is reached (spec §2.9: every scenario names one).</summary>
    Outcome AtLimit(RulesPack pack, GameState state);

    /// <summary>How a bot plays a role this scenario deals. Required for every role (spec §4.2).</summary>
    IBotPolicy Bot(string role);
}

/// <summary>An action a phone offers for a scenario, at a place, to a seat (spec §4.3). <see cref="Data"/> names a target.</summary>
public sealed record ScenarioAction(string Id, string Label, string? Data = null);
