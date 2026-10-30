namespace Tactics.Logic;

/// <summary>
/// <c>"chain"</c>: picks the bounces first: from the target, up to the given number of times, the nearest unit within
/// range of the last one picked that is on the first target's side, in the battle, not the source and not yet picked
/// (ties by <see cref="UnitId"/>). Then runs the effects on the target (as part of its hit, following its roll) and on each
/// bounce in turn (each a hit of its own, following no roll), aimed at that unit's tile.
/// </summary>
public sealed class ChainBlock : EffectBlock<ChainEffect>
{
    /// <inheritdoc />
    public override string Key => "chain";

    /// <inheritdoc />
    protected override string? Describe(ChainEffect def, int level, RulesPack rules)
    {
        var bounce = $"chains to up to {def.Times} more {(def.Times == 1 ? "target" : "targets")} within {TooltipText.Tiles(def.Range)}";
        return def.Effects.IsDefaultOrEmpty ? bounce : $"{Tooltips.List(def.Effects, level, rules)}; {bounce}";
    }

    /// <inheritdoc />
    protected override void Run(ChainEffect def, EffectContext ctx)
    {
        var first = ctx.TargetUnit(Key);
        var picked = new List<UnitState> { first };
        for (var i = 0; i < def.Times; i++)
        {
            var from = picked[^1].Pos;
            var next = ctx.W.State.Units.Values
                .Where(u => DownedRules.InBattle(u) && u.Sheet.Side == first.Sheet.Side && u.Id != ctx.Source
                            && picked.All(p => p.Id != u.Id) && Reach.Distance(from, u.Pos) <= def.Range)
                .OrderBy(u => Reach.Distance(from, u.Pos))
                .ThenBy(u => u.Id)
                .FirstOrDefault();
            if (next is null)
                break;
            picked.Add(next);
        }

        var effects = def.Effects.IsDefault ? [] : def.Effects;
        if (effects.IsEmpty)
            return;
        ctx.W.PushFront([
            new RunEffectsStep(ctx.Source, first.Id, ctx.Aim, ctx.Ability?.Id, effects, ctx.Last, ctx.HalfDamage, ctx.SaveStat,
                ctx.W.HookDepth, Resolution: ctx.Resolution),
            .. picked.Skip(1).Select(u => new RunEffectsStep(ctx.Source, u.Id, u.Pos, ctx.Ability?.Id, effects,
                HookDepth: ctx.W.HookDepth)),
        ]);
    }
}

/// <summary><c>"repeat"</c>: runs the effects on the target the given number of times, each run a hit of its own.</summary>
public sealed class RepeatBlock : EffectBlock<RepeatEffect>
{
    /// <inheritdoc />
    public override string Key => "repeat";

    /// <inheritdoc />
    protected override string? Describe(RepeatEffect def, int level, RulesPack rules)
        => $"{def.Times} times: {Tooltips.Group(def.Effects, level, rules)}";

    /// <inheritdoc />
    protected override void Run(RepeatEffect def, EffectContext ctx)
    {
        if (def.Effects.IsDefaultOrEmpty || def.Times < 1)
            return;
        ctx.W.PushFront([.. Enumerable.Range(0, def.Times).Select(_ => new RunEffectsStep(ctx.Source, ctx.Target, ctx.Aim,
            ctx.Ability?.Id, def.Effects, ctx.Last, ctx.HalfDamage, ctx.SaveStat, ctx.W.HookDepth))]);
    }
}

/// <summary>
/// <c>"if"</c>: tests the condition (<see cref="Hooks.Holds"/>) with the source as owner, the target as the other unit,
/// the roll this effect follows, the aim and the ability, and queues the matching branch to run next.
/// </summary>
public sealed class IfBlock : EffectBlock<IfEffect>
{
    /// <inheritdoc />
    public override string Key => "if";

    /// <inheritdoc />
    protected override string? Describe(IfEffect def, int level, RulesPack rules)
    {
        var text = $"if {TooltipText.When(def.When, rules)}, {Tooltips.Group(def.Then, level, rules)}";
        return def.Else.IsDefaultOrEmpty ? text : $"{text}; otherwise {Tooltips.Group(def.Else, level, rules)}";
    }

    /// <inheritdoc />
    protected override void Run(IfEffect def, EffectContext ctx)
    {
        var args = new HookArgs(ctx.Source, ctx.Target, ctx.Last, Tile: ctx.Aim, Ability: ctx.Ability?.Id);
        var branch = Hooks.Holds(def.When, ctx.W, args, ctx.Rules) ? def.Then : def.Else;
        ctx.Queue(branch, ctx.Last, ctx.HalfDamage, ctx.SaveStat);
    }
}

/// <summary>
/// <c>"random"</c>: rolls one die the size of the table's total weight (entries of weight 0 or less never come up) and
/// queues the entry it lands on, counting through the table in order.
/// </summary>
public sealed class RandomBlock : EffectBlock<RandomEffect>
{
    /// <inheritdoc />
    public override string Key => "random";

    /// <inheritdoc />
    protected override string? Describe(RandomEffect def, int level, RulesPack rules)
    {
        var table = def.Table.IsDefault ? [] : def.Table.Where(e => e.Weight > 0).ToArray();
        var total = table.Sum(e => e.Weight);
        return table.Length == 0
            ? "nothing happens"
            : "one of, at random: " + string.Join(", ", table.Select(e => $"{Tooltips.Group(e.Effects, level, rules)} ({e.Weight} in {total})"));
    }

    /// <inheritdoc />
    protected override void Run(RandomEffect def, EffectContext ctx)
    {
        var table = def.Table.IsDefault ? [] : def.Table;
        var total = table.Sum(e => Math.Max(0, e.Weight));
        if (total <= 0)
            return;
        var roll = ctx.W.Roll(total);
        foreach (var entry in table)
        {
            roll -= Math.Max(0, entry.Weight);
            if (roll > 0)
                continue;
            ctx.Queue(entry.Effects, ctx.Last, ctx.HalfDamage, ctx.SaveStat);
            return;
        }
    }
}
