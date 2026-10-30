using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// <c>"attack"</c>: d20 + stat modifier + proficiency + bonus + high ground against the target's AC. Emits
/// <see cref="Rolled"/> and queues the hit (plus crit) or miss branch to run next. (Cover, flanking and static modifiers
/// arrive in Task 7.)
/// </summary>
public sealed class AttackBlock : EffectBlock<AttackEffect>
{
    /// <inheritdoc />
    public override string Key => "attack";

    /// <inheritdoc />
    protected override void Run(AttackEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        var target = ctx.TargetUnit(Key);
        var statModifier = def.Stat is { } stat ? Stats.Mod(source.Sheet.Score(stat))
            : def.Kind switch
            {
                "weapon" => Stats.Mod(source.Sheet.Score(WeaponStat(source.Sheet))),
                "spell" => ctx.CastingModifier,
                _ => throw new InvalidOperationException($"'attack' kind must be \"weapon\" or \"spell\", not '{def.Kind}'."),
            };
        var modifier = statModifier + Stats.Proficiency(ctx.Level) + def.Bonus
                       + Reach.HighGroundBonus(ctx.W.Terrain, source.Pos, target.Pos);

        var roll = Rolls.D20(ctx.W, modifier, target.Sheet.Ac, Edge.None, critOn20: true, autoMissOn1: true);
        ctx.W.Emit(new Rolled(source.Id, RollKind.Attack, roll));
        ctx.Last = roll;

        if (!roll.Success)
            ctx.Queue(def.OnMiss, roll);
        else if (roll.Crit && !def.OnCrit.IsDefaultOrEmpty)
            ctx.Queue(def.OnHit.IsDefault ? def.OnCrit : def.OnHit.AddRange(def.OnCrit), roll);
        else
            ctx.Queue(def.OnHit, roll);
    }

    /// <summary>The stat a weapon attacks (and deals damage) with: the better of STR and DEX for a finesse weapon.</summary>
    public static Stat WeaponStat(UnitSheet sheet)
        => sheet.Weapon.Finesse && sheet.Score(Stat.Dex) > sheet.Score(Stat.Str) ? Stat.Dex
            : sheet.Weapon.Finesse ? Stat.Str
            : sheet.Weapon.Stat;
}

/// <summary>
/// <c>"save"</c>: the target rolls d20 + stat modifier (+ proficiency if proficient) against the DC. Emits
/// <see cref="Rolled"/> for the target and queues the fail branch, or on success the half damage and the success branch.
/// </summary>
public sealed class SaveBlock : EffectBlock<SaveEffect>
{
    /// <inheritdoc />
    public override string Key => "save";

    /// <inheritdoc />
    protected override void Run(SaveEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var sheet = target.Sheet;
        var modifier = Stats.Mod(sheet.Score(def.Stat))
                       + (sheet.SaveProficiencies.Contains(def.Stat) ? Stats.Proficiency(sheet.Level) : 0);

        var roll = Rolls.D20(ctx.W, modifier, def.Dc ?? ctx.SaveDc, Edge.None, critOn20: false, autoMissOn1: false);
        ctx.W.Emit(new Rolled(target.Id, RollKind.Save, roll));
        ctx.Last = roll;

        if (!roll.Success)
        {
            ctx.Queue(def.OnFail, roll, saveStat: def.Stat);
            return;
        }

        // Each Queue goes to the front, so queue the success branch first for the half damage to run before it.
        ctx.Queue(def.OnSuccess, roll, saveStat: def.Stat);
        if (def.HalfOnSuccess && !def.OnFail.IsDefault)
            ctx.Queue([.. def.OnFail.OfType<DamageEffect>()], roll, halfDamage: true, saveStat: def.Stat);
    }
}
