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
    /// Emits <see cref="RoundStarted"/> and <see cref="TurnStarted"/>; then every unit's <see cref="HookKind.OnBattleStart"/>
    /// effects resolve, ahead of the round-start and turn-start ones. The map must be in <paramref name="rules"/>.
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
        if (w.State.Outcome is null)
            Hooks.FireAll(w, HookKind.OnBattleStart, rules);
        return Settled(w, rules);
    }

    /// <summary>
    /// Validates <paramref name="command"/>, resolves it and, if the turn ended, starts the next one (skipping units that
    /// can't act). A refused command returns the same <paramref name="state"/> instance, no events and a
    /// <see cref="Rejection"/>; once the battle is over every command is refused with <see cref="RejectCode.BattleOver"/>.
    /// With <see cref="Invariants.Enabled"/>, the new state is checked (<see cref="Invariants.Check"/>).
    /// <para>With <paramref name="aiFinished"/> (a command of the plan the AI made for a player whose time ran out, spec
    /// §5.5), the command's events, up to and including the actor's <see cref="TurnEnded"/>, carry
    /// <see cref="BattleEvent.AiFinished"/>; the next turn's events don't. An item it uses counts as the AI's
    /// (<see cref="PayCostStep.ByAi"/>), as it does whenever an AI-controlled unit acts.</para>
    /// </summary>
    public static Resolution Apply(BattleState state, Command command, RulesPack rules, bool aiFinished = false)
    {
        var w = new BattleWriter(state, rules);
        if (Prepare(w, command, rules, aiFinished) is { } refused)
            return new Resolution(state, [], refused);
        var resolution = Settled(w, rules);
        return aiFinished ? resolution with { Events = Stamped(resolution.Events, command.Actor) } : resolution;
    }

    /// <summary>
    /// <paramref name="events"/> with those up to and including <paramref name="actor"/>'s <see cref="TurnEnded"/> (all of
    /// them, without one) flagged <see cref="BattleEvent.AiFinished"/>.
    /// </summary>
    private static ImmutableArray<BattleEvent> Stamped(ImmutableArray<BattleEvent> events, UnitId actor)
    {
        var stamped = events.ToBuilder();
        for (var i = 0; i < stamped.Count; i++)
        {
            stamped[i] = stamped[i] with { AiFinished = true };
            if (stamped[i] is TurnEnded ended && ended.Unit == actor)
                break;
        }
        return stamped.ToImmutable();
    }

    /// <summary>
    /// Whether <see cref="Apply"/> would accept <paramref name="command"/> in <paramref name="state"/>: null when it would,
    /// otherwise the <see cref="Rejection"/> it would give. Nothing is resolved and no die is rolled, so the AI can test a
    /// command against a state it only imagines.
    /// </summary>
    public static Rejection? Check(BattleState state, Command command, RulesPack rules)
        => Prepare(new BattleWriter(state, rules), command, rules, aiFinished: false);

    /// <summary>
    /// Validates <paramref name="command"/> and, if it is accepted, queues its steps on <paramref name="w"/> (rolling
    /// nothing); returns the rejection otherwise. A <see cref="React"/> goes to <see cref="PrepareReact"/>.
    /// <paramref name="aiFinished"/> marks a command the AI chose for a timed-out player (see <see cref="Apply"/>).
    /// </summary>
    private static Rejection? Prepare(BattleWriter w, Command command, RulesPack rules, bool aiFinished)
    {
        var state = w.State;
        if (state.Outcome is not null)
            return Rejection.Of(RejectCode.BattleOver);
        if (command is React react)
            return PrepareReact(w, react);
        if (state.Pending is not null)
            return Rejection.Of(RejectCode.ReactionPending);
        if (state.Active != command.Actor)
            return Rejection.Of(RejectCode.NotYourTurn);

        switch (command)
        {
            case EndTurn end:
                w.PushFront(new EndTurnStep(end.Actor));
                return null;
            case Delay delay:
                if (!CanDelay(w, delay, rules, out var to))
                    return Rejection.Of(RejectCode.CantDelay);
                w.PushFront(new DelayStep(delay.Actor, to));
                return null;
            case Use use:
                return PlanUse(w, use, rules, aiFinished);
            case Move move:
                return Movement.Plan(w, move);
            default:
                throw new ArgumentException($"Unknown command {command.GetType().Name}.", nameof(command));
        }
    }

    /// <summary>
    /// Answers the pending <see cref="ReactionOffer"/> (<see cref="RejectCode.NothingToReact"/> without one) as its reactor
    /// (<see cref="RejectCode.NotYourTurn"/> otherwise) with one of its options, matched by id
    /// (<see cref="RejectCode.InvalidTarget"/> otherwise). Clears the offer, emits <see cref="Reacted"/>, queues the
    /// reaction unless it is <c>"pass"</c>; settling then resumes the queue where it paused.
    /// </summary>
    private static Rejection? PrepareReact(BattleWriter w, React react)
    {
        if (w.State.Pending is not ReactionOffer offer)
            return Rejection.Of(RejectCode.NothingToReact);
        if (react.Actor != offer.Reactor)
            return Rejection.Of(RejectCode.NotYourTurn);
        if (offer.Options.FirstOrDefault(o => o.Id == react.Choice.Id) is not { } choice)
            return Rejection.Of(RejectCode.InvalidTarget);

        w.State = w.State with { Pending = null };
        w.Emit(new Reacted(offer.Reactor, choice.Id));
        if (choice.Id != Reactions.Pass)
            w.PushFront(new ReactionStep(offer.Reactor, choice));
        return null;
    }

    /// <summary>
    /// Delay is allowed at the start of the unit's turn (nothing spent: its effective Move, as <see cref="Economy.Fresh"/>
    /// gives it, still all there), once per round, by at least one slot, and only when a later slot exists;
    /// <paramref name="to"/> is the target index, clamped to the end of the order.
    /// </summary>
    private static bool CanDelay(BattleWriter w, Delay delay, RulesPack rules, out int to)
    {
        var state = w.State;
        to = (int)Math.Min((long)state.TurnIndex + delay.Slots, state.Order.Length - 1);
        var unit = state.Units[delay.Actor];
        var move = Modifiers.Move(unit.Sheet, Modifiers.For(w, unit.Id, rules));
        var untouched = unit.Economy.MoveLeft >= move && unit.Economy.Action && unit.Economy.Bonus;
        return delay.Slots >= 1 && to > state.TurnIndex && untouched && !state.DelayedThisRound.Contains(delay.Actor);
    }

    /// <summary>
    /// Validates <paramref name="use"/> in order (able to act, known ability, cost, target kind, range, line of sight) and,
    /// if it passes, queues <see cref="PayCostStep"/>, then the ability's effects in their authored order, split into
    /// runs of consecutive blocks: a run of tile blocks (<see cref="EffectDef.OnTile"/>) runs once at the aim with no target
    /// unit, even when the area holds no units; a run of other blocks runs once per affected unit in sorted
    /// <see cref="UnitId"/> order (one <see cref="RunEffectsStep"/> each). So <c>[attack, teleport]</c> attacks every target,
    /// then teleports. Returns the rejection otherwise, leaving the queue untouched. An item use is the AI's
    /// (<see cref="PayCostStep.ByAi"/>) when the user is AI-controlled or <paramref name="aiFinished"/> is set.
    /// </summary>
    private static Rejection? PlanUse(BattleWriter w, Use use, RulesPack rules, bool aiFinished)
    {
        var user = w.Unit(use.Actor);
        if (!Modifiers.CanAct(w.State, user.Id, rules))
            return Rejection.Of(RejectCode.Incapacitated);
        if (!rules.Abilities.TryGetValue(use.Ability, out var ability) || !Knows(user.Sheet, ability.Id)
            || rules.Items.Values.Any(i => i.Use == ability.Id))   // an item's ability runs only through common.use_item
            return Rejection.Of(RejectCode.UnknownAbility);
        if (use.Item is not null || ability.Id == UseItem)
        {
            // common.use_item runs the named item's own ability (its cost, target and effects) and uses one up.
            if (ability.Id != UseItem || use.Item is not { } item || !rules.Items.TryGetValue(item, out var itemDef)
                || !user.Bag.Any(s => s.Item == item && s.Count > 0) || !rules.Abilities.TryGetValue(itemDef.Use, out var itemAbility))
                return Rejection.Of(RejectCode.NoItem);
            ability = itemAbility;
        }
        if (CostRefusal(user, ability) is { } cost)
            return cost;
        if (ResolveTarget(w, user, ability, use.Target, out var aim, out var targets) is { } target)
            return target;

        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        if (ability.IsWeaponAttack)
            effects = [.. Enumerable.Repeat(effects, Math.Max(1, user.Sheet.Attacks)).SelectMany(e => e)];

        var byAi = use.Item is not null && (aiFinished || user.Controller is AiControlled);
        var steps = new List<Step> { new PayCostStep(user.Id, ability.Id, use.Target, use.Item, byAi) };
        for (var start = 0; start < effects.Length;)
        {
            var onTile = effects[start].OnTile;
            var end = start;
            while (end < effects.Length && effects[end].OnTile == onTile)
                end++;
            var run = effects[start..end];
            if (onTile)
                steps.Add(new RunEffectsStep(user.Id, null, aim, ability.Id, run));
            else
                steps.AddRange(targets.Select(t => new RunEffectsStep(user.Id, t, aim, ability.Id, run)));
            start = end;
        }
        w.PushFront([.. steps]);
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
    /// <item>Range: Manhattan distance ≤ range (+ the high-ground bonus when the range is over 1 tile) and ≥ <see cref="TargetDef.MinRange"/>, within
    /// <see cref="TargetDef.HeightTolerance"/> when set. A weapon attack uses the sheet's weapon: melee reach
    /// (<see cref="Reach.Melee"/>) or its range, needing sight when ranged.</item>
    /// <item>Affected units are in the area (anywhere for <c>all_*</c>), Up or Downed (only Downed for
    /// <c>downed_ally</c>), on the side the kind picks: <c>ally</c>/<c>downed_ally</c> the user's side, <c>any_unit</c> any
    /// side, everything else (enemy, tile, a self-centred area) the other sides, plus allies with
    /// <see cref="AreaDef.FriendlyFire"/>. The user counts only with <see cref="AreaDef.IncludeSelf"/>, except a
    /// <c>self</c> ability on a single or self area, which affects just the user. A single-target unit ability needs one
    /// fitting unit at the aim.</item>
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
            var downedOnly = def.Kind == TargetKind.DownedAlly;
            affected = [.. s.Units.Values
                .Where(u => (downedOnly ? u.Life == LifeState.Downed : DownedRules.InBattle(u))
                            && (everywhere || tiles.Contains(u.Pos)) && Affects(side, user, u, area))
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
            var effectiveRange = def.Range >= 1 && !Metamagic.IsMetamagicAbility(ability) && Metamagic.Has(user, Metamagic.Distant)
                ? def.Range + 2
                : def.Range;
            ranged = effectiveRange > 1;   // melee and touch (range 1) get no high-ground range
            var heightOk = def.HeightTolerance is not { } tolerance
                           || Math.Abs(terrain.HeightAt(user.Pos) - terrain.HeightAt(aim)) <= tolerance;
            if (distance > effectiveRange + (ranged ? rangeBonus : 0) || distance < def.MinRange || !heightOk)
                return Rejection.Of(RejectCode.OutOfRange);
            los = def.Los;
        }

        var standing = los
            ? s.Units.Values.Where(u => u.Life == LifeState.Up).Select(u => u.Pos).ToImmutableSortedSet()
            : ImmutableSortedSet<Tile>.Empty;
        if (los)
        {
            if (!Sight.Trace(terrain, user.Pos, aim, standing, ranged, ZoneRules.Obscured(s)).HasLineOfSight)
                return Rejection.Of(RejectCode.NoLineOfSight);
        }

        if (!ability.IsWeaponAttack && !Metamagic.IsMetamagicAbility(ability) && Metamagic.Has(user, Metamagic.Twinned)
            && area.Shape == AreaShape.Single && def.Kind is TargetKind.Enemy or TargetKind.Ally or TargetKind.AnyUnit
            && affected.Length == 1)
        {
            var first = s.Units[affected[0]];
            var effectiveRange = def.Range >= 1 && Metamagic.Has(user, Metamagic.Distant) ? def.Range + 2 : def.Range;
            var obscured = los ? ZoneRules.Obscured(s) : ImmutableSortedSet<Tile>.Empty;
            var second = s.Units.Values
                .Where(u => u.Id != first.Id && u.Id != user.Id && DownedRules.InBattle(u) && u.Sheet.Side == first.Sheet.Side)
                .Where(u =>
                {
                    var d = Reach.Distance(user.Pos, u.Pos);
                    var rb = effectiveRange > 1 ? Reach.RangeBonus(terrain, user.Pos, u.Pos) : 0;
                    var hOk = def.HeightTolerance is not { } tol
                              || Math.Abs(terrain.HeightAt(user.Pos) - terrain.HeightAt(u.Pos)) <= tol;
                    return d <= effectiveRange + rb && d >= def.MinRange && hOk
                           && (!los || Sight.Trace(terrain, user.Pos, u.Pos, standing, effectiveRange > 1, obscured).HasLineOfSight);
                })
                .OrderBy(u => Reach.Distance(first.Pos, u.Pos))
                .ThenBy(u => u.Id)
                .FirstOrDefault();
            if (second is not null)
                affected = affected.Add(second.Id);
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
    /// <summary>The built-in action that uses an item from the bag (<see cref="Use.Item"/>).</summary>
    public static readonly AbilityId UseItem = new("common.use_item");

    private static bool Knows(UnitSheet sheet, AbilityId ability)
        => ability.Value.StartsWith("common.", StringComparison.Ordinal) || sheet.Abilities.Contains(ability);

    /// <summary>Why <paramref name="unit"/> can't pay for <paramref name="ability"/>, or null when it can.</summary>
    internal static Rejection? CostRefusal(UnitState unit, AbilityDef ability)
    {
        var cost = ability.Cost;
        var economy = unit.Economy;
        if (cost.Action > 0 && !economy.Action && economy.ExtraActions <= 0 && !Metamagic.Quickens(unit, ability))
            return Rejection.Of(RejectCode.NoAction);
        if ((cost.Bonus > 0 || Metamagic.IsFreeQuickened(ability)) && !economy.Bonus)
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

    /// <summary>
    /// More steps than this in one command means the battle can't settle (e.g. every unit is incapacitated for good);
    /// <see cref="Settle"/> throws <see cref="RulesInvariantException"/> rather than loop forever.
    /// </summary>
    private const int MaxStepsPerCommand = 100_000;

    /// <summary>
    /// Settles the queue; once it is empty, the hit numbering (<see cref="BattleState.NextResolution"/>,
    /// <see cref="BattleState.DownedHits"/>) starts over. Then checks the invariants if <see cref="Invariants.Enabled"/>,
    /// and freezes the result.
    /// </summary>
    private static Resolution Settled(BattleWriter w, RulesPack rules)
    {
        Settle(w, rules);
        if (w.State.Queue.IsEmpty && (w.State.NextResolution != 0 || !w.State.DownedHits.IsEmpty))
            w.State = w.State with { NextResolution = 0, DownedHits = [] };
        if (Invariants.Enabled)
            Invariants.Check(w.State, rules, w.Terrain);
        return new Resolution(w.Freeze(), w.Events, null);
    }

    /// <summary>
    /// Drains the queue until it is empty, a decision is pending or the battle is over. The objectives are checked before
    /// the first step and after every step (<see cref="Objectives.Check"/>), so a battle ends as soon as it is decided.
    /// Each step starts at hook depth 0; steps that carry a depth set it themselves.
    /// </summary>
    private static void Settle(BattleWriter w, RulesPack rules)
    {
        for (var steps = 0; !Objectives.Check(w) && w.State.Pending is null && w.PopFront() is { } step; steps++)
        {
            if (steps >= MaxStepsPerCommand)
                throw new RulesInvariantException($"The battle ran {MaxStepsPerCommand} steps without settling.");
            w.HookDepth = 0;
            step.Run(w, rules);
        }
    }

    /// <summary>
    /// Starts the turn at <see cref="BattleState.TurnIndex"/>. When the order has run out, first starts a new round: the
    /// order is re-sorted (taking in the units <see cref="BattleState.Joining"/> it), <see cref="BattleState.Joining"/>,
    /// <see cref="BattleState.StartedThisRound"/>, <see cref="BattleState.DelayedThisRound"/> and every unit's
    /// <see cref="UnitState.UsedThisRound"/> are cleared, <see cref="RoundStarted"/> is emitted, zones whose owner has left
    /// the battle tick (<see cref="ZoneRules.TickOrphans"/>) and every Downed unit's countdown ticks
    /// (<see cref="DownedRules.TickRound"/>; if that decides the battle, nothing else happens).
    /// Every turn start clears every unit's <see cref="UnitState.UsedThisTurn"/>.
    /// <para>A unit that can't act (<see cref="SkipReason"/>) has its turn skipped: <see cref="TurnSkipped"/>, then a
    /// <see cref="SkipTurnStep"/> ends it (conditions still tick) and hands on to the next unit.</para>
    /// <para>Otherwise a unit's first start this round gets a fresh economy (<see cref="Economy.Fresh"/> with its
    /// modifiers); a resumed (delayed) turn gets Move, Action and Bonus back but keeps its Reaction as it is.
    /// <see cref="TurnStarted"/> is emitted.</para>
    /// Then the effects of every unit's <see cref="HookKind.OnRoundStart"/> hooks (new round only) and of the unit's
    /// <see cref="HookKind.OnTurnStart"/> hooks (first start only, not a resumed or skipped turn; burning and the like are
    /// turn-start condition passives) are queued, in that order, ahead of a skipped turn's end.
    /// </summary>
    internal static void BeginTurn(BattleWriter w, RulesPack rules)
    {
        var newRound = w.State.TurnIndex >= w.State.Order.Length;
        if (newRound)
        {
            var round = w.State.Round + 1;
            var order = TurnOrder.Sort(w.State, rules);
            w.State = w.State with
            {
                Round = round, Order = order, TurnIndex = 0, StartedThisRound = [], DelayedThisRound = [], Joining = [],
            };
            ClearUsed(w, round: true);
            w.Emit(new RoundStarted(round, order));
            ZoneRules.TickOrphans(w);
            DownedRules.TickRound(w);
            if (Objectives.Check(w))
                return;
        }

        if (w.State.Active is not { } id)
            return;
        ClearUsed(w, round: false);
        var unit = w.Unit(id);
        var resumed = w.State.StartedThisRound.Contains(id);
        w.State = w.State with { StartedThisRound = w.State.StartedThisRound.Add(id) };
        var hooks = new List<Step>();
        if (newRound)
            Hooks.CollectAll(w, HookKind.OnRoundStart, rules, hooks);

        if (SkipReason(w.State, unit, rules) is { } reason)
        {
            w.Emit(new TurnSkipped(id, reason));
            w.PushFront([.. hooks, new SkipTurnStep(id)]);
            return;
        }

        var fresh = Economy.Fresh(unit.Sheet, Modifiers.For(w, id, rules));
        w.Set(unit with { Economy = resumed ? fresh with { Reaction = unit.Economy.Reaction } : fresh });
        w.Emit(new TurnStarted(id, w.State.Round));
        if (!resumed)
            Hooks.Collect(w, HookKind.OnTurnStart, new HookArgs(id), rules, hooks);
        if (hooks.Count > 0)
            w.PushFront([.. hooks]);
    }

    /// <summary>
    /// Why <paramref name="unit"/>'s turn is skipped, or null when it can act: <c>"downed"</c>, <c>"out"</c> or
    /// <c>"dead"</c> by its life state, <c>"incapacitated"</c> by its modifiers.
    /// </summary>
    public static string? SkipReason(BattleState state, UnitState unit, RulesPack rules) => unit.Life switch
    {
        LifeState.Downed => "downed",
        LifeState.Out => "out",
        LifeState.Dead => "dead",
        _ => Modifiers.For(state, unit.Id, rules).Incapacitated ? "incapacitated" : null,
    };

    /// <summary>Empties every unit's <see cref="UnitState.UsedThisRound"/> (<paramref name="round"/>) or
    /// <see cref="UnitState.UsedThisTurn"/>.</summary>
    private static void ClearUsed(BattleWriter w, bool round)
    {
        foreach (var unit in w.State.Units.Values)
        {
            if (round && !unit.UsedThisRound.IsEmpty)
                w.Set(unit with { UsedThisRound = Ordinal.Set() });
            else if (!round && !unit.UsedThisTurn.IsEmpty)
                w.Set(unit with { UsedThisTurn = Ordinal.Set() });
        }
    }

    private static UnitState NewUnit(UnitPlacement p) => new(
        Id: p.Id, Sheet: p.Sheet, Controller: p.Controller, Pos: p.At, Hp: p.Sheet.MaxHp, TempHp: 0,
        Resources: p.Sheet.Resources.WithComparers(StringComparer.Ordinal), Conditions: [], Economy: Economy.Fresh(p.Sheet),
        Life: LifeState.Up, Countdown: 0, UsesLeft: Ordinal.Map<int>(), Concentrating: null, Bag: p.Bag.IsDefault ? [] : p.Bag,
        ItemsUsedByAi: 0, SummonedBy: null, Protected: p.Protected, UsedThisTurn: Ordinal.Set(), UsedThisRound: Ordinal.Set());
}
