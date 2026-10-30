using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// <c>"grantResource"</c>: adds the amount to the target's resource (a negative amount drains it), never below 0 and, when
/// its sheet has a maximum for it, never above that unless the effect is <c>overMax</c>. <c>"primary"</c> names the first
/// resource on the target's sheet (see <see cref="GrantResourceEffect.Primary"/>).
/// </summary>
public sealed class GrantResourceBlock : EffectBlock<GrantResourceEffect>
{
    /// <inheritdoc />
    public override string Key => "grantResource";

    /// <inheritdoc />
    protected override string? Describe(GrantResourceEffect def, int level, RulesPack rules)
    {
        var resource = def.Resource == GrantResourceEffect.Primary ? "of its first resource" : TooltipText.Humanize(def.Resource);
        var text = def.Amount >= 0 ? $"target gains {def.Amount} {resource}" : $"target loses {-def.Amount} {resource}";
        return def.OverMax && def.Amount > 0 ? text + ", even above its maximum" : text;
    }

    /// <inheritdoc />
    protected override void Run(GrantResourceEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var resource = def.Resource;
        if (resource == GrantResourceEffect.Primary)
        {
            if (target.Sheet.Resources.IsEmpty)
                return;
            resource = target.Sheet.Resources.Keys.Min(StringComparer.Ordinal)!;
        }
        var amount = Math.Max(0, target.Resources.GetValueOrDefault(resource) + def.Amount);
        if (!def.OverMax && target.Sheet.Resources.TryGetValue(resource, out var max))
            amount = Math.Min(amount, max);
        ctx.W.Set(target with { Resources = target.Resources.SetItem(resource, amount) });
    }
}

/// <summary>
/// <c>"extraAction"</c>: <c>"action"</c> gives the target one more action this turn (<see cref="TurnEconomy.ExtraActions"/>);
/// <c>"bonus"</c> gives its bonus action back; <c>"move"</c> adds the target's Move (with its modifiers) to what it has left,
/// as the Dash action does. Throws on any other kind.
/// </summary>
public sealed class ExtraActionBlock : EffectBlock<ExtraActionEffect>
{
    /// <summary>The kinds content may use.</summary>
    public static readonly ImmutableArray<string> Kinds = ["action", "bonus", "move"];

    /// <inheritdoc />
    public override string Key => "extraAction";

    /// <inheritdoc />
    protected override string? Describe(ExtraActionEffect def, int level, RulesPack rules)
        => def.Kind switch
        {
            "bonus" => "target gets its bonus action back",
            "move" => "target gains its Move again",
            _ => "target gains an extra action this turn",
        };

    /// <inheritdoc />
    protected override void Run(ExtraActionEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var economy = def.Kind switch
        {
            "action" => target.Economy with { ExtraActions = target.Economy.ExtraActions + 1 },
            "bonus" => target.Economy with { Bonus = true },
            "move" => target.Economy with
            {
                MoveLeft = target.Economy.MoveLeft + Modifiers.Move(target.Sheet, Modifiers.For(ctx.W, target.Id, ctx.Rules)),
            },
            _ => throw new InvalidOperationException($"'extraAction' kind must be \"action\", \"bonus\" or \"move\", not '{def.Kind}'."),
        };
        ctx.W.Set(target with { Economy = economy });
    }
}

/// <summary><c>"refreshReaction"</c>: the target gets its Reaction back.</summary>
public sealed class RefreshReactionBlock : EffectBlock<RefreshReactionEffect>
{
    /// <inheritdoc />
    public override string Key => "refreshReaction";

    /// <inheritdoc />
    protected override string? Describe(RefreshReactionEffect def, int level, RulesPack rules) => "target gets its reaction back";

    /// <inheritdoc />
    protected override void Run(RefreshReactionEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        ctx.W.Set(target with { Economy = target.Economy with { Reaction = true } });
    }
}

/// <summary>
/// <c>"statMod"</c>: puts the condition <c>statMod.&lt;ability id&gt;</c> (<c>statMod.effect</c> outside an ability) from
/// the source on the target, carrying the modifiers (<see cref="ActiveCondition.Mods"/>), replacing one with the same id.
/// Its rounds tick like any condition's. Modifiers to Move and extra actions count from the target's next turn. Emits
/// <see cref="ConditionApplied"/>; a target it leaves incapacitated loses its concentration.
/// </summary>
public sealed class StatModBlock : EffectBlock<StatModEffect>
{
    /// <inheritdoc />
    public override string Key => "statMod";

    /// <inheritdoc />
    protected override string? Describe(StatModEffect def, int level, RulesPack rules)
        => $"target {TooltipText.Predicate(def.Mods)} {TooltipText.ForRounds(def.Rounds)}";

    /// <inheritdoc />
    protected override void Run(StatModEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var id = new ConditionId("statMod." + (ctx.Ability?.Id.Value ?? "effect"));
        Sourced.Put(ctx, target.Id, id, def.Rounds, def.Concentration, def.Mods);
        Conditions.BreakIfIncapacitated(ctx.W, target.Id);
    }
}

