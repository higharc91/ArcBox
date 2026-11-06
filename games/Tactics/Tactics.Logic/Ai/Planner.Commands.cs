using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Planner
{
    /// <summary>
    /// The commands of <paramref name="c"/>, each checked with <see cref="Battle.Check"/> against the state the plan expects
    /// by then, or null when one would be refused. Then, with the action spent, an extra action (Action Surge) and the best
    /// second action if that is worth more than the surge costs (<see cref="Surge"/>), and the best bonus action at the end,
    /// if one is worth its cost.
    /// </summary>
    private ImmutableArray<Command>? Commands(Candidate c)
    {
        var commands = new List<Command>();
        var here = _me.Pos;
        void Act(Usable? usable, Target target, ImmutableArray<UnitId> targets = default)
        {
            if (usable is not null)
                commands.Add(new Use(_me.Id, usable.Command, target, usable.Item) { Targets = targets });
        }
        void Walk(Tile from, ImmutableArray<Tile> path)
        {
            if (!path.IsDefaultOrEmpty)
                commands.Add(new Move(_me.Id, [from, .. path]));
        }
        void Main() => Act(c.Usable, c.Action!.Target, c.Action.Targets);

        switch (c.Kind)
        {
            case Kind.Move:
                Act(c.Escape, new Target());
                Walk(here, _paths.PathTo(c.End));
                break;
            case Kind.MoveAct:
                Act(c.Escape, new Target());
                Walk(here, _paths.PathTo(c.ActTile));
                Act(c.Lead, new Target());   // Twinned or Careful Spell, shaping the spell after it
                Main();
                break;
            case Kind.MoveActMove:
                Act(c.Escape, new Target());   // a disengage lasts the turn: both walks
                Walk(here, _paths.PathTo(c.ActTile));
                Main();
                Walk(c.ActTile, c.Path);
                break;
            case Kind.ActMove:
                Main();
                Act(c.Escape, new Target());
                Walk(here, _paths.PathTo(c.End));
                break;
            case Kind.DashMove:
                Main();
                Act(c.Escape, new Target());
                Walk(here, c.Path);
                break;
            case Kind.HopAct:
                Walk(here, _paths.PathTo(c.Via!.Value));
                Act(c.Lead, new Target(Tile: c.ActTile));
                Main();
                break;
            case Kind.DashAct:
                Act(c.Lead, new Target());
                Walk(here, c.Path);
                Main();
                break;
        }

        var state = _s;
        foreach (var command in commands)
        {
            if (Battle.Check(state, command, _rules) is not null)
                return null;
            state = Advance(state, command);
        }

        // The main action may kill a unit it hits (a Downed one may go Out): the bonus action mustn't count on it. Nor, unless
        // the profile is cruel, may it strike a foe the main action may have just Downed (fair play, spec §5.4). And after an
        // action that moves units or changes the map, only a bonus action on the unit itself is sure to still hold.
        var main = c.Kind is Kind.Move or Kind.DashMove ? null : c.Usable;
        var leaving = Leaving(main, c.Action, Hypo(c.ActTile));
        var selfOnly = main is { Moves: true };
        if (Surge(state, leaving, selfOnly) is { } surge)
        {
            commands.AddRange([surge.Surge, surge.Second]);
            state = Advance(Advance(state, surge.Surge), surge.Second);
            leaving = leaving.Union(surge.Leaving);
            selfOnly |= surge.Moves;
        }
        if (Bonus(state, leaving, selfOnly) is { } bonus && Battle.Check(state, bonus.Use, _rules) is null)
            commands.Add(bonus.Use);
        commands.Add(new EndTurn(_me.Id));
        return [.. commands];
    }

    /// <summary>
    /// The units <paramref name="usable"/>'s <paramref name="action"/>, taken by the unit as it stands in <paramref name="at"/>,
    /// may take out of reach of a later action: a Downed one it hits (it may finish it), any it may kill (one that dies at
    /// 0 HP, when its damage can reach the HP left: <see cref="MayDrop"/>), and unless the profile is cruel any foe it may
    /// Down; and when an attack of several may drop its target, the foe the rest go on to (#107: the standing hostile in
    /// reach with the least HP, ties by id), if they may drop it. None for an action that deals no damage.
    /// </summary>
    private ImmutableSortedSet<UnitId> Leaving(Usable? usable, Action? action, (BattleState State, BattleWriter W) at)
    {
        if (usable is null || action is null || !Scorer.Deals(usable.Ability.Effects))
            return [];
        var leaving = action.Affected.Where(id => _s.Units[id] is var u
                                                  && (u.Life == LifeState.Downed
                                                      || (!DownedRules.GoesDown(u) || !_p.Cruel && Scorer.Hostile(_me, u))
                                                      && MayDrop(usable, action, at, id)))
            .ToImmutableSortedSet();
        var me = at.State.Units[_me.Id];
        if (Strikes.Aimed(usable.Ability) && me.Sheet.Attacks > 1 && action.Affected.Any(id => MayDrop(usable, action, at, id))
            && at.State.Units.Values
                .Where(u => u.Life == LifeState.Up && Scorer.Hostile(me, u) && u.Sheet.Side != Side.Neutral && !action.Affected.Contains(u.Id)
                            && Battle.ResolveTarget(at.W, me, usable.Ability, new Target(Unit: u.Id), out _, out var hit) is null && hit.Contains(u.Id))
                .OrderBy(u => u.Hp).ThenBy(u => u.Id).FirstOrDefault() is { } next
            && MayDrop(usable, action, at, next.Id))
            leaving = leaving.Add(next.Id);
        return leaving;
    }

    /// <summary>An extra action and the second action it buys, with what the second may take out of reach and whether it moves units or changes the ground.</summary>
    private sealed record Surging(Use Surge, Use Second, ImmutableSortedSet<UnitId> Leaving, bool Moves);

    /// <summary>
    /// With the action spent in <paramref name="state"/> and an extra action (Action Surge) the unit can pay for: the best
    /// second action from where the plan leaves it (any action but a dash, never on a unit in <paramref name="leaving"/>, and
    /// with <paramref name="selfOnly"/>, after an action that moved units or changed the map, only one on the unit itself),
    /// when what it is worth less its own cost is more than the surge costs. An extra action is worth the best second action
    /// it buys; null when none is worth it.
    /// </summary>
    private Surging? Surge(BattleState state, ImmutableSortedSet<UnitId> leaving, bool selfOnly)
    {
        var me = state.Units[_me.Id];
        if (me.Economy.Action || me.Economy.ExtraActions > 0
            || _usables.FirstOrDefault(u => u.Surge && Battle.CostRefusal(me, u.Ability) is null) is not { } surge)
            return null;
        var use = new Use(_me.Id, surge.Command, new Target(), surge.Item);
        if (Battle.Check(state, use, _rules) is not null)
            return null;
        var surged = Advance(state, use);
        var after = surged.Units[_me.Id];
        var w = new BattleWriter(surged, _rules);
        (Usable Usable, Action Action)? best = null;
        var bestScore = Hoard(surge) + Epsilon;
        foreach (var usable in _usables.Where(u => u.Phase == Phase.Main && !u.Dash && !u.Surge && Battle.CostRefusal(after, u.Ability) is null
                                                   && (!selfOnly || u.Ability.Target.Kind == TargetKind.Self)))
        {
            var hoard = Hoard(usable);
            foreach (var action in Actions(usable, after.Pos, surged, w))
            {
                if (action.Target.Unit is { } unit && leaving.Contains(unit)
                    || !action.Targets.IsDefaultOrEmpty && action.Targets.Any(leaving.Contains) || !ItemOk(usable, after.Pos, action))
                    continue;
                if (Round(action.Value - hoard) > Round(bestScore))
                    (best, bestScore) = ((usable, action), action.Value - hoard);
            }
        }
        if (best is not { } second)
            return null;
        var next = new Use(_me.Id, second.Usable.Command, second.Action.Target, second.Usable.Item) { Targets = second.Action.Targets };
        return Battle.Check(surged, next, _rules) is null
            ? new Surging(use, next, Leaving(second.Usable, second.Action, (surged, w)), second.Usable.Moves)
            : null;
    }

    /// <summary>
    /// The best bonus action (or free action) worth more than it costs where the plan leaves the unit, if its Bonus is
    /// still there: escapes and dashes are left out (they only help a move), and so are an extra action (planned with the
    /// second action it buys, <see cref="Surge"/>) and a concentration ability after the plan has just begun concentrating on
    /// another. A teleport or leap goes to the landing tile worth most over staying. It never targets a unit in
    /// <paramref name="leaving"/> (the main action may have killed or Downed it) and, with <paramref name="selfOnly"/>, only
    /// uses abilities on the unit itself. Its score is what it is worth less its cost.
    /// </summary>
    private (Use Use, double Score)? Bonus(BattleState state, ImmutableSortedSet<UnitId> leaving, bool selfOnly)
    {
        var me = state.Units[_me.Id];
        if (!me.Economy.Bonus)
            return null;
        Use? best = null;
        var bestScore = Epsilon;
        var justConcentrating = me.Concentrating is not null && me.Concentrating != _me.Concentrating;
        var w = new BattleWriter(state, _rules);
        foreach (var usable in _usables.Where(u => u.Phase == Phase.Bonus && !u.Escape && !u.Dash && !u.Surge))
        {
            if (Battle.CostRefusal(me, usable.Ability) is not null || usable.Ability.Cost.Concentration && justConcentrating
                || selfOnly && usable.Ability.Target.Kind != TargetKind.Self)
                continue;
            var hoard = Hoard(usable);
            foreach (var action in Actions(usable, me.Pos, state, w))
            {
                if (action.Target.Unit is { } unit && leaving.Contains(unit)
                    || !action.Targets.IsDefaultOrEmpty && action.Targets.Any(leaving.Contains) || !ItemOk(usable, me.Pos, action))
                    continue;
                if (Round(action.Value - hoard) > Round(bestScore))
                    (best, bestScore) = (new Use(_me.Id, usable.Command, action.Target, usable.Item) { Targets = action.Targets },
                        action.Value - hoard);
            }
        }
        return best is null ? null : (best, bestScore);
    }

    private readonly Dictionary<(Tile, AbilityId?, AbilityId?, AbilityId?, ItemId?, bool), double> _bonusLeft = [];

    /// <summary>
    /// What the bonus action <paramref name="c"/> leaves for the end of its turn is worth: the best bonus action's score
    /// (<see cref="Bonus"/>) where the plan ends, once its disengage, its lead (a teleport or dash) and its action are paid
    /// for; 0 when one of them spends the bonus action. An estimate for ranking (it doesn't know which foes the action
    /// drops), cached by where the plan ends and what it uses.
    /// </summary>
    private double BonusLeft(Candidate c)
    {
        var selfOnly = c.Usable is { Moves: true } && c.Kind is not (Kind.Move or Kind.DashMove);
        var key = (c.End, c.Escape?.Command, c.Lead?.Command, c.Usable?.Command, c.Usable?.Item, selfOnly);
        if (_bonusLeft.TryGetValue(key, out var known))
            return known;
        var state = Hypo(c.End).State;
        foreach (var (usable, target) in new[] { (c.Escape, new Target()), (c.Lead, c.Kind == Kind.HopAct ? new Target(Tile: c.ActTile) : new Target()),
                     (c.Usable, c.Action?.Target ?? new Target()) })
            if (usable is not null && Battle.CostRefusal(state.Units[_me.Id], usable.Ability) is null)
                state = Advance(state, new Use(_me.Id, usable.Command, target, usable.Item));
        return _bonusLeft[key] = Bonus(state, [], selfOnly)?.Score ?? 0;
    }

    /// <summary>
    /// The state the plan expects after <paramref name="command"/>: no dice, just positions and what is spent, where a
    /// teleport or leap lands, and what an ability on the unit itself gives back (an extra action, the bonus action, Move).
    /// </summary>
    private BattleState Advance(BattleState state, Command command)
    {
        var me = state.Units[_me.Id];
        switch (command)
        {
            case Move move:
                {
                    var cost = 0;
                    for (var i = 1; i < move.Path.Length; i++)
                        cost += Paths.StepCost(_scorer.Terrain, move.Path[i - 1], move.Path[i], me.Sheet.Jump);
                    me = me with { Pos = move.Path[^1], Economy = me.Economy with { MoveLeft = me.Economy.MoveLeft - cost } };
                    break;
                }
            case Use use:
                {
                    var usable = _usables.First(u => u.Command == use.Ability && u.Item == use.Item);
                    var cost = usable.Ability.Cost;
                    var economy = me.Economy;
                    var conditions = me.Conditions;
                    if (cost.Action > 0)
                    {
                        if (Metamagic.Quickens(me, usable.Ability))
                            conditions = conditions.RemoveAll(c => c.Id == Metamagic.Quickened);
                        else
                            economy = economy.Action ? economy with { Action = false } : economy with { ExtraActions = economy.ExtraActions - 1 };
                    }
                    if (cost.Bonus > 0 || Metamagic.IsFreeQuickened(usable.Ability))
                        economy = economy with { Bonus = false };
                    if (Metamagic.Shapes(usable.Ability))
                        conditions = conditions.RemoveAll(c => c.Id.Value.StartsWith("metamagic.", StringComparison.Ordinal));
                    else if (Metamagic.IsMetamagicAbility(usable.Ability)
                             && usable.Ability.Effects.OfType<TransformEffect>().FirstOrDefault(t => t.Hook == "metamagic") is { } mm
                             && NamedHooks.String(mm.Args, "kind") is { } kind)
                    {
                        var id = new ConditionId("metamagic." + kind);
                        conditions = conditions.RemoveAll(c => c.Id == id)
                            .Add(new ActiveCondition(id, me.Id, RoundsLeft: 0, UntilSave: false, SaveDc: 0, Stat.Con, Concentration: false));
                    }
                    economy = economy with { MoveLeft = economy.MoveLeft - cost.Move };
                    if (usable.Ability.Target.Kind == TargetKind.Self)
                        foreach (var extra in (usable.Ability.Effects.IsDefault ? [] : usable.Ability.Effects).OfType<ExtraActionEffect>())
                            economy = extra.Kind switch
                            {
                                "action" => economy with { ExtraActions = economy.ExtraActions + 1 },
                                "bonus" => economy with { Bonus = true },
                                "move" => economy with { MoveLeft = economy.MoveLeft + Modifiers.Move(me.Sheet, _scorer.Mods(me)) },
                                _ => economy,
                            };
                    var pos = usable.Hop > 0 && use.Target.Tile is { } landing ? landing : me.Pos;
                    var resources = me.Resources;
                    foreach (var (name, amount) in cost.Resource ?? Ordinal.Map<int>())
                        resources = resources.SetItem(name, resources.GetValueOrDefault(name) - amount);
                    var uses = cost.Uses is { } n
                        ? me.UsesLeft.SetItem(usable.Ability.Id.Value, me.UsesLeft.GetValueOrDefault(usable.Ability.Id.Value, n) - 1)
                        : me.UsesLeft;
                    var bag = me.Bag;
                    if (use.Item is { } item && bag.FirstOrDefault(b => b.Item == item && b.Count > 0) is { } stack)
                        bag = stack.Count > 1 ? bag.Replace(stack, stack with { Count = stack.Count - 1 }) : bag.Remove(stack);
                    me = me with
                    {
                        Pos = pos, Economy = economy, Resources = resources, UsesLeft = uses, Bag = bag, Conditions = conditions,
                        Concentrating = cost.Concentration ? usable.Ability.Id : me.Concentrating,
                    };
                    break;
                }
        }
        return state with { Units = state.Units.SetItem(me.Id, me) };
    }
}
