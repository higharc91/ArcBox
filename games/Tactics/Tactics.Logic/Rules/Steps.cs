using System.Text.Json.Serialization;

namespace Tactics.Logic;

/// <summary>
/// One unit of resolution on the battle's queue. <see cref="Battle.Apply"/> turns a command into steps and runs them in
/// order; a step may push further steps to the front, or pause the queue by setting <see cref="BattleState.Pending"/>.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(EndTurnStep), nameof(EndTurnStep))]
[JsonDerivedType(typeof(DelayStep), nameof(DelayStep))]
public abstract record Step
{
    /// <summary>Resolves the step against the writer.</summary>
    internal abstract void Run(BattleWriter w, RulesPack rules);
}

/// <summary>Ends <see cref="Unit"/>'s turn and hands it to the next unit in the order.</summary>
public sealed record EndTurnStep(UnitId Unit) : Step
{
    internal override void Run(BattleWriter w, RulesPack rules)
    {
        w.Emit(new TurnEnded(Unit));
        w.State = w.State with { TurnIndex = w.State.TurnIndex + 1 };
        w.TurnOver = true;
    }
}

/// <summary>
/// Moves <see cref="Unit"/> to <see cref="ToIndex"/> in this round's order and hands the turn to the unit now at its old
/// place. The delayed unit's turn starts afresh when its new place comes up.
/// </summary>
public sealed record DelayStep(UnitId Unit, int ToIndex) : Step
{
    internal override void Run(BattleWriter w, RulesPack rules)
    {
        var order = w.State.Order;
        w.State = w.State with { Order = order.RemoveAt(order.IndexOf(Unit)).Insert(ToIndex, Unit) };
        w.Emit(new Delayed(Unit, ToIndex));
        w.TurnOver = true;
    }
}