/// <summary>
/// <c>"mark"</c>: the source's mark (<see cref="Condition"/>) moves to the target: removed from any other unit it marked,
/// then put on the target for the rounds. Emits <see cref="ConditionApplied"/> and <see cref="Marked"/>.
/// </summary>
public sealed class MarkBlock : EffectBlock<MarkEffect>
{
    /// <summary>The condition a mark is.</summary>
    public static readonly ConditionId Condition = new("marked");

    /// <inheritdoc />
    public override string Key => "mark";

    /// <inheritdoc />
    protected override string? Describe(MarkEffect def, int level, RulesPack rules)
        => $"marks target {TooltipText.ForRounds(def.Rounds)}";

    /// <inheritdoc />
    protected override void Run(MarkEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        foreach (var other in ctx.W.State.Units.Values
                     .Where(u => u.Id != target.Id && u.Conditions.Any(c => c.Id == Condition && c.Source == ctx.Source))
                     .Select(u => u.Id).ToArray())
            Conditions.Remove(ctx.W, other, Condition);
        Sourced.Put(ctx, target.Id, Condition, def.Rounds, concentration: false, mods: null);
        ctx.W.Emit(new Marked(target.Id, ctx.Source));
    }
}

/// <summary>
/// <c>"taunt"</c>: the target is taunted by the source (<see cref="Condition"/>) for the rounds, replacing any other taunt:
/// its attack rolls against anyone but the source have disadvantage (<see cref="TauntedAway"/>). Emits
/// <see cref="ConditionApplied"/>.
/// </summary>
public sealed class TauntBlock : EffectBlock<TauntEffect>
{
    /// <summary>The condition a taunt is.</summary>
    public static readonly ConditionId Condition = new("taunted");

    /// <inheritdoc />
    public override string Key => "taunt";

    /// <inheritdoc />
    protected override string? Describe(TauntEffect def, int level, RulesPack rules)
        => $"taunts target {TooltipText.ForRounds(def.Rounds)}";

    /// <inheritdoc />
    protected override void Run(TauntEffect def, EffectContext ctx)
        => Sourced.Put(ctx, ctx.TargetUnit(Key).Id, Condition, def.Rounds, concentration: false, mods: null);

    /// <summary>True when <paramref name="attacker"/> is taunted by a unit still in the battle other than <paramref name="target"/>.</summary>
    public static bool TauntedAway(BattleState state, UnitId attacker, UnitId target)
        => state.Units[attacker].Conditions.Any(c => c.Id == Condition && c.Source is { } by && by != target
                                                     && state.Units.TryGetValue(by, out var taunter) && DownedRules.InBattle(taunter));
}

/// <summary>
/// <c>"reveal"</c>: every unit hostile to the source on the area's tiles around the aim loses the conditions that make it
/// invisible (by definition, implied or carried), in the order applied (<see cref="ConditionRemoved"/> each). Units go in
/// <see cref="UnitId"/> order.
/// </summary>
public sealed class RevealBlock : EffectBlock<RevealEffect>
{
    /// <inheritdoc />
    public override string Key => "reveal";

    /// <inheritdoc />
    protected override string? Describe(RevealEffect def, int level, RulesPack rules)
        => $"reveals hidden enemies ({TooltipText.Area(def.Area)})";

    /// <inheritdoc />
    protected override void Run(RevealEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        var tiles = Areas.Tiles(def.Area, source.Pos, ctx.Aim ?? source.Pos, ctx.W.Terrain).ToImmutableSortedSet();
        foreach (var unit in ctx.W.State.Units.Values
                     .Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos) && Movement.Hostile(source, u)).ToArray())
        {
            var hiding = Conditions.Active(unit, ctx.Rules).Where(a => a.Def.Static?.Invisible == true).Select(a => a.From.Id)
                .ToImmutableSortedSet();
            foreach (var id in unit.Conditions.Where(c => c.Mods?.Invisible == true || hiding.Contains(c.Id)).Select(c => c.Id)
                         .Distinct().ToArray())
                Conditions.Remove(ctx.W, unit.Id, id);
        }
    }
}

/// <summary>Conditions from a block's source that the combat blocks put on their target.</summary>
internal static class Sourced
{
    /// <summary>
    /// Puts <paramref name="id"/> from the source on <paramref name="target"/> for <paramref name="rounds"/>, replacing one
    /// with the same id; applied during the source's turn, it is marked <see cref="ActiveCondition.AppliedThisTurn"/>.
    /// Emits <see cref="ConditionApplied"/>.
    /// </summary>
    public static void Put(EffectContext ctx, UnitId target, ConditionId id, int rounds, bool concentration, StaticMods? mods)
    {
        var unit = ctx.W.Unit(target);
        var condition = new ActiveCondition(id, ctx.Source, rounds, UntilSave: false, SaveDc: 0, Stat.Con, concentration,
            AppliedThisTurn: ctx.W.State.Active == ctx.Source, Mods: mods);
        ctx.W.Set(unit with { Conditions = unit.Conditions.RemoveAll(c => c.Id == id).Add(condition) });
        ctx.W.Emit(new ConditionApplied(target, id));
    }
}
