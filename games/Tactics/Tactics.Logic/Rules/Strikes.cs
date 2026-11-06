using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// The attacks of a weapon attack (<see cref="AbilityDef.IsWeaponAttack"/>, #107): each of a unit's
/// <see cref="UnitSheet.Attacks"/> attacks is one <see cref="StrikeStep"/>, with its own target, and a
/// <see cref="AbilityDef.RiderOnce"/> ability's extras land on its first hit only.
/// </summary>
public static class Strikes
{
    /// <summary>
    /// True when <paramref name="ability"/>'s attacks each choose a target from a list (<see cref="Use.Targets"/>): a weapon
    /// attack aimed at one foe. An area weapon attack (Cleave) strikes every unit in its area instead.
    /// </summary>
    public static bool Aimed(AbilityDef ability)
        => ability.IsWeaponAttack && ability.Target.Area.Shape == AreaShape.Single
           && ability.Target.Kind is TargetKind.Enemy or TargetKind.AnyUnit;

    /// <summary>
    /// What a <see cref="AbilityDef.RiderOnce"/> ability's attacks do once its rider has landed: its first attack roll with
    /// no bonus, whose hit deals its first damage block (for weapon damage, the weapon's dice and stat alone, without the
    /// ability's extra dice) and nothing else: no save, condition, push, crit or miss branch, and none of the ability's
    /// other blocks. Empty when the ability makes no attack roll.
    /// </summary>
    public static ImmutableArray<EffectDef> Plain(AbilityDef ability)
    {
        if (EffectTree.All(ability.Effects).OfType<AttackEffect>().FirstOrDefault() is not { } attack)
            return [];
        var damage = (attack.OnHit.IsDefault ? [] : attack.OnHit).OfType<DamageEffect>().FirstOrDefault();
        ImmutableArray<EffectDef> hit = damage is null ? []
            : [damage.Weapon ? damage with { Dice = null, Bonus = [] } : damage];
        return [attack with { Bonus = 0, OnHit = hit, OnMiss = [], OnCrit = [] }];
    }

    /// <summary>
    /// The effects of each of <paramref name="attacks"/> attacks of <paramref name="ability"/> on one target, in order, as if
    /// the first one hit: the whole list, then (with <see cref="AbilityDef.RiderOnce"/>) <see cref="Plain"/> for the rest.
    /// For previews that walk a list; the rules decide per attack whether the rider has landed.
    /// </summary>
    public static ImmutableArray<EffectDef> Sequence(AbilityDef ability, int attacks)
    {
        var full = ability.Effects.IsDefault ? [] : ability.Effects;
        if (!ability.IsWeaponAttack)
            return full;
        var rest = ability.RiderOnce ? Plain(ability) : full;
        return [.. full, .. Enumerable.Repeat(rest, Math.Max(1, attacks) - 1).SelectMany(e => e)];
    }

    /// <summary>
    /// The attacks of <paramref name="user"/>'s weapon attack as queued by <see cref="Battle"/>: for an aimed ability
    /// (<see cref="Aimed"/>) one chain of <paramref name="count"/> attacks over <paramref name="targets"/> (padded with its last
    /// entry); for an area, a chain of fixed attacks for each unit in it, in sorted order.
    /// </summary>
    internal static IEnumerable<StrikeStep> Plan(UnitState user, AbilityDef ability, Tile aim, ImmutableArray<UnitId> targets, int count)
    {
        if (Aimed(ability))
        {
            if (!targets.IsEmpty)
                yield return new StrikeStep(user.Id, ability.Id, aim, [.. targets, .. Enumerable.Repeat(targets[^1], Math.Max(0, count - targets.Length))],
                    0, count);
            yield break;
        }
        foreach (var target in targets)
            yield return new StrikeStep(user.Id, ability.Id, aim, [target], 0, count, Fixed: true);
    }

