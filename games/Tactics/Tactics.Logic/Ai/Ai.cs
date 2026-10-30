namespace Tactics.Logic;

/// <summary>
/// The utility AI (spec §5): one planner for enemies, absent players' characters and timed-out turns, each with its own
/// profile. It never draws from the battle's RNG, so the same state always gives the same plan.
/// </summary>
public static class Ai
{
    /// <summary>
    /// The whole turn for <paramref name="unit"/>, whose turn it must be (otherwise the plan has no commands): moves and
    /// abilities in order, ending with <see cref="EndTurn"/>, each of which <see cref="Battle.Apply"/> accepts in the state
    /// the plan expects (a move cut short by an opportunity attack leaves the rest stale: plan again). The profile is the
    /// unit's own (<see cref="AiProfile.For"/>) unless <see cref="AiOptions.ProfileOverride"/> names another. See
    /// <see cref="Planner"/> for how the turn is chosen.
    /// </summary>
    public static TurnPlan Plan(BattleState s, UnitId unit, RulesPack rules, AiOptions? options = null)
        => Planner.Plan(s, unit, rules, options);
}
