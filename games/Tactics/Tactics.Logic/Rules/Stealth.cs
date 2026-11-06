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
    /// <summary>
    /// True when <paramref name="unit"/> stands in plain view of a foe in <paramref name="state"/>. The foes that can act
    /// are found once; an adjacent one answers before any line of sight is traced.
    /// </summary>
    public static bool InPlainView(BattleState state, Terrain terrain, UnitId unit, RulesPack rules)
    {
        var me = state.Units[unit];
        var foes = state.Units.Values.Where(u => u.Sheet.Side != me.Sheet.Side && Modifiers.CanAct(state, u.Id, rules)).ToArray();
        if (foes.Any(foe => Reach.Distance(foe.Pos, me.Pos) <= 1))
            return true;
        if (foes.Length == 0)
            return false;
        var obscured = ZoneRules.Obscured(state);
        return foes.Any(foe => Sight.Trace(terrain, foe.Pos, me.Pos, ImmutableSortedSet<Tile>.Empty, ranged: false, obscured)
            is { HasLineOfSight: true, Cover: Cover.None });
    }
}
