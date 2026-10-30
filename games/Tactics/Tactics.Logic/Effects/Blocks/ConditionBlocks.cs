namespace Tactics.Logic;

/// <summary>
/// <c>"apply"</c>: puts a condition on the target, replacing one with the same id. An until-save condition takes the
/// stat and DC of the enclosing save; a condition from a concentration ability is tied to the source's concentration.
/// Emits <see cref="ConditionApplied"/>.
/// </summary>
public sealed class ApplyBlock : EffectBlock<ApplyEffect>
{
    /// <inheritdoc />
    public override string Key => "apply";

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

        var condition = new ActiveCondition(def.Condition, ctx.Source, def.Rounds, def.UntilSave, dc, stat,
            Concentration: ctx.Ability?.Cost.Concentration == true);
        ctx.W.Set(target with { Conditions = target.Conditions.RemoveAll(c => c.Id == def.Condition).Add(condition) });
        ctx.W.Emit(new ConditionApplied(target.Id, def.Condition));
    }
}

/// <summary><c>"remove"</c>: ends the condition on the target. Emits <see cref="ConditionRemoved"/> if it had it.</summary>
public sealed class RemoveBlock : EffectBlock<RemoveEffect>
{
    /// <inheritdoc />
    public override string Key => "remove";

    /// <inheritdoc />
    protected override void Run(RemoveEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var kept = target.Conditions.RemoveAll(c => c.Id == def.Condition);
        if (kept.Length == target.Conditions.Length)
            return;
        ctx.W.Set(target with { Conditions = kept });
        ctx.W.Emit(new ConditionRemoved(target.Id, def.Condition));
    }
}
