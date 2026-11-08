using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    /// <summary>
    /// What <paramref name="outcome"/> on <paramref name="target"/> is worth to <paramref name="user"/> playing
    /// <paramref name="p"/> (spec §5.2, §5.4), in HP-like points:
    /// <list type="bullet">
    /// <item>A hostile: damage (capped at what it has left, more for a hurt one with focus fire), the chance it drops times
    /// what it is worth (<see cref="KillWorth"/>), and control (a lost turn costs it its damage per round). Hurting a Downed
    /// one ticks its countdown: a cruel profile gains the chance of a hit times its worth over the countdown left; any other
    /// loses as much (fair play, spec §5.4, #110), so an area that would catch it looks for another aim.</item>
    /// <item>An ally (or the user): healing (double below half HP), getting up from Downed, buffs, a ward that stands it back
    /// up when Downed (worth getting it up, times the chance it drops next round: <see cref="DropChance"/>); hurting it costs
    /// one and a half times what hurting a hostile is worth.</item>
    /// <item>A neutral: hurting it costs a little. A unit the objectives are about (<see cref="IsObjective"/>: a protected
    /// unit, a hostile the party must defeat, a party unit it must protect) counts the objective weight on top.</item>
    /// </list>
    /// </summary>
    public double Worth(UnitState user, UnitState target, Outcome outcome, AiProfile p)
        => TargetWorth(user, target, outcome, p) + (outcome.SelfHeal > 0 || outcome.SelfTempHp > 0
            ? TargetWorth(user, user, new Outcome(DamageDist.Zero, Heal: outcome.SelfHeal, TempHp: outcome.SelfTempHp), p) : 0);

    private double TargetWorth(UnitState user, UnitState target, Outcome outcome, AiProfile p)
    {
        var hp = target.Hp + target.TempHp;
        var maxHp = Math.Max(1, target.Sheet.MaxHp);
        double value;
        if (target.Sheet.Side == Side.Neutral || user.Sheet.Side == Side.Neutral && target.Id != user.Id)
            value = -0.5 * outcome.Damage.Mean;
        else if (Hostile(user, target))
        {
            if (target.Life == LifeState.Downed)
                return (p.Cruel ? 1 : -1) * p.Kill * outcome.Damage.AtLeast(1) * KillWorth(target) / Math.Max(1, target.Countdown);
            var pKill = hp > 0 ? outcome.Damage.AtLeast(hp) : 1;
            var focus = 1 + p.FocusFire * (1 - (double)target.Hp / maxHp);
            value = p.Damage * outcome.Damage.MeanCapped(hp) * focus + p.Kill * pKill * KillWorth(target)
                    + p.Damage * outcome.Splash
                    + (1 - pKill) * p.Control * (-outcome.Offense * Math.Max(1, Dpr(target)) - outcome.Defense * Incoming(target)
                                                 + 0.3 * outcome.Push)
                    - p.Damage * Math.Min(outcome.Heal, target.Sheet.MaxHp - target.Hp);
        }
        else if (target.Life == LifeState.Downed)
        {
            value = p.Revive * outcome.Revive * KillWorth(target) + p.Heal * 0.5 * Math.Min(outcome.Heal, maxHp)
                    - 0.5 * outcome.Damage.AtLeast(1) * KillWorth(target);
        }
        else
        {
            var urgency = target.Hp * 2 < maxHp ? 2 : 1;
            var pKill = hp > 0 ? outcome.Damage.AtLeast(hp) : 0;
            var restored = Math.Max(0, Math.Min(outcome.Heal, target.Sheet.MaxHp - target.Hp));
            var drop = DropChance(target);
            var afterHealing = restored > 0
                ? Math.Clamp(ThreatAgainst(target.Sheet.Side).At(target.Pos) / Math.Max(1, hp + restored), 0, 1)
                : drop;
            // Healing uses its mean amount: discount the uncertain survival gain against a guaranteed ward.
            const double healingSurvivalConfidence = 0.8;
            var preserved = healingSurvivalConfidence * Math.Max(0, drop - afterHealing);
            // Preventing a Down preserves the ally's turn, in addition to restoring HP. A ward covers the risk left after healing.
            value = p.Heal * urgency * restored
                    + p.Heal * 0.5 * Math.Max(0, outcome.TempHp - target.TempHp)
                    + p.Revive * (preserved + outcome.Ward * afterHealing) * KillWorth(target)
                    + p.Control * (outcome.Offense * Math.Max(2, Dpr(target)) + outcome.Defense * Incoming(target) - 0.3 * outcome.Push)
                    - 1.5 * (p.Damage * (outcome.Damage.MeanCapped(hp) + outcome.Splash) + p.Kill * pKill * KillWorth(target));
        }
        return IsObjective(user, target) ? value * (1 + p.Objective) : value;
    }

    /// <summary>What taking a unit out of the fight is worth: two rounds of its damage, plus a little for any unit.</summary>
    public double KillWorth(UnitState unit) => 2 * Dpr(unit) + 2;

    /// <summary>
    /// The damage the unit's foes are expected to deal to it next round: half the threat map against its side on its tile
    /// (not every foe that could strike it will).
    /// </summary>
    public double Incoming(UnitState unit) => 0.5 * ThreatAgainst(unit.Sheet.Side).At(unit.Pos);

    /// <summary>The threat map against <paramref name="side"/>, built once per plan.</summary>
    public ThreatMap ThreatAgainst(Side side)
    {
        if (!_threat.TryGetValue(side, out var map))
            _threat[side] = map = ThreatMap.Build(this, side, Victim(side));
        return map;
    }

    /// <summary>
    /// A tile block's worth at the aim to <paramref name="p"/> (for the planner's actions and for a spell being countered): a
    /// zone's turn-start and enter effects on the units in it now (enter effects at half, as units already there don't
    /// enter it), a difficult zone slowing foes in it, a hazard's enter effects at half, and a summon's damage per round for
    /// two rounds per unit summoned.
    /// </summary>
    public double TileWorth(EffectDef effect, Strike st, Tile aim, AiProfile p)
    {
        switch (effect)
        {
            case ZoneEffect zone:
                {
                    var tiles = Areas.Tiles(zone.Area, st.From, aim, Terrain).ToImmutableSortedSet();
                    var rounds = Math.Min(3, zone.Rounds ?? 3);
                    var value = 0.0;
                    foreach (var unit in st.W.State.Units.Values.Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos)))
                    {
                        foreach (var passive in zone.Passives.IsDefault ? [] : zone.Passives)
                        {
                            if (!ZoneRules.Affects(zone.Affects, st.User.Sheet.Side, unit.Sheet.Side))
                                continue;
                            var weight = passive.Hook switch
                            {
                                HookKind.OnTurnStart => rounds,
                                HookKind.OnEnterTile => 0.5,
                                _ => 0,
                            };
                            if (weight > 0 && passive.Target == PassiveTarget.Self && !passive.Effects.IsDefaultOrEmpty)
                                value += weight * Worth(st.User, unit, Walk(passive.Effects, st, unit), p);
                        }
                        if (zone.Difficult && Hostile(st.User, unit) && unit.Life == LifeState.Up)
                            value += 0.3 * p.Control;
                    }
                    return value;
                }
            case HazardEffect hazard:
                {
                    var tiles = Areas.Tiles(hazard.Area, st.From, aim, Terrain).ToImmutableSortedSet();
                    return st.W.State.Units.Values.Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos))
                        .Sum(u => 0.5 * Worth(st.User, u, Walk(hazard.OnEnter, st, u), p));
                }
            case SummonEffect summon:
                return Rules.MonsterSheet(summon.Monster) is { } sheet
                    ? summon.Count * 2 * (1 + sheet.Attacks * Math.Max(0, sheet.Weapon.Damage.Average)) * 0.5 * p.Damage
                    : 0;
            default:
                return 0;
        }
    }
}
