namespace Tactics.Logic;

/// <summary>
/// <c>"apply"</c>: puts a condition on the target, replacing one with the same id. An until-save condition takes the
/// stat and DC of the enclosing save; a condition from a concentration ability is tied to the source's concentration.
/// Applied during the source's own turn, it is marked <see cref="ActiveCondition.AppliedThisTurn"/>.
/// Emits <see cref="ConditionApplied"/>. A target it leaves incapacitated loses its concentration.
/// </summary>
public sealed class ApplyBlock : EffectBlock<ApplyEffect>
{
    /// <inheritdoc />
    public override string Key => "apply";

    /// <inheritdoc />
    protected override string? Describe(ApplyEffect def, int level, RulesPack rules)
        => $"target is {TooltipText.Condition(def.Condition, rules)}"
           + (def.UntilSave ? " until saved" : def.Rounds is { } rounds ? $" {TooltipText.ForRounds(rounds)}" : "");

    /// <inheritdoc />
    protected override void Run(ApplyEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var (dc, stat) = (0, Stat.Con);   // unused unless UntilSave
        if (def.UntilSave)
        {
            if (ctx.SaveStat is not { } saveStat || ctx.Last is not { } save)
                throw new InvalidOperationException($"'apply' {def.Condition} until save must sit under a 'save' block.");
            (dc, stat) = (save.Vs, saveStat);
        }

        var conc = ctx.Ability?.Cost.Concentration == true;
        if (ctx.ConcentrationLapsed(conc))
            return;
        var rounds = Metamagic.ExtendRounds(ctx, def.Rounds, conc);
        var condition = new ActiveCondition(def.Condition, ctx.Source, rounds, def.UntilSave, dc, stat,
            Concentration: conc, AppliedThisTurn: ctx.W.State.Active == ctx.Source);
        ctx.W.Set(target with { Conditions = target.Conditions.RemoveAll(c => c.Id == def.Condition).Add(condition) });
        ctx.W.Emit(new ConditionApplied(target.Id, def.Condition));
        Conditions.BreakIfIncapacitated(ctx.W, target.Id);
    }
}

/// <summary>
/// <c>"remove"</c>: ends the condition on the target (<see cref="Conditions.Remove"/>). Emits <see cref="ConditionRemoved"/>
/// if it had it.
/// </summary>
public sealed class RemoveBlock : EffectBlock<RemoveEffect>
{
    /// <inheritdoc />
    public override string Key => "remove";

    /// <inheritdoc />
    protected override string? Describe(RemoveEffect def, int level, RulesPack rules)
        => $"ends {TooltipText.Condition(def.Condition, rules)} on target";

    /// <inheritdoc />
    protected override void Run(RemoveEffect def, EffectContext ctx) => Conditions.Remove(ctx.W, ctx.TargetUnit(Key).Id, def.Condition);
}

/// <summary>
/// <c>"cleanse"</c>: ends each condition on the target whose definition (or one it implies) has any of the tags, in the
/// order applied (<see cref="ConditionRemoved"/> each).
/// </summary>
public sealed class CleanseBlock : EffectBlock<CleanseEffect>
{
    /// <inheritdoc />
    public override string Key => "cleanse";

    /// <inheritdoc />
    protected override string? Describe(CleanseEffect def, int level, RulesPack rules)
        => $"ends {string.Join(", ", def.Tags)} conditions on target";

    /// <inheritdoc />
    protected override void Run(CleanseEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var matching = Conditions.Active(target, ctx.Rules)
            .Where(a => a.Def.Tags.Overlaps(def.Tags))
            .Select(a => a.From.Id)
            .Distinct()
            .ToArray();
        foreach (var id in matching)
            Conditions.Remove(ctx.W, target.Id, id);
    }
}

/// <summary>
/// <c>"dispel"</c>: ends the concentration of the target, or on a tile run of the unit on the aimed tile
/// (<see cref="Conditions.BreakConcentration"/>), and/or every zone covering the aimed tile (<see cref="ZoneRules.End"/>).
/// </summary>
public sealed class DispelBlock : EffectBlock<DispelEffect>
{
    /// <inheritdoc />
    public override string Key => "dispel";

    /// <inheritdoc />
    protected override string? Describe(DispelEffect def, int level, RulesPack rules)
    {
        List<string> parts = [];
        if (def.Concentration)
            parts.Add("ends concentration");
        if (def.Zones)
            parts.Add("ends zones on the aimed tile");
        return parts.Count == 0 ? "dispels nothing" : string.Join(" and ", parts);
    }

    /// <inheritdoc />
    protected override void Run(DispelEffect def, EffectContext ctx)
    {
        if (def.Concentration)
        {
            var unit = ctx.Target ?? (ctx.Aim is { } at ? Motion.OccupantAt(ctx.W, at, except: default)?.Id : null);
            if (unit is { } id)
                Conditions.BreakConcentration(ctx.W, id);
        }
        if (def.Zones && ctx.Aim is { } aim)
            ZoneRules.End(ctx.W, z => z.Tiles.Contains(aim));
    }
}
