using System.Collections.Immutable;
using System.Globalization;

namespace Tactics.Logic;

/// <summary>
/// The battle's win and lose conditions (spec §3.9, §4.8): <c>win</c> and <c>lose</c> expressions of leaves combined
/// with <c>any</c>/<c>all</c>. The leaves:
/// <list type="bullet">
/// <item><c>defeat_all</c>: no enemy is in the battle (Up or Downed). <c>party_out</c>: no party member is Up.</item>
/// <item><c>defeat:&lt;tag&gt;</c>: at least one unit with the tag has been in the battle, and none is now.</item>
/// <item><c>survive:&lt;n&gt;</c>: round n is over (it holds from the start of round n + 1).</item>
/// <item><c>reach:&lt;zone&gt;:&lt;k&gt;</c>: at least k standing party members are on the map zone's tiles.</item>
/// <item><c>protect:&lt;tag&gt;</c>: at least one unit with the tag exists and every one is still in the battle.</item>
/// <item><c>flag:&lt;name&gt;</c>: the story flag is set.</item>
/// </list>
/// Summons don't count for either side. Tags are matched by <see cref="HasTag"/>. On top of the lose expression, a
/// protected NPC on the party's side that is Out or Dead loses the battle. An unknown leaf never holds (the loader
/// rejects them).
/// </summary>
public static class Objectives
{
    /// <summary>The leaf that holds once every enemy has been defeated.</summary>
    public const string DefeatAll = "defeat_all";

    /// <summary>The leaf that holds once no party member is left standing.</summary>
    public const string PartyOut = "party_out";

    /// <summary>The tag every party member has (not summons or protected NPCs).</summary>
    public const string PartyTag = "party";

    /// <summary>The tag every enemy has (not summons).</summary>
    public const string EnemyTag = "enemy";

    /// <summary>
    /// Whether <paramref name="expr"/> holds in <paramref name="state"/>: a leaf by its rule, <c>any</c> when one entry holds,
    /// <c>all</c> when every entry holds. <paramref name="map"/> (the base map) is needed for <c>reach</c>; without it the
    /// rules pack isn't consulted and <c>reach</c> never holds.
    /// </summary>
    public static bool Holds(BattleState state, ObjectiveExpr expr, BattleMap? map = null)
    {
        if (expr.Leaf is { } leaf)
            return Leaf(state, leaf, map);
        if (!expr.Any.IsDefaultOrEmpty)
            return expr.Any.Any(e => Holds(state, e, map));
        return !expr.All.IsDefaultOrEmpty && expr.All.All(e => Holds(state, e, map));
    }

