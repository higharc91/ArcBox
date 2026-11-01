using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// <c>"turnOrder"</c>: changes this round's order (<see cref="BattleState.Order"/>) for a target whose turn is still to
/// come; one that has had or is having its turn this round (or isn't in the order yet) stays put, so no turn is repeated
/// or skipped. <c>"delay"</c> moves it the slots later (at most to the end), <c>"hasten"</c> the slots earlier (at most to
/// the next turn), <c>"swap"</c> trades places with the unit the slots after it (at most the last). Emits
/// <see cref="TurnOrderChanged"/> when the order changes. Throws on any other op.
/// </summary>
public sealed class TurnOrderBlock : EffectBlock<TurnOrderEffect>
{
    /// <summary>The ops content may use.</summary>
    public static readonly ImmutableArray<string> Ops = ["delay", "hasten", "swap"];

    /// <inheritdoc />
    public override string Key => "turnOrder";

    /// <inheritdoc />
    protected override string? Describe(TurnOrderEffect def, int level, RulesPack rules)
    {
        var places = $"{def.Slots} {(def.Slots == 1 ? "place" : "places")}";
        return def.Op switch
        {
            "delay" => $"delays target by {places} in the turn order",
            "hasten" => $"moves target {places} earlier in the turn order",
            "swap" => $"swaps target's place with the unit {places} after it in the turn order",
            _ => null,
        };
    }

    /// <inheritdoc />
    protected override void Run(TurnOrderEffect def, EffectContext ctx)
    {
        if (!Ops.Contains(def.Op))
            throw new InvalidOperationException($"'turnOrder' op must be \"delay\", \"hasten\" or \"swap\", not '{def.Op}'.");
        var target = ctx.TargetUnit(Key).Id;
        var s = ctx.W.State;
        var order = s.Order;
        var from = order.IndexOf(target);
        if (from <= s.TurnIndex)
            return;

        var last = order.Length - 1;
        var slots = Math.Max(0, def.Slots);
        order = def.Op switch
        {
            "delay" => order.RemoveAt(from).Insert(Math.Min(from + slots, last), target),
            "hasten" => order.RemoveAt(from).Insert(Math.Max(from - slots, s.TurnIndex + 1), target),
            _ => order.SetItem(from, order[Math.Min(from + slots, last)]).SetItem(Math.Min(from + slots, last), target),
        };
        if (order.SequenceEqual(s.Order))
            return;
        ctx.W.State = s with { Order = order };
        ctx.W.Emit(new TurnOrderChanged(order));
    }
}
