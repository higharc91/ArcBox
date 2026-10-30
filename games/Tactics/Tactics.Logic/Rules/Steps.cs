using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// One unit of resolution on the battle's queue. <see cref="Battle.Apply"/> turns a command into steps and runs them in
/// order; a step may push further steps to the front, or pause the queue by setting <see cref="BattleState.Pending"/>.
/// Steps are part of the saved state, so every step is a serialisable record.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(EndTurnStep), nameof(EndTurnStep))]
[JsonDerivedType(typeof(StartNextTurnStep), nameof(StartNextTurnStep))]
[JsonDerivedType(typeof(DelayStep), nameof(DelayStep))]
[JsonDerivedType(typeof(PayCostStep), nameof(PayCostStep))]
[JsonDerivedType(typeof(RunEffectsStep), nameof(RunEffectsStep))]
[JsonDerivedType(typeof(MoveStep), nameof(MoveStep))]
[JsonDerivedType(typeof(OfferReactionStep), nameof(OfferReactionStep))]
[JsonDerivedType(typeof(ReactionStep), nameof(ReactionStep))]
public abstract record Step
{
    /// <summary>Resolves the step against the writer.</summary>
    public abstract void Run(BattleWriter w, RulesPack rules);
}

/// <summary>Ends <see cref="Unit"/>'s turn (<see cref="TurnEnded"/>) and queues the hand-off to the next unit.</summary>
public sealed record EndTurnStep(UnitId Unit) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        w.Emit(new TurnEnded(Unit));
        w.PushFront(new StartNextTurnStep());
    }
}

/// <summary>
/// Hands the turn to the next unit in the order, starting a new round (re-sorted) when the order runs out.
/// <see cref="BattleState.Active"/> stays on the old unit until this step runs.
/// </summary>
public sealed record StartNextTurnStep : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        w.State = w.State with { TurnIndex = w.State.TurnIndex + 1 };
        Battle.BeginTurn(w, rules);
    }
}

/// <summary>
/// Moves <see cref="Unit"/> to <see cref="ToIndex"/> in this round's order and starts the turn of the unit now at its old
/// place. When the new place comes up, the delayed unit's turn resumes (see <see cref="Battle.BeginTurn"/>).
/// </summary>
public sealed record DelayStep(UnitId Unit, int ToIndex) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var order = w.State.Order;
        w.State = w.State with
        {
            Order = order.RemoveAt(order.IndexOf(Unit)).Insert(ToIndex, Unit),
            DelayedThisRound = w.State.DelayedThisRound.Add(Unit),
        };
        w.Emit(new Delayed(Unit, ToIndex));
        Battle.BeginTurn(w, rules);
    }
}

/// <summary>
/// Spends what <see cref="Ability"/> costs <see cref="Unit"/> (Action, or an extra action once the Action is spent; Bonus;
/// Reaction; Move; resources; a per-battle use), starts its concentration, and emits <see cref="AbilityUsed"/>. The
/// command was validated, so the cost is affordable.
/// </summary>
public sealed record PayCostStep(UnitId Unit, AbilityId Ability, Target Target) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var ability = rules.Abilities[Ability];
        var cost = ability.Cost;
        var unit = w.Unit(Unit);

        var economy = unit.Economy;
        if (cost.Action > 0)
            economy = economy.Action ? economy with { Action = false } : economy with { ExtraActions = economy.ExtraActions - 1 };
        if (cost.Bonus > 0)
            economy = economy with { Bonus = false };
        if (cost.Reaction > 0)
            economy = economy with { Reaction = false };
        economy = economy with { MoveLeft = economy.MoveLeft - cost.Move };

        var resources = unit.Resources;
        foreach (var (name, amount) in cost.Resource ?? Ordinal.Map<int>())
            resources = resources.SetItem(name, resources.GetValueOrDefault(name) - amount);

        var usesLeft = unit.UsesLeft;
        if (cost.Uses is { } uses)
            usesLeft = usesLeft.SetItem(Ability.Value, usesLeft.GetValueOrDefault(Ability.Value, uses) - 1);

        // Task 7 ends the previous concentration effect when a new one starts.
        w.Set(unit with
        {
            Economy = economy, Resources = resources, UsesLeft = usesLeft,
            Concentrating = cost.Concentration ? Ability : unit.Concentrating,
        });
        w.Emit(new AbilityUsed(Unit, Ability, Target, ability.Presentation));
    }
}

/// <summary>
/// Runs <see cref="Effects"/> in order from <see cref="Source"/> on <see cref="Target"/> (null for a tile) aimed at
/// <see cref="Aim"/>. Each run takes the first effect and queues the rest behind it, so whatever a block queues (a hit's
/// effects, a reaction) resolves before the next effect, and a save mid-list resumes exactly there. <see cref="Last"/>,
/// <see cref="HalfDamage"/> and <see cref="SaveStat"/> carry the roll a branch follows (see <see cref="EffectContext"/>).
/// Nothing runs on a target that is Dead or Out.
/// </summary>
public sealed record RunEffectsStep(UnitId Source, UnitId? Target, Tile? Aim, AbilityId? Ability,
    ImmutableArray<EffectDef> Effects, RollResult? Last = null, bool HalfDamage = false, Stat? SaveStat = null) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        if (Effects.IsDefaultOrEmpty)
            return;
        if (Target is { } target && w.Unit(target).Life is LifeState.Dead or LifeState.Out)
            return;
        if (Effects.Length > 1)
            w.PushFront(this with { Effects = Effects.RemoveAt(0) });

        var ability = Ability is { } id ? rules.Abilities.GetValueOrDefault(id) : null;
        var ctx = new EffectContext(w, rules, Source, Target, Aim, ability, Last, HalfDamage, SaveStat);
        BlockRegistry.For(Effects[0]).Run(Effects[0], ctx);
    }
}
