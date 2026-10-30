using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A unit's static modifiers: the <see cref="StaticMods"/> of its sheet passives (in sheet order), of its conditions
/// (in the order applied, each followed by the conditions it implies), those its conditions carry themselves
/// (<see cref="ActiveCondition.Mods"/>) and those of the passives of the zones it stands in, added up. Ids the rules pack
/// doesn't define add nothing.
/// </summary>
public static class Modifiers
{
    /// <summary>The sum of every static modifier on <paramref name="unit"/> (see <see cref="Sum"/>).</summary>
    public static StaticMods For(BattleWriter w, UnitId unit, RulesPack rules) => For(w.State, unit, rules);

    /// <summary>The sum of every static modifier on <paramref name="unit"/> in <paramref name="state"/>.</summary>
    public static StaticMods For(BattleState state, UnitId unit, RulesPack rules) => Sum(Sources(state, unit, rules));

    /// <summary>Each source of static modifiers on <paramref name="unit"/>, one entry per passive or condition that has any.</summary>
    public static IEnumerable<StaticMods> Sources(BattleState state, UnitId unit, RulesPack rules)
    {
        var u = state.Units[unit];
        foreach (var passive in Passives(u, rules))
            if (passive.Static is { } mods)
                yield return mods;
        foreach (var condition in Conditions.Defs(u, rules))
        {
            if (condition.Static is { } mods)
                yield return mods;
            foreach (var passive in condition.Passives.IsDefault ? [] : condition.Passives)
                if (passive.Static is { } passiveMods)
                    yield return passiveMods;
        }
        foreach (var condition in u.Conditions.IsDefault ? [] : u.Conditions)
            if (condition.Mods is { } carried)
                yield return carried;
        foreach (var (passive, _) in ZoneRules.PassivesAt(state, u))
            if (passive.Static is { } zoneMods)
                yield return zoneMods;
    }

    /// <summary>
    /// The passives on <paramref name="unit"/>'s sheet that the rules pack defines (<see cref="RulesPack.PassiveFor"/>: its
    /// own and those made from injuries), in sheet order.
    /// </summary>
    internal static IEnumerable<PassiveDef> Passives(UnitState unit, RulesPack rules)
    {
        foreach (var id in unit.Sheet.Passives.IsDefault ? [] : unit.Sheet.Passives)
            if (rules.PassiveFor(id) is { } passive)
                yield return passive;
    }

    /// <summary>
    /// Adds modifiers up: numbers and per-stat saves add, flags are true when any source sets them, stat and damage-type
    /// sets are unions, and each edge is <see cref="Rolls.Combine"/> of the sources' edges.
    /// </summary>
    public static StaticMods Sum(IEnumerable<StaticMods> sources)
    {
        var (ac, attack, damage, move, initiative, extra, maxHp) = (0, 0, 0, 0, 0, 0, 0);
        var (incapacitated, cantMove, critWhenAdjacent, invisible, evasion) = (false, false, false, false, false);
        var saveEdges = new SortedDictionary<Stat, EdgeCount>();
        var saveProficiencies = ImmutableSortedSet.CreateBuilder<Stat>();
        var saves = ImmutableSortedDictionary.CreateBuilder<Stat, int>();
        var autoFail = ImmutableSortedSet.CreateBuilder<Stat>();
        var resist = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        var immune = ImmutableSortedSet.CreateBuilder<string>(StringComparer.Ordinal);
        var own = new EdgeCount();
        var melee = new EdgeCount();
        var ranged = new EdgeCount();

        foreach (var m in sources)
        {
            ac += m.Ac;
            attack += m.Attack;
            damage += m.Damage;
            move += m.Move;
            initiative += m.Initiative;
            extra += m.ExtraActions;
            maxHp += m.MaxHp;
            incapacitated |= m.Incapacitated;
            cantMove |= m.CantMove;
            critWhenAdjacent |= m.CritWhenAdjacent;
            invisible |= m.Invisible;
            evasion |= m.Evasion;
            foreach (var (stat, edge) in m.SaveEdges ?? ImmutableSortedDictionary<Stat, Edge>.Empty)
            {
                if (!saveEdges.TryGetValue(stat, out var count))
                    saveEdges[stat] = count = new EdgeCount();
                count.Add(edge);
            }
            saveProficiencies.UnionWith(m.SaveProficiencies ?? []);
            foreach (var (stat, bonus) in m.Saves ?? ImmutableSortedDictionary<Stat, int>.Empty)
                saves[stat] = saves.GetValueOrDefault(stat) + bonus;
            autoFail.UnionWith(m.AutoFailSaves ?? []);
            resist.UnionWith(m.Resist ?? Ordinal.Set());
            immune.UnionWith(m.Immune ?? Ordinal.Set());
            own.Add(m.OwnAttacks);
            melee.Add(m.AttackedMelee);
            ranged.Add(m.AttackedRanged);
        }

        return new StaticMods(ac, attack, damage, move, initiative, saves.ToImmutable(), incapacitated, cantMove,
            own.Edge, melee.Edge, ranged.Edge, autoFail.ToImmutable(), critWhenAdjacent, invisible, resist.ToImmutable(),
            immune.ToImmutable(), extra, maxHp,
            saveEdges.Where(e => e.Value.Edge != Edge.None).ToImmutableSortedDictionary(e => e.Key, e => e.Value.Edge),
            saveProficiencies.ToImmutable(), evasion);
    }

