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
[JsonDerivedType(typeof(SkipTurnStep), nameof(SkipTurnStep))]
[JsonDerivedType(typeof(DelayStep), nameof(DelayStep))]
[JsonDerivedType(typeof(PayCostStep), nameof(PayCostStep))]
[JsonDerivedType(typeof(RunEffectsStep), nameof(RunEffectsStep))]
[JsonDerivedType(typeof(MoveStep), nameof(MoveStep))]
[JsonDerivedType(typeof(OfferReactionStep), nameof(OfferReactionStep))]
[JsonDerivedType(typeof(ReactionStep), nameof(ReactionStep))]
[JsonDerivedType(typeof(AttackStep), nameof(AttackStep))]
[JsonDerivedType(typeof(TriggerActionStep), nameof(TriggerActionStep))]
public abstract record Step
{
    /// <summary>Resolves the step against the writer.</summary>
    public abstract void Run(BattleWriter w, RulesPack rules);
}

/// <summary>
/// Ends <see cref="Unit"/>'s turn. First its <see cref="HookKind.OnTurnEnd"/> hooks fire and their effects resolve (the
/// step re-queues itself behind them with <see cref="HooksDone"/>); then the hand-off to the next unit is queued, its
/// until-save conditions roll and durations tick (<see cref="Conditions.EndTurn"/>) and <see cref="TurnEnded"/> is
/// emitted. Effects of hooks those saves fire (<see cref="HookKind.OnSave"/>) go ahead of the hand-off, so they resolve
/// while it is still this unit's turn.
/// </summary>
public sealed record EndTurnStep(UnitId Unit,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool HooksDone = false) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        if (!HooksDone)
        {
            w.PushFront(this with { HooksDone = true });
            Hooks.Fire(w, HookKind.OnTurnEnd, new HookArgs(Unit), rules);
            return;
        }

        w.PushFront(new StartNextTurnStep());
        Conditions.EndTurn(w, Unit, rules);
        w.Emit(new TurnEnded(Unit));
    }
}

/// <summary>
/// Hands the turn to the next unit in the order, starting a new round (re-sorted) when the order runs out.
/// <see cref="BattleState.Active"/> stays on the old unit until this step runs. If the old unit left the battle during its
/// turn (Out or Dead), it leaves the order now and the next unit takes its index.
/// </summary>
public sealed record StartNextTurnStep : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var s = w.State;
        var left = s.TurnIndex < s.Order.Length && !DownedRules.InBattle(s.Units[s.Order[s.TurnIndex]]);
        w.State = left ? s with { Order = s.Order.RemoveAt(s.TurnIndex) } : s with { TurnIndex = s.TurnIndex + 1 };
        Battle.BeginTurn(w, rules);
    }
}

/// <summary>
/// Ends the turn of <see cref="Unit"/>, which was skipped because it can't act (<see cref="TurnSkipped"/>): the hand-off to
/// the next unit is queued, then its until-save conditions roll and the durations it ticks tick
/// (<see cref="Conditions.EndTurn"/>), so a stun can wear off and the conditions it applied still expire. No turn-end hooks
/// fire and no <see cref="TurnEnded"/> is emitted.
/// </summary>
public sealed record SkipTurnStep(UnitId Unit) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        w.PushFront(new StartNextTurnStep());
        Conditions.EndTurn(w, Unit, rules);
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
/// Reaction; Move; resources; a per-battle use), and emits <see cref="AbilityUsed"/>. A concentration ability first ends
/// the unit's current concentration (<see cref="Conditions.BreakConcentration"/>), then becomes it. A spell (tier 1 or
/// more) opens a <see cref="ReactionTriggerKind.SpellCast"/> window: its <see cref="OfferReactionStep"/>s run before the
/// ability's effects. With an <see cref="Item"/> (the ability is that item's), one of it leaves the unit's bag and the use
/// is recorded in <see cref="BattleState.ItemsUsed"/>; when the AI chose it (<see cref="ByAi"/>), the record says so and it
/// counts in <see cref="UnitState.ItemsUsedByAi"/>. The command was validated, so the cost is affordable.
/// </summary>
public sealed record PayCostStep(UnitId Unit, AbilityId Ability, Target Target,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ItemId? Item = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool ByAi = false) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var ability = rules.Abilities[Ability];
        var cost = ability.Cost;
        var unit = w.Unit(Unit);

        var economy = unit.Economy;
        var quickened = false;
        if (cost.Action > 0)
        {
            if (Metamagic.Quickens(unit, ability))
                quickened = true;
            else
                economy = economy.Action ? economy with { Action = false } : economy with { ExtraActions = economy.ExtraActions - 1 };
        }
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

        if (cost.Concentration)
        {
            Conditions.BreakConcentration(w, Unit);
            unit = w.Unit(Unit);
        }
        var bag = unit.Bag;
        var usedByAi = unit.ItemsUsedByAi;
        if (Item is { } item)
        {
            var stack = bag.First(s => s.Item == item && s.Count > 0);
            bag = stack.Count > 1 ? bag.Replace(stack, stack with { Count = stack.Count - 1 }) : bag.Remove(stack);
            w.State = w.State with { ItemsUsed = w.State.ItemsUsed.Add(new ItemUse(Unit, item, ByAi)) };
            if (ByAi)
                usedByAi++;
        }
        w.Set(unit with
        {
            Economy = economy, Resources = resources, UsesLeft = usesLeft, Bag = bag, ItemsUsedByAi = usedByAi,
            Concentrating = cost.Concentration ? Ability : unit.Concentrating,
        });
        if (quickened)
            Conditions.Remove(w, Unit, Metamagic.Quickened);
        if (!ability.IsWeaponAttack && !Metamagic.IsMetamagicAbility(ability))
        {
            Conditions.Remove(w, Unit, Metamagic.Distant);
            Conditions.Remove(w, Unit, Metamagic.Twinned);
        }
        w.Emit(new AbilityUsed(Unit, Ability, Target, ability.Presentation));

        if (ability.Tier < 1)
            return;
        var trigger = new ReactionTrigger(ReactionTriggerKind.SpellCast, Unit, Target.Unit, Ability, null);
        w.PushFront([.. Reactions.OffersFor(w, trigger, rules).Select(o => new OfferReactionStep(o.Reactor, trigger))]);
    }
}

