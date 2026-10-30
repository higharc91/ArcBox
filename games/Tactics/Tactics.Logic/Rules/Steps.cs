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
