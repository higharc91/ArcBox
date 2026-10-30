using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    /// <summary>
    /// What <paramref name="effects"/> are expected to do to <paramref name="target"/> (spec §5.2): attack and save chances
    /// from the exact d20 maths with advantage, cover and high ground, damage from the dice with crits and resistances,
    /// conditions by what their modifiers do. <paramref name="crit"/> and <paramref name="half"/> are the branch it runs in;
    /// <paramref name="last"/> the roll it follows.
    /// </summary>
    public Outcome Walk(ImmutableArray<EffectDef> effects, Strike st, UnitState target, bool crit = false, bool half = false,
        RollResult? last = null)
    {
        var outcome = Outcome.None;
        foreach (var effect in effects.IsDefault ? [] : effects)
            outcome = outcome.Then(One(effect, st, target, crit, half, last));
        return outcome;
    }

    private Outcome One(EffectDef effect, Strike st, UnitState target, bool crit, bool half, RollResult? last)
    {
        switch (effect)
        {
            case AttackEffect attack:
                return Attack(attack, st, target);
            case SaveEffect save:
                return Save(save, st, target);
            case DamageEffect damage:
                return new Outcome(Damage(damage, st, target, crit, half));
            case HealEffect heal:
                {
                    var amount = Math.Max(0, heal.Dice.At(st.Context.Level).Average + st.Context.Sum(heal.Bonus));
                    return new Outcome(DamageDist.Zero, Heal: amount, Revive: target.Life == LifeState.Downed && amount > 0 ? 1 : 0);
                }
            case TempHpEffect temp:
                return new Outcome(DamageDist.Zero,
                    TempHp: Math.Max(0, temp.Dice.At(st.Context.Level).Average + st.Context.Sum(temp.Bonus)));
            case ReviveEffect:
                return target.Life == LifeState.Downed ? new Outcome(DamageDist.Zero, Revive: 1) : Outcome.None;
            case HelpEffect:
                return target.Life == LifeState.Downed ? new Outcome(DamageDist.Zero, Revive: 1)
                    : Has(target, RulesPack.Helped.Id) ? Outcome.None
                    : new Outcome(DamageDist.Zero, Offense: 0.1);
            case ApplyEffect apply:
                {
                    if (Has(target, apply.Condition))
                        return Outcome.None;   // it has it already: nothing gained
                    var benefit = ConditionBenefit(apply.Condition);
                    var turns = Turns(apply.Rounds, apply.UntilSave);
                    return new Outcome(DamageDist.Zero, Offense: benefit.Offense * Math.Min(turns, benefit.OffenseTurns),
                        Defense: benefit.Defense * Math.Min(turns, benefit.DefenseTurns),
                        Splash: Hostile(st.User, target) ? QuarryDamage(apply.Condition, st, target, turns) : 0);
                }
            case StatModEffect statMod:
                {
                    if (st.Ability is { } source && Has(target, new ConditionId("statMod." + source.Id.Value)))
                        return Outcome.None;
                    var (offense, defense) = ModsBenefit(statMod.Mods);
                    var turns = Turns(statMod.Rounds, untilSave: false);
                    return new Outcome(DamageDist.Zero, Offense: offense * turns, Defense: defense * turns);
                }
            case RemoveEffect remove:
                return Removing(target, c => c.Id == remove.Condition);
            case CleanseEffect cleanse:
                return Removing(target, c => Rules.Conditions.TryGetValue(c.Id, out var def) && def.Tags.Overlaps(cleanse.Tags));
            case PushEffect push:
                return new Outcome(DamageDist.Zero, Push: push.Distance);
            case PullEffect pull:
                return new Outcome(DamageDist.Zero, Push: pull.Distance);
            case TauntEffect taunt:
                return new Outcome(DamageDist.Zero, Offense: -0.3 * Turns(taunt.Rounds, untilSave: false));
            case RepeatEffect repeat:
                {
                    var once = Walk(repeat.Effects, st, target, crit, half, last);
                    var total = Outcome.None;
                    for (var i = 0; i < repeat.Times; i++)
                        total = total.Then(once);
                    return total;
                }
            case ChainEffect chain:
                {
                    var first = Walk(chain.Effects, st, target, crit, half, last);
                    return first with { Splash = first.Splash + first.Damage.Mean * chain.Times * 0.5 };
                }
            case IfEffect check:
                {
                    var args = new HookArgs(st.User.Id, target.Id, last, Tile: target.Pos, Ability: st.Ability?.Id);
                    return Walk(Hooks.Holds(check.When, st.W, args, Rules) ? check.Then : check.Else, st, target, crit, half, last);
                }
            case RandomEffect random:
                {
                    var total = random.Table.Sum(e => Math.Max(0, e.Weight));
                    return total <= 0 ? Outcome.None : Outcome.Mix([.. random.Table.Select(e =>
                        ((double)Math.Max(0, e.Weight) / total, Walk(e.Effects, st, target, crit, half, last)))]);
                }
            default:
                return Outcome.None;   // movement, terrain, resources, turn order: no value of their own to the target
        }
    }

    /// <summary>An attack roll (see <see cref="AttackStep"/>): hit, crit and miss branches, with the user's on-hit passives.</summary>
    private Outcome Attack(AttackEffect def, Strike st, UnitState target)
    {
        var user = st.User;
        var sheet = user.Sheet;
        var weapon = def.Kind == "weapon";
        var ranged = weapon ? sheet.Weapon.Ranged : (st.Ability?.Target.Range ?? 1) > 1;
        var statModifier = def.Stat is { } stat ? Stats.Mod(sheet.Score(stat))
            : weapon ? Stats.Mod(sheet.Score(AttackBlock.WeaponStat(sheet)))
            : EffectContext.CastingModifierOf(sheet);
        var userSources = Sources(user.Id, user.Pos);
        var targetSources = Sources(target.Id, target.Pos);
        var modifier = statModifier + Stats.Proficiency(sheet.Level) + def.Bonus + Modifiers.Sum(userSources).Attack
                       + (st.Neutral ? 0 : Reach.HighGroundBonus(Terrain, user.Pos, target.Pos));

        var (advantages, disadvantages) = (0, 0);
        foreach (var mods in userSources)
        {
            advantages += (mods.OwnAttacks == Edge.Advantage ? 1 : 0) + (mods.Invisible ? 1 : 0);
            disadvantages += mods.OwnAttacks == Edge.Disadvantage ? 1 : 0;
        }
        foreach (var mods in targetSources)
        {
            var against = ranged ? mods.AttackedRanged : mods.AttackedMelee;
            advantages += against == Edge.Advantage ? 1 : 0;
            disadvantages += (against == Edge.Disadvantage ? 1 : 0) + (mods.Invisible ? 1 : 0);
        }
        if (!st.Neutral && !ranged && Modifiers.Flanked(st.W.State, user.Id, target.Id, Rules))
            advantages++;
        if (!st.Neutral && TauntBlock.TauntedAway(st.W.State, user.Id, target.Id))
            disadvantages++;
        if (user.Conditions.Any(c => c.Id == RulesPack.Helped.Id))
            advantages++;
        var edge = Rolls.Combine(advantages, disadvantages);

        var cover = !st.Neutral && (ranged || def.Kind == "spell") ? Cover(user.Pos, target.Pos, ranged: true) : 0;
        var targetMods = Modifiers.Sum(targetSources);
        var ac = target.Sheet.Ac + targetMods.Ac + cover;
        var pHit = Odds.Hit(modifier, ac, edge);
        var pCrit = targetMods.CritWhenAdjacent && Reach.Distance(user.Pos, target.Pos) == 1 ? pHit : Odds.Crit(edge);

        var hitRoll = new RollResult([], 0, modifier, 0, ac, Success: true, Crit: false, Fumble: false);
        var critRoll = hitRoll with { Crit = true };
        var hooks = OnHitPassives(def, st, target, edge, hitRoll);
        var onHit = def.OnHit.IsDefault ? [] : def.OnHit;
        var hit = Walk(onHit, st, target, last: hitRoll).Then(Walk(hooks, st, target, last: hitRoll));
        var critical = Walk(def.OnCrit.IsDefaultOrEmpty ? onHit : onHit.AddRange(def.OnCrit), st, target, crit: true, last: critRoll)
            .Then(Walk(hooks, st, target, crit: true, last: critRoll));
        var miss = Walk(def.OnMiss, st, target, last: hitRoll with { Success = false });
        return Outcome.Mix([(pCrit, critical), (pHit - pCrit, hit), (1 - pHit, miss)]);
    }

    /// <summary>
    /// The effects of the user's own passives that would fire on hitting <paramref name="target"/> (sneak attack, divine
    /// strike, …): their <see cref="PassiveDef.When"/> is checked as the engine would (<see cref="Hooks.Holds"/>). A
    /// once-per-turn passive counts on the first attack it could fire on only.
    /// </summary>
    private ImmutableArray<EffectDef> OnHitPassives(AttackEffect def, Strike st, UnitState target, Edge edge, RollResult roll)
    {
        var effects = ImmutableArray.CreateBuilder<EffectDef>();
        foreach (var passive in Modifiers.Passives(st.User, Rules))
        {
            if (passive.Hook != HookKind.OnAttackHit || passive.Effects.IsDefaultOrEmpty || passive.Target != PassiveTarget.Other
                || passive.OncePerTurn && (st.User.UsedThisTurn.Contains(passive.Id) || st.Fired.Contains(passive.Id))
                || passive.UsesPerBattle is { } uses && st.User.UsesLeft.GetValueOrDefault(passive.Id, uses) < 1)
                continue;
            var args = new HookArgs(st.User.Id, target.Id, roll, Tile: target.Pos, Ability: st.Ability?.Id, AttackKind: def.Kind,
                Edge: edge);
            if (passive.When is { } when && !Hooks.Holds(when, st.W, args, Rules))
                continue;
            if (passive.OncePerTurn)
                st.Fired.Add(passive.Id);
            effects.AddRange(passive.Effects);
        }
        return effects.ToImmutable();
    }

    /// <summary>The share of a unit's attacks the quarry estimate expects to hit.</summary>
    private const double QuarryHitShare = 0.6;

    /// <summary>
    /// What marking <paramref name="target"/> with <paramref name="condition"/> for <paramref name="turns"/> of the user's
    /// turns adds to the user's own hits on it (Hunter's Mark, Divine Smite): the damage of each of its on-hit passives that
    /// need the target to have the condition (a <c>targetHasCondition</c> leaf, alone or in an <c>all</c>), times the hits
    /// expected (<see cref="QuarryHitShare"/> of its attacks each turn, one a turn for a once-per-turn passive), at most one
    /// when the passive ends the condition itself. The passive's other tests aren't checked.
    /// </summary>
    private double QuarryDamage(ConditionId condition, Strike st, UnitState target, double turns)
    {
        var total = 0.0;
        foreach (var passive in Modifiers.Passives(st.User, Rules))
        {
            if (passive.Hook != HookKind.OnAttackHit || passive.Target != PassiveTarget.Other || passive.Effects.IsDefaultOrEmpty
                || passive.When is not { } when || !Needs(when, condition))
                continue;
            var perTurn = passive.OncePerTurn ? QuarryHitShare : QuarryHitShare * Math.Max(1, st.User.Sheet.Attacks);
            var hits = perTurn * turns;
            if (All(passive.Effects).Any(e => e is RemoveEffect r && r.Condition == condition))
                hits = Math.Min(1, hits);
            total += hits * Walk(passive.Effects, st, target).Damage.Mean;
        }
        return total;

        static bool Needs(WhenDef when, ConditionId id)
            => when.TargetHasCondition == id || !when.All.IsDefaultOrEmpty && when.All.Any(w => Needs(w, id));
    }

    /// <summary>A saving throw (see <see cref="SaveBlock"/>): fail and success branches, half damage, evasion.</summary>
    private Outcome Save(SaveEffect def, Strike st, UnitState target)
    {
        var sheet = target.Sheet;
        var sources = Sources(target.Id, target.Pos);
        var mods = Modifiers.Sum(sources);
        var dc = def.Dc ?? (def.DcStat is { } dcStat ? st.Context.DcFor(dcStat) : st.Context.SaveDc);
        var proficient = sheet.SaveProficiencies.Contains(def.Stat) || mods.SaveProficiencies?.Contains(def.Stat) == true;
        var origin = st.Ability?.Target.Area.Shape is AreaShape.Burst or AreaShape.Cross or AreaShape.Ring && st.Aim is { } aim
            ? aim
            : st.User.Pos;
        var cover = def.Stat == Stat.Dex && !st.Neutral ? Cover(origin, target.Pos, ranged: false) : 0;
        var bonus = Stats.Mod(sheet.Score(def.Stat)) + (proficient ? Stats.Proficiency(sheet.Level) : 0)
                    + (mods.Saves?.GetValueOrDefault(def.Stat) ?? 0) + cover;
        var advantages = sources.Count(m => m.SaveEdges?.GetValueOrDefault(def.Stat) == Edge.Advantage)
                         + (target.Conditions.Any(c => c.Id == RulesPack.Helped.Id) ? 1 : 0);
        var disadvantages = sources.Count(m => m.SaveEdges?.GetValueOrDefault(def.Stat) == Edge.Disadvantage);
        var pFail = mods.AutoFailSaves?.Contains(def.Stat) == true
            ? 1
            : Odds.SaveFail(bonus, dc, Rolls.Combine(advantages, disadvantages));

        var evades = def.Stat == Stat.Dex && def.HalfOnSuccess && mods.Evasion;
        var failed = new RollResult([], 0, bonus, 0, dc, Success: false, Crit: false, Fumble: false);
        var saved = failed with { Success = true };
        var onFail = Walk(def.OnFail, st, target, half: evades, last: failed);
        var onSuccess = Walk(def.OnSuccess, st, target, last: saved);
        if (def.HalfOnSuccess && !evades && !def.OnFail.IsDefault)
            onSuccess = Walk([.. def.OnFail.OfType<DamageEffect>()], st, target, half: true, last: saved).Then(onSuccess);
        return Outcome.Mix([(pFail, onFail), (1 - pFail, onSuccess)]);
    }

    /// <summary>A damage block's damage (see <see cref="DamageBlock"/> and <see cref="Hp.Damage"/>).</summary>
    private DamageDist Damage(DamageEffect def, Strike st, UnitState target, bool crit, bool half)
    {
        var sheet = st.User.Sheet;
        var dist = DamageDist.Zero;
        var flat = st.Context.Sum(def.Bonus);
        if (def.Weapon)
        {
            dist = Dist(sheet.Weapon.Damage, crit);
            flat += Stats.Mod(sheet.Score(AttackBlock.WeaponStat(sheet))) + Mods(st.User).Damage;
        }
        if (def.Dice is { } dice)
            dist = dist.Plus(Dist(dice.At(st.Context.Level), crit));
        dist = dist.Shift(flat);
        if (dist.Min < 0)
            dist = dist.Map(v => Math.Max(0, v));
        if (half)
            dist = dist.Map(v => v / 2);

        if ((def.Type ?? (def.Weapon ? sheet.Weapon.DamageType : null)) is { } type)
        {
            var mods = Mods(target);
            if (target.Sheet.Immune.Contains(type) || mods.Immune?.Contains(type) == true)
                return DamageDist.Zero;
            if (target.Sheet.Resist.Contains(type) || mods.Resist?.Contains(type) == true)
                dist = dist.Map(v => v / 2);
            if (target.Sheet.Vulnerable.Contains(type))
                dist = dist.Map(v => v * 2);
        }
        return dist;
    }

    /// <summary>Ending the target's conditions that match: worth the opposite of what they do to it.</summary>
    private Outcome Removing(UnitState target, Func<ActiveCondition, bool> matches)
    {
        var (offense, defense) = (0.0, 0.0);
        foreach (var id in target.Conditions.Where(matches).Select(c => c.Id).Distinct())
        {
            var benefit = ConditionBenefit(id);
            offense -= benefit.Offense * Math.Min(1.5, benefit.OffenseTurns);
            defense -= benefit.Defense * Math.Min(1.5, benefit.DefenseTurns);
        }
        return new Outcome(DamageDist.Zero, Offense: offense, Defense: defense);
    }

    /// <summary>True when the unit has the condition on it (not counting implied ones).</summary>
    private static bool Has(UnitState unit, ConditionId id) => !unit.Conditions.IsDefault && unit.Conditions.Any(c => c.Id == id);

    /// <summary>
    /// How many of the target's turns an effect lasting <paramref name="rounds"/> is expected to matter for: until a save,
    /// 1.5; for the whole battle, 3; until the end of the current turn, a quarter; otherwise the rounds, at most 3.
    /// </summary>
    private static double Turns(int? rounds, bool untilSave)
        => untilSave ? 1.5 : rounds is not { } n ? 3 : n <= 0 ? 0.25 : Math.Min(n, 3);
}
