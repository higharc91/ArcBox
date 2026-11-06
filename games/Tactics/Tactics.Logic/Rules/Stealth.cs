using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Hiding (#108). A unit is in plain view when a foe (a unit of another side) that can act (<see cref="Modifiers.CanAct"/>:
/// Up and not incapacitated) stands next to it, or has line of sight to it with no cover (<see cref="Sight.Trace"/> from the
/// foe's tile: the map's heights, walls and props, and sight-blocking zones such as smoke; a standing unit is no cover to hide
/// behind). Content asks with the <c>seen</c> test (<see cref="WhenDef.Seen"/>): Hide requires <c>{"seen": false}</c>, and
/// the <c>hiding</c> condition ends when its holder ends its turn with <c>{"seen": true}</c>.
/// </summary>
public static class Stealth
{
    /// <summary>True when <paramref name="unit"/> stands in plain view of a foe in <paramref name="state"/>.</summary>
    public static bool InPlainView(BattleState state, Terrain terrain, UnitId unit, RulesPack rules)
    {
        var me = state.Units[unit];
        var obscured = ZoneRules.Obscured(state);
        foreach (var foe in state.Units.Values)
        {
            if (foe.Sheet.Side == me.Sheet.Side || !Modifiers.CanAct(state, foe.Id, rules))
                continue;
            if (Reach.Distance(foe.Pos, me.Pos) <= 1)
                return true;
            var sight = Sight.Trace(terrain, foe.Pos, me.Pos, ImmutableSortedSet<Tile>.Empty, ranged: false, obscured);
            if (sight.HasLineOfSight && sight.Cover == Cover.None)
                return true;
        }
        return false;
    }
}
