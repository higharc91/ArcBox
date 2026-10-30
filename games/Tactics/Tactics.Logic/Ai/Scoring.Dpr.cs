using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    /// <summary>
    /// The unit's expected damage per round against a typical foe (<see cref="Victim"/>): its best action (an attack or
    /// a damaging ability it can pay for) plus its best bonus action.
    /// </summary>
    public double Dpr(UnitState unit)
    {
        if (_dpr.TryGetValue(unit.Id, out var dpr))
            return dpr;
        var victim = Victim(Foes(unit.Sheet.Side));
        var options = victim is null ? [] : Options(unit, victim);
        dpr = options.Where(o => !o.Bonus).Select(o => o.Ev).DefaultIfEmpty(0).Max()
              + options.Where(o => o.Bonus).Select(o => o.Ev).DefaultIfEmpty(0).Max();
        return _dpr[unit.Id] = dpr;
    }

    /// <summary>The side a unit's foes are on (neutrals count the party as their foes).</summary>
    private static Side Foes(Side side) => side == Side.Party ? Side.Enemy : Side.Party;

    /// <summary>
    /// A typical unit of <paramref name="side"/> still in the battle: the one with the middle AC (ties by id), or null when
    /// there is none.
    /// </summary>
    public UnitState? Victim(Side side)
    {
        var units = State.Units.Values.Where(u => u.Sheet.Side == side && DownedRules.InBattle(u))
            .OrderBy(u => u.Sheet.Ac + Mods(u).Ac).ThenBy(u => u.Id).ToArray();
        return units.Length == 0 ? null : units[units.Length / 2];
    }

    /// <summary>
    /// The ways <paramref name="attacker"/> can hurt <paramref name="victim"/> on its turn, ignoring the map: the Attack
    /// action and every damaging ability it knows and can pay for, each with its expected damage and its reach.
    /// </summary>
    public ImmutableArray<ThreatOption> Options(UnitState attacker, UnitState victim)
    {
        if (_options.TryGetValue((attacker.Id, victim.Id), out var found))
            return found;
        var options = ImmutableArray.CreateBuilder<ThreatOption>();
        var fresh = attacker with { Economy = Economy.Fresh(attacker.Sheet, Mods(attacker)) };
        var weapon = attacker.Sheet.Weapon;
        if (Rules.Abilities.TryGetValue(RulesPack.CommonAttack.Id, out var attack))
        {
            var ev = Expected(fresh, attack, victim, Math.Max(1, attacker.Sheet.Attacks));
            options.Add(new ThreatOption(ev, weapon.Ranged ? weapon.Range : 1, Melee: !weapon.Ranged, weapon.Reach, Bonus: false,
                AttackModifier(fresh, attack)));
        }
        foreach (var id in attacker.Sheet.Abilities)
        {
            if (!Rules.Abilities.TryGetValue(id, out var ability) || ItemAbilities.Contains(id) || ability.Cost.Reaction > 0
                || ability.Cost.Action == 0 && ability.Cost.Bonus == 0 || !Deals(ability.Effects)
                || ability.Target.Kind is not (TargetKind.Enemy or TargetKind.AnyUnit or TargetKind.Tile or TargetKind.Self)
                || Battle.CostRefusal(fresh, ability) is not null)
                continue;
            var self = ability.Target.Kind == TargetKind.Self;
            var range = self ? Math.Max(1, ability.Target.Area.Size) : Math.Max(1, ability.Target.Range);
            var ev = Expected(fresh, ability, victim, ability.IsWeaponAttack ? Math.Max(1, attacker.Sheet.Attacks) : 1);
            options.Add(new ThreatOption(ev, range, Melee: range <= 1 && !self, Reach: false, Bonus: ability.Cost.Action == 0,
                AttackModifier(fresh, ability)));
        }
        return _options[(attacker.Id, victim.Id)] = options.ToImmutable();
    }

    /// <summary>The expected damage of <paramref name="ability"/> (its effects <paramref name="times"/> over) on <paramref name="victim"/>.</summary>
    private double Expected(UnitState attacker, AbilityDef ability, UnitState victim, int times)
    {
        var st = new Strike(W, attacker, ability, victim.Pos, neutral: true);
        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        var total = Outcome.None;
        for (var i = 0; i < times; i++)
            total = total.Then(Walk(effects, st, victim));
        return total.Damage.Mean + total.Splash;
    }

    /// <summary>The attack roll modifier of the ability's first attack, if it makes one (to rescale its odds by AC and cover).</summary>
    private static int? AttackModifier(UnitState attacker, AbilityDef ability)
    {
        if (FirstAttack(ability.Effects) is not { } def)
            return null;
        var sheet = attacker.Sheet;
        var stat = def.Stat is { } s ? Stats.Mod(sheet.Score(s))
            : def.Kind == "weapon" ? Stats.Mod(sheet.Score(AttackBlock.WeaponStat(sheet)))
            : EffectContext.CastingModifierOf(sheet);
        return stat + Stats.Proficiency(sheet.Level) + def.Bonus;
    }
}

/// <summary>
/// One way a unit can hurt a foe next turn: the expected damage, its reach in tiles (<see cref="Melee"/>: weapon reach, with
/// a <see cref="Reach"/> weapon two in a line), whether it takes the bonus action, and the attack roll's modifier if it
/// makes one.
/// </summary>
internal sealed record ThreatOption(double Ev, int Range, bool Melee, bool Reach, bool Bonus, int? AttackModifier);
