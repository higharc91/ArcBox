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
            throw new NotSupportedException($"{nameof(React)} arrives in Task 6");
        if (state.Active != command.Actor)
            return Reject(state, RejectCode.NotYourTurn);

        var w = new BattleWriter(state, rules);
        switch (command)
        {
            case EndTurn end:
                w.PushFront(new EndTurnStep(end.Actor));
                break;
            case Delay delay:
                if (!CanDelay(state, delay, out var to))
                    return Reject(state, RejectCode.CantDelay);
                w.PushFront(new DelayStep(delay.Actor, to));
                break;
            case Use use:
                if (PlanUse(w, use, rules) is { } refused)
                    return new Resolution(state, [], refused);
                break;
            case Move:
                throw new NotSupportedException($"{command.GetType().Name} arrives in Task 6");
            default:
                throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command));
        }

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

    /// <summary>
    /// Validates <paramref name="use"/> in order (known ability, cost, target kind, range, line of sight) and,
    /// if it passes, queues <see cref="PayCostStep"/> and one <see cref="RunEffectsStep"/> per affected unit in sorted
    /// <see cref="UnitId"/> order. Returns the rejection otherwise, leaving the queue untouched.
    /// </summary>
    private static Rejection? PlanUse(BattleWriter w, Use use, RulesPack rules)
    {
        var user = w.Unit(use.Actor);
        if (!rules.Abilities.TryGetValue(use.Ability, out var ability) || !Knows(user.Sheet, ability.Id))
            return Rejection.Of(RejectCode.UnknownAbility);
        if (CostRefusal(user, ability) is { } cost)
            return cost;
        if (ResolveTarget(w, user, ability, use.Target, out var aim, out var targets) is { } target)
            return target;

        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        if (ability.IsWeaponAttack)
            effects = [.. Enumerable.Repeat(effects, Math.Max(1, user.Sheet.Attacks)).SelectMany(e => e)];

        // Task 6/10: tile effects (teleport, zones) on a Tile/EmptyTile ability with no unit in the area.
        w.PushFront([
            new PayCostStep(user.Id, ability.Id, use.Target),
            .. targets.Select(t => new RunEffectsStep(user.Id, t, aim, ability.Id, effects)),
        ]);
        return null;
    }

    /// <summary>
    /// Where <paramref name="ability"/> lands and whom it affects, or why <paramref name="target"/> is refused
    /// (<see cref="RejectCode.InvalidTarget"/>, then <see cref="RejectCode.OutOfRange"/>, then
    /// <see cref="RejectCode.NoLineOfSight"/>).
    /// <list type="bullet">
    /// <item>The aim is <see cref="Target.Tile"/>, else the target unit's tile. A <c>self</c> ability aims at the user's own
    /// tile (a line or cone at <see cref="Target.Tile"/>, which only sets its direction), and an <c>all_allies</c> or
    /// <c>all_enemies</c> area needs no aim; both skip the range and sight checks.</item>
    /// <item>Range: Manhattan distance ≤ range + the high-ground bonus and ≥ <see cref="TargetDef.MinRange"/>, within
    /// <see cref="TargetDef.HeightTolerance"/> when set. A weapon attack uses the sheet's weapon: melee reach
    /// (<see cref="Reach.Melee"/>) or its range, needing sight when ranged.</item>
    /// <item>Affected units stand in the area (anywhere for <c>all_*</c>), Up (Downed for <c>downed_ally</c>), on the side
    /// the kind picks: <c>ally</c>/<c>downed_ally</c> the user's side, <c>any_unit</c> any side, everything else (enemy,
    /// tile, a self-centred area) the other sides, plus allies with <see cref="AreaDef.FriendlyFire"/>. The user counts only
    /// with <see cref="AreaDef.IncludeSelf"/>, except a <c>self</c> ability on a single or self area, which affects just the
    /// user. A single-target unit ability needs one fitting unit at the aim.</item>
    /// </list>
    /// </summary>
    internal static Rejection? ResolveTarget(BattleWriter w, UnitState user, AbilityDef ability, Target target, out Tile aim,
        out ImmutableArray<UnitId> affected)
    {
        aim = user.Pos;
        affected = [];
        var s = w.State;
        var terrain = w.Terrain;
        var def = ability.Target;
        var area = def.Area;
        var everywhere = area.Shape is AreaShape.AllAllies or AreaShape.AllEnemies;
        var invalid = Rejection.Of(RejectCode.InvalidTarget);

        UnitState? picked = null;
        if (target.Unit is { } id)
        {
            if (!s.Units.TryGetValue(id, out picked) || picked.Life is LifeState.Dead or LifeState.Out)
                return invalid;
        }

        var self = def.Kind == TargetKind.Self;
        if (self && picked is not null && picked.Id != user.Id)
            return invalid;
        if (self && area.Shape is AreaShape.Line or AreaShape.Cone)
            aim = target.Tile ?? user.Pos;     // only the direction matters: the shape starts at the user
        else if (!self && !everywhere)
        {
            if (target.Tile is { } tile)
                aim = tile;
            else if (picked is not null)
                aim = picked.Pos;
            else
                return invalid;
        }
        if (!terrain.Map.InBounds(aim))
            return invalid;
        var at = aim;
        var occupied = s.Units.Values.Any(u => u.Pos == at && u.Life is LifeState.Up or LifeState.Downed);
        if (def.Kind == TargetKind.EmptyTile && (occupied || !terrain.Walkable(aim)))
            return invalid;

        if (self && area.Shape is AreaShape.Single or AreaShape.Self)
            affected = [user.Id];
        else
        {
            var side = area.Shape switch
            {
                AreaShape.AllAllies => TargetKind.Ally,
                AreaShape.AllEnemies => TargetKind.Enemy,
                _ => def.Kind,
            };
            var tiles = everywhere ? [] : Areas.Tiles(area, user.Pos, aim, terrain).ToImmutableSortedSet();
            var life = def.Kind == TargetKind.DownedAlly ? LifeState.Downed : LifeState.Up;
            affected = [.. s.Units.Values
                .Where(u => u.Life == life && (everywhere || tiles.Contains(u.Pos)) && Affects(side, user, u, area))
                .Select(u => u.Id)];
            var unitKind = def.Kind is TargetKind.Ally or TargetKind.Enemy or TargetKind.AnyUnit or TargetKind.DownedAlly;
            if (area.Shape == AreaShape.Single && unitKind && affected.IsEmpty)
                return invalid;
        }

        if (self || everywhere)
            return null;

        var distance = Reach.Distance(user.Pos, aim);
        var rangeBonus = Reach.RangeBonus(terrain, user.Pos, aim);
        bool los;
        bool ranged;
        if (ability.IsWeaponAttack)
        {
            var weapon = user.Sheet.Weapon;
            var inReach = weapon.Ranged
                ? distance <= weapon.Range + rangeBonus
                : Reach.Melee(terrain, user.Pos, aim, weapon.Reach);
            if (!inReach)
                return Rejection.Of(RejectCode.OutOfRange);
            (los, ranged) = (weapon.Ranged, weapon.Ranged);
        }
        else
        {
            var heightOk = def.HeightTolerance is not { } tolerance
                           || Math.Abs(terrain.HeightAt(user.Pos) - terrain.HeightAt(aim)) <= tolerance;
            if (distance > def.Range + rangeBonus || distance < def.MinRange || !heightOk)
                return Rejection.Of(RejectCode.OutOfRange);
            (los, ranged) = (def.Los, def.Range > 1);
        }

        if (los)
        {
            var standing = s.Units.Values.Where(u => u.Life == LifeState.Up).Select(u => u.Pos).ToImmutableSortedSet();
            if (!Sight.Trace(terrain, user.Pos, aim, standing, ranged).HasLineOfSight)
                return Rejection.Of(RejectCode.NoLineOfSight);
        }
        return null;
    }

    /// <summary>Whether an area of a <paramref name="side"/>-kind ability from <paramref name="user"/> affects <paramref name="unit"/>.</summary>
    private static bool Affects(TargetKind side, UnitState user, UnitState unit, AreaDef area)
    {
        if (unit.Id == user.Id)
            return area.IncludeSelf;
        var sameSide = unit.Sheet.Side == user.Sheet.Side;
        return side switch
        {
            TargetKind.Ally or TargetKind.DownedAlly => sameSide,
            TargetKind.AnyUnit => true,
            _ => !sameSide || area.FriendlyFire,
        };
    }

    /// <summary>A unit knows the abilities on its sheet and every built-in <c>common.*</c> action.</summary>
    private static bool Knows(UnitSheet sheet, AbilityId ability)
        => ability.Value.StartsWith("common.", StringComparison.Ordinal) || sheet.Abilities.Contains(ability);

    /// <summary>Why <paramref name="unit"/> can't pay for <paramref name="ability"/>, or null when it can.</summary>
    private static Rejection? CostRefusal(UnitState unit, AbilityDef ability)
    {
        var cost = ability.Cost;
        var economy = unit.Economy;
        if (cost.Action > 0 && !economy.Action && economy.ExtraActions <= 0)
            return Rejection.Of(RejectCode.NoAction);
        if (cost.Bonus > 0 && !economy.Bonus)
            return Rejection.Of(RejectCode.NoBonus);
        if (cost.Reaction > 0 && !economy.Reaction)
            return Rejection.Of(RejectCode.NoReaction);
        if (cost.Move > economy.MoveLeft)
            return Rejection.Of(RejectCode.NoMove);
        foreach (var (resource, amount) in (cost.Resource ?? Ordinal.Map<int>()).WithComparers(StringComparer.Ordinal))
            if (unit.Resources.GetValueOrDefault(resource) < amount)
                return Rejection.Of(RejectCode.CantAfford, resource);
        if (cost.Uses is { } uses && unit.UsesLeft.GetValueOrDefault(ability.Id.Value, uses) < 1)
            return Rejection.Of(RejectCode.NoUses);
        return null;
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
