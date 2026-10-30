using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The rules engine's entry points: pure functions from a state and a command to the next state and its events.
/// The same state and command always give the same result.
/// </summary>
public static partial class Battle
{
    /// <summary>
    /// Creates the battle (resources full, HP at max) and starts round 1 with the first unit's turn.
    /// Emits <see cref="RoundStarted"/> and <see cref="TurnStarted"/>. The map must be in <paramref name="rules"/>.
    /// </summary>
    public static Resolution Start(BattleSetup setup, RulesPack rules, ulong seed)
    {
        if (rules.MapFor(setup.Map.Id) is null)
            throw new ArgumentException($"The rules pack has no map '{setup.Map.Id}'.", nameof(rules));

        var units = ImmutableSortedDictionary.CreateBuilder<UnitId, UnitState>();
        foreach (var placement in setup.Units)
        {
            if (!units.TryAdd(placement.Id, NewUnit(placement)))
                throw new ArgumentException($"Duplicate unit id '{placement.Id}'.", nameof(setup));
        }

        var state = new BattleState(
            ContentHash: rules.Hash, MapId: setup.Map.Id, Units: units.ToImmutable(), Round: 0, Order: [], TurnIndex: 0,
            Rng: Rng.Seeded(seed), Queue: [], Pending: null, HeightDelta: ImmutableSortedDictionary<Tile, int>.Empty,
            Zones: [], Objectives: new ObjectiveState(setup.Objectives, Ordinal.Map<int>()), Flags: Ordinal.Set(),
            Counters: Ordinal.Map<int>(), FiredTriggers: Ordinal.Set(), Outcome: null, Injuries: [], ItemsUsed: [],
            NextSerial: 0, StartedThisRound: [], DelayedThisRound: [], Xp: setup.Xp,
            Loot: setup.Loot.IsDefault ? [] : setup.Loot, Difficulty: setup.Difficulty,
            Triggers: setup.Triggers.IsDefault ? [] : setup.Triggers);

        var w = new BattleWriter(state, rules);
        BeginTurn(w, rules);
        Settle(w, rules);
        return new Resolution(w.Freeze(), w.Events, null);
    }

    /// <summary>
    /// Validates <paramref name="command"/>, resolves it and, if the turn ended, starts the next one. A refused command
    /// returns the same <paramref name="state"/> instance, no events and a <see cref="Rejection"/>.
    /// </summary>
    public static Resolution Apply(BattleState state, Command command, RulesPack rules)
    {
        if (state.Outcome is not null)
            return Reject(state, RejectCode.BattleOver);
        if (state.Pending is not null && command is not React)
            return Reject(state, RejectCode.ReactionPending);
        if (command is React)
            throw new NotSupportedException($"{nameof(React)} arrives in Task 5/6");
        if (state.Active != command.Actor)
            return Reject(state, RejectCode.NotYourTurn);

        Step step;
        switch (command)
        {
            case EndTurn end:
                step = new EndTurnStep(end.Actor);
                break;
            case Delay delay:
                if (!CanDelay(state, delay, out var to))
                    return Reject(state, RejectCode.CantDelay);
                step = new DelayStep(delay.Actor, to);
                break;
            case Move or Use:
                throw new NotSupportedException($"{command.GetType().Name} arrives in Task 5/6");
            default:
                throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command));
        }

        var w = new BattleWriter(state, rules);
        w.PushFront(step);
        Settle(w, rules);
        return new Resolution(w.Freeze(), w.Events, null);
    }

    private static Resolution Reject(BattleState state, RejectCode code) => new(state, [], Rejection.Of(code));

    /// <summary>
    /// Delay is allowed at the start of the unit's turn (nothing spent), once per round, by at least one slot, and only
    /// when a later slot exists; <paramref name="to"/> is the target index, clamped to the end of the order.
    /// </summary>
    private static bool CanDelay(BattleState state, Delay delay, out int to)
    {
        to = (int)Math.Min((long)state.TurnIndex + delay.Slots, state.Order.Length - 1);
        var unit = state.Units[delay.Actor];
        var untouched = unit.Economy.MoveLeft >= unit.Sheet.Move && unit.Economy.Action && unit.Economy.Bonus;
        return delay.Slots >= 1 && to > state.TurnIndex && untouched && !state.DelayedThisRound.Contains(delay.Actor);
    }

    /// <summary>Drains the queue until it is empty, a decision is pending or the battle is over.</summary>
    private static void Settle(BattleWriter w, RulesPack rules)
    {
        while (w.State.Pending is null && w.State.Outcome is null && w.PopFront() is { } step)
            step.Run(w, rules);
    }

    /// <summary>
    /// Starts the turn at <see cref="BattleState.TurnIndex"/>. When the order has run out, first starts a new round: the
    /// order is re-sorted, <see cref="BattleState.StartedThisRound"/> and <see cref="BattleState.DelayedThisRound"/> are
    /// cleared and <see cref="RoundStarted"/> is emitted. A unit's first start this round gets a fresh economy; a resumed
    /// (delayed) turn gets Move, Action and Bonus back but keeps its Reaction as it is, and skips turn-start effects.
    /// </summary>
    internal static void BeginTurn(BattleWriter w, RulesPack rules)
    {
        if (w.State.TurnIndex >= w.State.Order.Length)
        {
            var round = w.State.Round + 1;
            var order = TurnOrder.Sort(w.State, rules);
            w.State = w.State with
            {
                Round = round, Order = order, TurnIndex = 0, StartedThisRound = [], DelayedThisRound = [],
            };
            w.Emit(new RoundStarted(round, order));
        }

        // Task 8: skip (TurnSkipped) units that can't act and move on to the next.
        if (w.State.Active is not { } id)
            return;
        var unit = w.Unit(id);
        var resumed = w.State.StartedThisRound.Contains(id);
        var fresh = Economy.Fresh(unit.Sheet);
        w.Set(unit with { Economy = resumed ? fresh with { Reaction = unit.Economy.Reaction } : fresh });
        w.State = w.State with { StartedThisRound = w.State.StartedThisRound.Add(id) };
        w.Emit(new TurnStarted(id, w.State.Round));

        if (!resumed)
        {
            // Task 7: turn-start hooks and condition ticks go here, so they run only on a unit's first start this round.
        }
    }

    private static UnitState NewUnit(UnitPlacement p) => new(
        Id: p.Id, Sheet: p.Sheet, Controller: p.Controller, Pos: p.At, Hp: p.Sheet.MaxHp, TempHp: 0,
        Resources: p.Sheet.Resources.WithComparers(StringComparer.Ordinal), Conditions: [], Economy: Economy.Fresh(p.Sheet),
        Life: LifeState.Up, Countdown: 0, UsesLeft: Ordinal.Map<int>(), Concentrating: null, Bag: p.Bag.IsDefault ? [] : p.Bag,
        ItemsUsedByAi: 0, SummonedBy: null, Protected: p.Protected);
}