    /// <summary>Whether one leaf (see <see cref="Objectives"/>) holds.</summary>
    public static bool Leaf(BattleState state, string leaf, BattleMap? map = null)
    {
        switch (leaf.Split(':'))
        {
            case [DefeatAll]:
                return !state.Units.Values.Any(u => Counts(u, Side.Enemy) && DownedRules.InBattle(u));
            case [PartyOut]:
                return !state.Units.Values.Any(u => Counts(u, Side.Party) && !u.Protected && u.Life == LifeState.Up);
            case ["defeat", var tag]:
            {
                var tagged = state.Units.Values.Where(u => HasTag(u, tag)).ToArray();
                return tagged.Length > 0 && !tagged.Any(DownedRules.InBattle);
            }
            case ["protect", var tag]:
            {
                var tagged = state.Units.Values.Where(u => HasTag(u, tag)).ToArray();
                return tagged.Length > 0 && tagged.All(DownedRules.InBattle);
            }
            case ["survive", var rounds]:
                return int.TryParse(rounds, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && state.Round > n;
            case ["reach", var zone, var count]:
                if (map is null || !map.Zones.TryGetValue(zone, out var tiles) || !int.TryParse(count, NumberStyles.None, CultureInfo.InvariantCulture, out var k))
                    return false;
                return state.Units.Values.Count(u => Counts(u, Side.Party) && !u.Protected && u.Life == LifeState.Up
                                                     && tiles.Contains(u.Pos)) >= k;
            case ["flag", var name]:
                return state.Flags.Contains(name);
            default:
                return false;
        }
    }

    /// <summary>
    /// Whether <paramref name="unit"/> carries <paramref name="tag"/>: <see cref="PartyTag"/> for party members (not summons,
    /// helpers or protected NPCs), <see cref="EnemyTag"/> for enemies (not summons), otherwise one of its own sheet's tags,
    /// whatever form it is in.
    /// </summary>
    public static bool HasTag(UnitState unit, string tag) => tag switch
    {
        PartyTag => Counts(unit, Side.Party) && !unit.Protected,
        EnemyTag => Counts(unit, Side.Enemy),
        _ => (unit.TrueSheet ?? unit.Sheet).Tags.Contains(tag),
    };

    /// <summary>
    /// The outcome <paramref name="state"/>'s objectives give now, or null while the battle goes on. The win is checked
    /// first, so a blow that ends both sides at once wins; a protected NPC of the party's that is Out or Dead loses.
    /// </summary>
    public static BattleOutcome? Decide(BattleState state, BattleMap? map = null)
        => Holds(state, state.Objectives.Current.Win, map) ? BattleOutcome.Won
            : Holds(state, state.Objectives.Current.Lose, map) || ProtectedLost(state) ? BattleOutcome.Lost
            : null;

    /// <summary>True when a protected NPC on the party's side is Out or Dead.</summary>
    private static bool ProtectedLost(BattleState state)
        => state.Units.Values.Any(u => u.Protected && u.Sheet.Side == Side.Party && u.Life is LifeState.Out or LifeState.Dead);

    /// <summary>
    /// Ends the battle if its objectives are decided (see <see cref="End"/>). Returns true once the battle is over, whether
    /// it ended now or before; it only ever ends once.
    /// </summary>
    internal static bool Check(BattleWriter w)
    {
        if (w.State.Outcome is not null)
            return true;
        if (Decide(w.State, w.Terrain.Map) is not { } outcome)
            return false;
        End(w, outcome);
        return true;
    }

    /// <summary>
    /// Ends the battle with <paramref name="outcome"/>, unless it is already over: sets <see cref="BattleState.Outcome"/>,
    /// clears the queue (the turn hand-off included) and any pending decision, and emits <see cref="BattleEnded"/>.
    /// </summary>
    internal static void End(BattleWriter w, BattleOutcome outcome)
    {
        if (w.State.Outcome is not null)
            return;
        w.State = w.State with { Outcome = outcome, Queue = [], Pending = null };
        w.Emit(new BattleEnded(outcome));
    }

    private static bool Counts(UnitState unit, Side side) => unit.Sheet.Side == side && unit.SummonedBy is null && !unit.Helper;
}

public static partial class Battle
{
    /// <summary>
    /// What the campaign takes from an ended battle (spec §2.5, §3.9), or null while it goes on:
    /// <list type="bullet">
    /// <item>XP for every party member, AI-played or not (summons, helpers and protected NPCs aren't party members): the
    /// encounter's full <see cref="BattleState.Xp"/> on a win, half of it (rounded down) on a loss or a flight;</item>
    /// <item>the injuries and items used, grouped by unit in the order they happened;</item>
    /// <item>the loot tables to roll, on a win only, and the story flags set.</item>
    /// </list>
    /// </summary>
    public static BattleResult? Result(BattleState state, RulesPack rules)
    {
        if (state.Outcome is not { } outcome)
            return null;

        var award = outcome == BattleOutcome.Won ? state.Xp : state.Xp / 2;
        var xp = state.Units.Values
            .Where(u => Objectives.HasTag(u, Objectives.PartyTag))
            .ToImmutableSortedDictionary(u => u.Id, _ => award);
        var injuries = state.Injuries.GroupBy(i => i.Unit)
            .ToImmutableSortedDictionary(g => g.Key, g => g.Select(i => i.Injury).ToImmutableArray());
        var items = state.ItemsUsed.GroupBy(i => i.Unit)
            .ToImmutableSortedDictionary(g => g.Key, g => g.ToImmutableArray());
        return new BattleResult(outcome, xp, injuries, items, outcome == BattleOutcome.Won ? state.Loot : [], [.. state.Flags]);
    }
}
