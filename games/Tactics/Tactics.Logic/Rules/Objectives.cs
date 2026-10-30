using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The battle's win and lose conditions (spec §3.9). Leaves evaluated so far: <c>"defeat_all"</c> (no enemy is Up or
/// Downed) and <c>"party_out"</c> (no party member is Up: every one is Downed, Out or Dead). Summons don't count for either
/// side. Task 22 adds the other leaves; until then an unknown leaf never holds.
/// </summary>
public static class Objectives
{
    /// <summary>The leaf that holds once every enemy has been defeated.</summary>
    public const string DefeatAll = "defeat_all";

    /// <summary>The leaf that holds once no party member is left standing.</summary>
    public const string PartyOut = "party_out";

    /// <summary>
    /// Whether <paramref name="expr"/> holds in <paramref name="state"/>: a leaf by its rule, <c>any</c> when one entry holds,
    /// <c>all</c> when every entry holds.
    /// </summary>
    public static bool Holds(BattleState state, ObjectiveExpr expr)
    {
        if (expr.Leaf is { } leaf)
            return leaf switch
            {
                DefeatAll => !state.Units.Values.Any(u => Counts(u, Side.Enemy) && DownedRules.InBattle(u)),
                PartyOut => !state.Units.Values.Any(u => Counts(u, Side.Party) && u.Life == LifeState.Up),
                _ => false,
            };
        if (!expr.Any.IsDefaultOrEmpty)
            return expr.Any.Any(e => Holds(state, e));
        return !expr.All.IsDefaultOrEmpty && expr.All.All(e => Holds(state, e));
    }

    /// <summary>
    /// The outcome <paramref name="state"/>'s objectives give now, or null while the battle goes on. The win is checked
    /// first, so a blow that ends both sides at once wins.
    /// </summary>
    public static BattleOutcome? Decide(BattleState state)
        => Holds(state, state.Objectives.Current.Win) ? BattleOutcome.Won
            : Holds(state, state.Objectives.Current.Lose) ? BattleOutcome.Lost
            : null;

    /// <summary>
    /// Ends the battle if its objectives are decided: sets <see cref="BattleState.Outcome"/>, clears the queue (the turn
    /// hand-off included) and any pending decision, and emits <see cref="BattleEnded"/>. Returns true once the battle is
    /// over, whether it ended now or before; it only ever ends once.
    /// </summary>
    internal static bool Check(BattleWriter w)
    {
        if (w.State.Outcome is not null)
            return true;
        if (Decide(w.State) is not { } outcome)
            return false;
        w.State = w.State with { Outcome = outcome, Queue = [], Pending = null };
        w.Emit(new BattleEnded(outcome));
        return true;
    }

    private static bool Counts(UnitState unit, Side side) => unit.Sheet.Side == side && unit.SummonedBy is null;
}

public static partial class Battle
{
    /// <summary>
    /// What the campaign takes from an ended battle (spec §2.5, §3.9), or null while it goes on:
    /// <list type="bullet">
    /// <item>the encounter's full XP (<see cref="BattleState.Xp"/>) for every party member, AI-played or not, whatever the
    /// outcome (summons and protected NPCs aren't party members);</item>
    /// <item>the injuries and items used, grouped by unit in the order they happened;</item>
    /// <item>the loot tables to roll, on a win only, and the story flags set.</item>
    /// </list>
    /// </summary>
    public static BattleResult? Result(BattleState state, RulesPack rules)
    {
        if (state.Outcome is not { } outcome)
            return null;

        var xp = state.Units.Values
            .Where(u => u.Sheet.Side == Side.Party && u.SummonedBy is null && !u.Protected)
            .ToImmutableSortedDictionary(u => u.Id, _ => state.Xp);
        var injuries = state.Injuries.GroupBy(i => i.Unit)
            .ToImmutableSortedDictionary(g => g.Key, g => g.Select(i => i.Injury).ToImmutableArray());
        var items = state.ItemsUsed.GroupBy(i => i.Unit)
            .ToImmutableSortedDictionary(g => g.Key, g => g.ToImmutableArray());
        return new BattleResult(outcome, xp, injuries, items, outcome == BattleOutcome.Won ? state.Loot : [], [.. state.Flags]);
    }
}
