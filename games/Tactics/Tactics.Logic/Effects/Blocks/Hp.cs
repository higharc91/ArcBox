namespace Tactics.Logic;

/// <summary>Taking damage and healing. Reaching 0 HP goes through <see cref="DownedRules.Drop"/>.</summary>
public static class Hp
{
    /// <summary>
    /// Deals <paramref name="amount"/> damage of <paramref name="type"/> to <paramref name="unit"/> from
    /// <paramref name="source"/> (if a unit dealt it): immunity (the sheet's or a static modifier's) cancels it, resistance
    /// halves it (rounded down), vulnerability doubles it. Returns the damage dealt.
    /// <list type="bullet">
    /// <item>An Up unit: temporary HP absorbs it first; emits <see cref="Damaged"/>. Damage over 0 that leaves it at 0 HP
    /// drops it (<see cref="DownedRules.Drop"/>); otherwise a concentrating unit saves to keep its concentration
    /// (<see cref="Conditions.ConcentrationDc"/>) and the unit's <see cref="HookKind.OnDamaged"/> hooks fire.</item>
    /// <item>A Downed unit takes no damage (0 is returned): damage over 0 is a hit on its countdown, 2 for a
    /// <paramref name="crit"/>, once per hit <paramref name="resolution"/> (<see cref="DownedRules.Hit"/>).</item>
    /// <item>An Out or Dead unit takes nothing.</item>
    /// </list>
    /// </summary>
    public static int Damage(BattleWriter w, UnitId unit, int amount, string? type, UnitId? source = null, bool crit = false,
        int? resolution = null)
    {
        var u = w.Unit(unit);
        if (!DownedRules.InBattle(u))
            return 0;
        amount = Math.Max(0, amount);
        if (type is not null)
        {
            var mods = Modifiers.For(w, unit, w.Rules);
            if (u.Sheet.Immune.Contains(type) || mods.Immune?.Contains(type) == true)
                amount = 0;
            if (u.Sheet.Resist.Contains(type) || mods.Resist?.Contains(type) == true)
                amount /= 2;
            if (u.Sheet.Vulnerable.Contains(type))
                amount *= 2;
        }

        if (u.Life == LifeState.Downed)
        {
            if (amount > 0)
                DownedRules.Hit(w, unit, crit, resolution);
            return 0;
        }

        var absorbed = Math.Min(u.TempHp, amount);
        var hp = Math.Max(0, u.Hp - (amount - absorbed));
        w.Set(u with { TempHp = u.TempHp - absorbed, Hp = hp });
        w.Emit(new Damaged(unit, amount, type, hp));
        if (amount <= 0)
            return amount;
        if (hp == 0)
        {
            DownedRules.Drop(w, unit, source);
            return amount;
        }

        Conditions.CheckConcentration(w, unit, amount, source, w.Rules);
        Hooks.Fire(w, HookKind.OnDamaged, new HookArgs(unit, source, Amount: amount, DamageType: type), w.Rules);
        return amount;
    }

    /// <summary>
    /// Restores up to <paramref name="amount"/> HP (never above max). Emits <see cref="Healed"/> with the HP restored. A
    /// Downed unit healed for more than 0 gets up with the HP healed (<see cref="DownedRules.Revive"/>); an Out or Dead one
    /// can't be healed.
    /// </summary>
    public static int Heal(BattleWriter w, UnitId unit, int amount)
    {
        var u = w.Unit(unit);
        if (!DownedRules.InBattle(u))
            return 0;
        var hp = Math.Max(u.Hp, Math.Min(u.Sheet.MaxHp, u.Hp + Math.Max(0, amount)));
        w.Set(u with { Hp = hp });
        w.Emit(new Healed(unit, hp - u.Hp, hp));
        if (u.Life == LifeState.Downed && hp > 0)
            DownedRules.Revive(w, unit, hp);
        return hp - u.Hp;
    }
}

/// <summary>
/// <c>"damage"</c>: the dice at the source's level (and, for a weapon, the weapon's dice, attack stat and static damage
/// modifiers), plus named addends; a critical hit doubles the dice (and takes 2 off a Downed target's countdown); a
/// successful save's half damage halves the total.
/// </summary>
public sealed class DamageBlock : EffectBlock<DamageEffect>
{
    /// <inheritdoc />
    public override string Key => "damage";

    /// <inheritdoc />
    protected override void Run(DamageEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var sheet = ctx.SourceUnit.Sheet;
        var crit = ctx.Last?.Crit == true;
        var total = 0;
        if (def.Weapon)
            total += Rolls.Damage(ctx.W, sheet.Weapon.Damage, crit) + Stats.Mod(sheet.Score(AttackBlock.WeaponStat(sheet)))
                     + Modifiers.For(ctx.W, ctx.Source, ctx.Rules).Damage;
        if (def.Dice is { } dice)
            total += Rolls.Damage(ctx.W, dice.At(ctx.Level), crit);
        total += ctx.Sum(def.Bonus);
        total = Math.Max(0, total);
        if (ctx.HalfDamage)
            total /= 2;

        Hp.Damage(ctx.W, target.Id, total, def.Type ?? (def.Weapon ? sheet.Weapon.DamageType : null), ctx.Source, crit,
            ctx.Resolution);
    }
}

/// <summary><c>"heal"</c>: the dice at the source's level plus named addends, up to the target's max HP.</summary>
public sealed class HealBlock : EffectBlock<HealEffect>
{
    /// <inheritdoc />
    public override string Key => "heal";

    /// <inheritdoc />
    protected override void Run(HealEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var amount = Rolls.Damage(ctx.W, def.Dice.At(ctx.Level), crit: false) + ctx.Sum(def.Bonus);
        Hp.Heal(ctx.W, target.Id, amount);
    }
}

/// <summary>
/// <c>"help"</c>: the Help action. A Downed target gets up at 1 HP (<see cref="DownedRules.Revive"/>); a standing one gets
/// <see cref="RulesPack.Helped"/> from the source for 1 round: advantage on its next d20 (<see cref="Consume"/>).
/// </summary>
public sealed class HelpBlock : EffectBlock<HelpEffect>
{
    /// <inheritdoc />
    public override string Key => "help";

    /// <inheritdoc />
    protected override void Run(HelpEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        if (target.Life == LifeState.Downed)
        {
            DownedRules.Revive(ctx.W, target.Id, 1);
            return;
        }

        var helped = new ActiveCondition(RulesPack.Helped.Id, ctx.Source, RoundsLeft: 1, UntilSave: false, SaveDc: 0, Stat.Con,
            Concentration: false, AppliedThisTurn: ctx.W.State.Active == ctx.Source);
        ctx.W.Set(target with { Conditions = target.Conditions.RemoveAll(c => c.Id == helped.Id).Add(helped) });
        ctx.W.Emit(new ConditionApplied(target.Id, helped.Id));
    }

    /// <summary>
    /// Called as <paramref name="unit"/> rolls a d20 (an attack roll or a save): if it is helped, the help is used up
    /// (<see cref="ConditionRemoved"/>) and true is returned, so the roll has advantage.
    /// </summary>
    public static bool Consume(BattleWriter w, UnitId unit)
    {
        if (!w.Unit(unit).Conditions.Any(c => c.Id == RulesPack.Helped.Id))
            return false;
        Conditions.Remove(w, unit, RulesPack.Helped.Id);
        return true;
    }
}