/// <summary>
/// Runs <see cref="Effects"/> in order from <see cref="Source"/> on <see cref="Target"/> (null for a tile) aimed at
/// <see cref="Aim"/>. Each run takes the first effect and queues the rest behind it, so whatever a block queues (a hit's
/// effects, a reaction) resolves before the next effect, and a save mid-list resumes exactly there. <see cref="Last"/>,
/// <see cref="HalfDamage"/> and <see cref="SaveStat"/> carry the roll a branch follows (see <see cref="EffectContext"/>).
/// <see cref="HookDepth"/> is how deeply nested in hooks the effects are. A hook's effects name their
/// <see cref="Passive"/>: before the first one runs, its limits are spent (<see cref="Hooks"/>; nothing runs if the owner
/// no longer has it or its limits are used up) and <see cref="PassiveTriggered"/> is emitted. Nothing runs on a target
/// that is Dead or Out, and an ability's effects (<see cref="HookDepth"/> 0) stop once their source is no longer Up.
/// <see cref="Resolution"/> numbers the hit the effects belong to: the first run of a list without one takes a new number
/// (<see cref="BattleWriter.NewResolution"/>), which the rest of the list and its branches keep, so damage to a Downed
/// target costs it countdown once per hit (<see cref="DownedRules.Hit"/>).
/// </summary>
public sealed record RunEffectsStep(UnitId Source, UnitId? Target, Tile? Aim, AbilityId? Ability,
    ImmutableArray<EffectDef> Effects, RollResult? Last = null, bool HalfDamage = false, Stat? SaveStat = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int HookDepth = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] PassiveRef? Passive = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Resolution = null) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        if (Effects.IsDefaultOrEmpty)
            return;
        if (Target is { } target && w.Unit(target).Life is LifeState.Dead or LifeState.Out)
            return;
        if (HookDepth == 0 && w.Unit(Source).Life != LifeState.Up)
            return;
        w.HookDepth = HookDepth;
        if (Passive is { } passive)
        {
            if (!Hooks.TrySpend(w, passive, rules))
                return;
            w.Emit(new PassiveTriggered(passive.Owner, passive.Id));
        }
        var resolution = Resolution ?? w.NewResolution();
        if (Effects.Length > 1)
            w.PushFront(this with { Effects = Effects.RemoveAt(0), Passive = null, Resolution = resolution });

        var ability = Ability is { } id ? rules.Abilities.GetValueOrDefault(id) : null;
        var ctx = new EffectContext(w, rules, Source, Target, Aim, ability, Last, HalfDamage, SaveStat, resolution);
        BlockRegistry.For(Effects[0]).Run(Effects[0], ctx);
        if (HookDepth == 0 && ability is not null && !ability.IsWeaponAttack && !Metamagic.IsMetamagicAbility(ability)
            && !w.State.Queue.Any(s => s is RunEffectsStep r && r.HookDepth == 0 && r.Source == Source && r.Ability == Ability))
            Metamagic.ConsumeAll(w, Source);
    }
}