    /// <summary>True when <paramref name="unit"/> is Up and not incapacitated, so it can act, react and flank.</summary>
    public static bool CanAct(BattleState state, UnitId unit, RulesPack rules)
        => state.Units[unit].Life == LifeState.Up && !For(state, unit, rules).Incapacitated;

    /// <summary>
    /// True when <paramref name="attacker"/> flanks <paramref name="target"/>: they are 4-adjacent and an ally of the attacker
    /// that can act (<see cref="CanAct"/>) stands on the tile directly opposite (<see cref="Reach.OppositeOf"/>).
    /// </summary>
    public static bool Flanked(BattleState state, UnitId attacker, UnitId target, RulesPack rules)
    {
        var a = state.Units[attacker];
        if (Reach.OppositeOf(a.Pos, state.Units[target].Pos) is not { } opposite)
            return false;
        return state.Units.Values.Any(u => u.Id != attacker && u.Pos == opposite && u.Sheet.Side == a.Sheet.Side
                                           && CanAct(state, u.Id, rules));
    }

    /// <summary>The AC (and DEX save) bonus of cover: half +2, three-quarters +5. Total cover counts as three-quarters
    /// (targeting already needs line of sight).</summary>
    public static int CoverBonus(Cover cover) => cover switch
    {
        Cover.None => 0,
        Cover.Half => 2,
        _ => 5,
    };

    /// <summary>The cover bonus <paramref name="target"/> has against something coming from <paramref name="from"/>
    /// (<see cref="Sight.Trace"/>; with <paramref name="ranged"/>, standing units in the way give half cover).</summary>
    public static int CoverAgainst(BattleWriter w, Tile from, Tile target, bool ranged)
    {
        if (from == target)
            return 0;
        var standing = w.State.Units.Values.Where(u => u.Life == LifeState.Up).Select(u => u.Pos).ToImmutableSortedSet();
        return CoverBonus(Sight.Trace(w.Terrain, from, target, standing, ranged, ZoneRules.Obscured(w.State)).Cover);
    }

    /// <summary>The unit's effective Move: the sheet's plus modifiers, never negative, and 0 when it can't move.</summary>
    public static int Move(UnitSheet sheet, StaticMods mods) => mods.CantMove ? 0 : Math.Max(0, sheet.Move + mods.Move);

    private sealed class EdgeCount
    {
        private int _advantages;
        private int _disadvantages;

        public void Add(Edge edge)
        {
            if (edge == Edge.Advantage)
                _advantages++;
            else if (edge == Edge.Disadvantage)
                _disadvantages++;
        }

        public Edge Edge => Rolls.Combine(_advantages, _disadvantages);
    }
}
