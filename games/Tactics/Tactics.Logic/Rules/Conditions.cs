using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Conditions on units (spec §3.7): what they imply, how they end (duration, save, concentration) and concentration
/// itself (spec §3.1: one effect per caster, a CON save on damage).
/// </summary>
public static class Conditions
{
    /// <summary>
    /// The definitions of <paramref name="unit"/>'s conditions in the order they were applied, each followed by the chain of
    /// conditions it implies, each id once. Ids the rules pack doesn't define are skipped.
    /// </summary>
    public static IEnumerable<ConditionDef> Defs(UnitState unit, RulesPack rules) => Active(unit, rules).Select(a => a.Def);

    /// <summary>Like <see cref="Defs"/>, with the condition on the unit that each definition comes from (an implied
    /// condition comes from the one implying it).</summary>
    internal static IEnumerable<(ConditionDef Def, ActiveCondition From)> Active(UnitState unit, RulesPack rules)
    {
        var seen = new List<ConditionId>();
        foreach (var active in unit.Conditions.IsDefault ? [] : unit.Conditions)
        {
            ConditionId? next = active.Id;
            while (next is { } id && !seen.Contains(id) && rules.Conditions.TryGetValue(id, out var def))
            {
                seen.Add(id);
                yield return (def, active);
                next = def.Implies;
            }
        }
    }

    /// <summary>True when <paramref name="unit"/> has the condition, directly or implied by another.</summary>
    public static bool Has(UnitState unit, ConditionId condition, RulesPack rules)
        => (!unit.Conditions.IsDefault && unit.Conditions.Any(c => c.Id == condition)) || Defs(unit, rules).Any(d => d.Id == condition);

    /// <summary>Ends <paramref name="condition"/> on <paramref name="unit"/>, emitting <see cref="ConditionRemoved"/> if it had it.</summary>
    public static void Remove(BattleWriter w, UnitId unit, ConditionId condition)
    {
        var u = w.Unit(unit);
        var kept = u.Conditions.RemoveAll(c => c.Id == condition);
        if (kept.Length == u.Conditions.Length)
            return;
        w.Set(u with { Conditions = kept });
        w.Emit(new ConditionRemoved(unit, condition));
    }

    /// <summary>The concentration save DC for <paramref name="damage"/>: max(10, ⌊damage ÷ 2⌋).</summary>
    public static int ConcentrationDc(int damage) => Math.Max(10, damage / 2);

    /// <summary>
    /// Ends <paramref name="caster"/>'s concentration, if any: emits <see cref="ConcentrationBroken"/>, then removes every
    /// concentration condition it applied (units in sorted <see cref="UnitId"/> order, <see cref="ConditionRemoved"/> each)
    /// and its concentration zones.
    /// </summary>
    public static void BreakConcentration(BattleWriter w, UnitId caster)
    {
        var unit = w.Unit(caster);
        if (unit.Concentrating is not { } ability)
            return;
        w.Set(unit with { Concentrating = null });
        w.Emit(new ConcentrationBroken(caster, ability));

        foreach (var id in w.State.Units.Keys)
            foreach (var condition in w.Unit(id).Conditions.Where(c => c.Concentration && c.Source == caster).Select(c => c.Id).ToArray())
                Remove(w, id, condition);
        // Task 10 emits ZoneEnded for these.
        w.State = w.State with { Zones = w.State.Zones.RemoveAll(z => z.Concentration && z.Owner == caster) };
    }

    /// <summary>
    /// After <paramref name="damage"/> (more than 0) to <paramref name="unit"/>: if it is concentrating, it rolls a CON save
    /// against <see cref="ConcentrationDc"/>, and a failure breaks its concentration.
    /// </summary>
    internal static void CheckConcentration(BattleWriter w, UnitId unit, int damage, UnitId? source, RulesPack rules)
    {
        if (damage <= 0 || w.Unit(unit).Concentrating is null)
            return;
        var save = Rolls.Save(w, unit, Stat.Con, ConcentrationDc(damage), rules, source: source);
        if (!save.Success)
            BreakConcentration(w, unit);
    }

    /// <summary>
    /// The end of <paramref name="unit"/>'s turn (after its turn-end hooks):
    /// <list type="number">
    /// <item>each of its until-save conditions rolls its save; a success emits <see cref="ConditionSaved"/> and ends it;</item>
    /// <item>every condition whose source is <paramref name="unit"/>, and every condition on <paramref name="unit"/> with no
    /// source (or a source that has left the battle), ticks: one at 0 rounds ends; one applied this turn
    /// (<see cref="ActiveCondition.AppliedThisTurn"/>) only loses that mark; any other loses a round and ends at 0. So
    /// <c>rounds: 0</c> lasts until the end of the source's current turn, and <c>rounds: 1</c> until the end of its next
    /// one. Units go in sorted <see cref="UnitId"/> order, conditions in the order applied.</item>
    /// </list>
    /// </summary>
    internal static void EndTurn(BattleWriter w, UnitId unit, RulesPack rules)
    {
        foreach (var condition in w.Unit(unit).Conditions.Where(c => c.UntilSave).ToArray())
        {
            var save = Rolls.Save(w, unit, condition.SaveStat, condition.SaveDc, rules, source: condition.Source);
            if (!save.Success)
                continue;
            w.Emit(new ConditionSaved(unit, condition.Id, save));
            Remove(w, unit, condition.Id);
        }

        foreach (var id in w.State.Units.Keys)
        {
            var u = w.Unit(id);
            if (!u.Conditions.Any(c => c.RoundsLeft is not null && TickedBy(w.State, id, c) == unit))
                continue;
            var ticked = ImmutableArray.CreateBuilder<ActiveCondition>();
            var expired = new List<ConditionId>();
            foreach (var c in u.Conditions)
            {
                if (c.RoundsLeft is not { } left || TickedBy(w.State, id, c) != unit)
                    ticked.Add(c);
                else if (left <= 0 || !c.AppliedThisTurn && left == 1)
                {
                    ticked.Add(c with { RoundsLeft = 0, AppliedThisTurn = false });
                    expired.Add(c.Id);
                }
                else
                    ticked.Add(c.AppliedThisTurn ? c with { AppliedThisTurn = false } : c with { RoundsLeft = left - 1 });
            }
            w.Set(u with { Conditions = ticked.ToImmutable() });
            foreach (var condition in expired)
                Remove(w, id, condition);
        }
    }

    /// <summary>Whose turn end ticks <paramref name="condition"/> on <paramref name="unit"/>: its source while that source is
    /// in the battle (Up or Downed), otherwise the unit itself.</summary>
    private static UnitId TickedBy(BattleState state, UnitId unit, ActiveCondition condition)
        => condition.Source is { } source && state.Units.TryGetValue(source, out var s) && s.Life is LifeState.Up or LifeState.Downed
            ? source
            : unit;
}
