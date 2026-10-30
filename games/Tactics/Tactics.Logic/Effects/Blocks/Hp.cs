namespace Tactics.Logic;

/// <summary>Taking damage and healing. Downed, death and revival arrive in Task 8.</summary>
public static class Hp
{
    /// <summary>
    /// Deals <paramref name="amount"/> damage of <paramref name="type"/> to <paramref name="unit"/> from
    /// <paramref name="source"/> (if a unit dealt it): immunity (the sheet's or a static modifier's) cancels it, resistance
    /// halves it (rounded down), vulnerability doubles it; temporary HP absorbs it first. Emits <see cref="Damaged"/>. Damage
    /// over 0 then makes a concentrating unit save to keep its concentration (<see cref="Conditions.ConcentrationDc"/>) and
    /// fires the unit's <see cref="HookKind.OnDamaged"/> hooks. Returns the damage dealt.
    /// </summary>
    public static int Damage(BattleWriter w, UnitId unit, int amount, string? type, UnitId? source = null)
    {
        var u = w.Unit(unit);
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

        var absorbed = Math.Min(u.TempHp, amount);
        var hp = Math.Max(0, u.Hp - (amount - absorbed));
        w.Set(u with { TempHp = u.TempHp - absorbed, Hp = hp });
        w.Emit(new Damaged(unit, amount, type, hp));
        if (amount <= 0)
            return amount;

        Conditions.CheckConcentration(w, unit, amount, source, w.Rules);
        Hooks.Fire(w, HookKind.OnDamaged, new HookArgs(unit, source, Amount: amount, DamageType: type), w.Rules);
        return amount;
    }

    /// <summary>Restores up to <paramref name="amount"/> HP (never above max). Emits <see cref="Healed"/> with the HP restored.</summary>
    public static int Heal(BattleWriter w, UnitId unit, int amount)
    {
        var u = w.Unit(unit);
        var hp = Math.Max(u.Hp, Math.Min(u.Sheet.MaxHp, u.Hp + Math.Max(0, amount)));
        w.Set(u with { Hp = hp });
        w.Emit(new Healed(unit, hp - u.Hp, hp));
        return hp - u.Hp;
    }
}

/// <summary>
/// <c>"damage"</c>: the dice at the source's level (and, for a weapon, the weapon's dice, attack stat and static damage
/// modifiers), plus named addends; a critical hit doubles the dice; a successful save's half damage halves the total.
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

        Hp.Damage(ctx.W, target.Id, total, def.Type ?? (def.Weapon ? sheet.Weapon.DamageType : null), ctx.Source);
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