    /// <summary>
    /// Whom attack <paramref name="index"/> of an aimed weapon attack goes to: its listed target if it can still be attacked
    /// (in the battle, a valid target and in reach: <see cref="Battle.ResolveTarget"/>; a listed Downed unit counts, as
    /// the player or AI chose it), else the next listed one that can, in list order and round to the start; else the
    /// standing (Up) hostile in reach with the least HP, ties by id, never a Downed one; else nobody, and the attack is spent.
    /// </summary>
    internal static UnitState? Pick(BattleWriter w, UnitState user, AbilityDef ability, ImmutableArray<UnitId> targets, int index)
    {
        var listed = Enumerable.Range(0, targets.Length).Select(i => targets[(index + i) % targets.Length]).Distinct();
        foreach (var id in listed)
            if (w.State.Units.TryGetValue(id, out var unit) && Attackable(w, user, ability, unit))
                return unit;
        return w.State.Units.Values
            .Where(u => u.Life == LifeState.Up && u.Sheet.Side != user.Sheet.Side && u.Sheet.Side != Side.Neutral
                        && Attackable(w, user, ability, u))
            .OrderBy(u => u.Hp).ThenBy(u => u.Id)
            .FirstOrDefault();
    }

    private static bool Attackable(BattleWriter w, UnitState user, AbilityDef ability, UnitState unit)
        => DownedRules.InBattle(unit)
           && Battle.ResolveTarget(w, user, ability, new Target(Unit: unit.Id), out _, out var affected) is null
           && affected.Contains(unit.Id);

    /// <summary>
    /// An attack of <paramref name="ability"/> by <paramref name="source"/> hit: for a <see cref="AbilityDef.RiderOnce"/>
    /// ability its rider has landed, so its attacks still queued (<see cref="StrikeStep.Landed"/>) are plain from now on.
    /// </summary>
    internal static void RecordHit(BattleWriter w, UnitId source, AbilityId? ability)
    {
        if (ability is not { } id || !w.Rules.Abilities.TryGetValue(id, out var def) || !def.RiderOnce)
            return;
        var queue = w.State.Queue;
        for (var i = 0; i < queue.Count; i++)
            if (queue[i] is StrikeStep { Landed: false } strike && strike.Source == source && strike.Ability == id)
                queue = queue.SetItem(i, strike with { Landed = true });
        w.State = w.State with { Queue = queue };
    }
}

/// <summary>
/// Attack <see cref="Index"/> (from 0) of <see cref="Count"/> of a weapon attack by <see cref="Source"/>: it picks its target
/// (<see cref="Targets"/>[<see cref="Index"/>], or another when that one can't be attacked any more: <see cref="Strikes.Pick"/>),
/// queues the ability's effects on it (just <see cref="Strikes.Plain"/> once a <see cref="AbilityDef.RiderOnce"/> rider has
/// <see cref="Landed"/>), then the next attack. A <see cref="Fixed"/> attack (one of an area's, on each unit in it) keeps
/// its one target and does nothing once that unit has left the battle. Nothing happens once the source is no longer Up.
/// <see cref="Retargeted"/> is emitted when an aimed attack's target isn't the one the attack before it went to
/// (<see cref="Last"/>; for the first, the command's target), so the TV can turn to it. Tile blocks run at the target's
/// tile (an area's at its aim), with no target unit.
/// </summary>
public sealed record StrikeStep(UnitId Source, AbilityId Ability, Tile Aim, ImmutableArray<UnitId> Targets, int Index, int Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] UnitId? Last = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Landed = false,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Fixed = false) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var user = w.Unit(Source);
        if (user.Life != LifeState.Up || Index >= Count || Targets.IsDefaultOrEmpty
            || !rules.Abilities.TryGetValue(Ability, out var ability))
            return;

        UnitState? target;
        if (Fixed)
            target = w.Unit(Targets[Math.Min(Index, Targets.Length - 1)]) is var unit && DownedRules.InBattle(unit) ? unit : null;
        else
        {
            target = Strikes.Pick(w, user, ability, Targets, Index);
            if (target is not null && target.Id != (Last ?? Targets[0]))
                w.Emit(new Retargeted(Source, target.Id));
        }

        var steps = new List<Step>();
        if (target is not null)
        {
            var effects = Landed && ability.RiderOnce ? Strikes.Plain(ability) : ability.Effects.IsDefault ? [] : ability.Effects;
            var aim = Fixed ? Aim : target.Pos;
            for (var start = 0; start < effects.Length;)
            {
                var onTile = effects[start].OnTile;
                var end = start;
                while (end < effects.Length && effects[end].OnTile == onTile)
                    end++;
                steps.Add(new RunEffectsStep(Source, onTile ? null : target.Id, aim, Ability, effects[start..end]));
                start = end;
            }
        }
        if (Index + 1 < Count)
            steps.Add(this with { Index = Index + 1, Last = Fixed ? null : target?.Id ?? Last });
        if (steps.Count > 0)
            w.PushFront([.. steps]);
    }
}
