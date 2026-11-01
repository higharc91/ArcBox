using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Planner
{
    /// <summary>
    /// The commands of <paramref name="c"/>, each checked with <see cref="Battle.Check"/> against the state the plan expects
    /// by then, or null when one would be refused. Then the best bonus action at the end, if one is worth its cost.
    /// </summary>
    private ImmutableArray<Command>? Commands(Candidate c)
    {
        var commands = new List<Command>();
        Usable? Escape(bool first) => c.Escape is { } e && first == (c.Kind != Kind.ActMove && c.Kind != Kind.DashMove) ? e : null;
        void Act(Usable? usable, Target target)
        {
            if (usable is not null)
                commands.Add(new Use(_me.Id, usable.Command, target, usable.Item));
        }
        void Walk()
        {
            if (c.End != c.ActTile || c.Kind == Kind.DashMove)
            {
                var path = c.Path.IsDefault ? _paths.PathTo(c.End) : c.Path;
                if (!path.IsEmpty)
                    commands.Add(new Move(_me.Id, [c.ActTile, .. path]));
            }
        }

        switch (c.Kind)
        {
            case Kind.Move:
                Act(Escape(first: true), new Target());
                if (c.End != _me.Pos)
                    commands.Add(new Move(_me.Id, [_me.Pos, .. _paths.PathTo(c.End)]));
                break;
            case Kind.MoveAct:
                Act(Escape(first: true), new Target());
                if (c.ActTile != _me.Pos)
                    commands.Add(new Move(_me.Id, [_me.Pos, .. _paths.PathTo(c.ActTile)]));
                Act(c.Usable, c.Action!.Target);
                break;
            case Kind.ActMove:
            case Kind.DashMove:
                Act(c.Usable, c.Action!.Target);
                Act(Escape(first: false), new Target());
                Walk();
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
        var main = c.Kind is Kind.MoveAct or Kind.ActMove ? c.Usable : null;
        var leaving = main is not null && Scorer.Deals(main.Ability.Effects)
            ? c.Action!.Affected.Where(id => _s.Units[id] is var u
                                              && (u.Life == LifeState.Downed || !DownedRules.GoesDown(u)
                                                  || !_p.Cruel && Scorer.Hostile(_me, u) && MayDrop(c, id)))
                .ToImmutableSortedSet()
            : [];
        if (Bonus(state, leaving, selfOnly: main is { Stable: false }) is { } bonus && Battle.Check(state, bonus, _rules) is null)
            commands.Add(bonus);
        commands.Add(new EndTurn(_me.Id));
        return [.. commands];
    }

    /// <summary>
    /// The best bonus action (or free action) worth more than it costs where the plan leaves the unit, if its Bonus is
    /// still there: escapes and dashes are left out (they only help a move), and so is a concentration ability after the
    /// plan has just begun concentrating on another. It never targets a unit in <paramref name="leaving"/> (the main action
    /// may have killed or Downed it) and, with <paramref name="selfOnly"/>, only uses abilities on the unit itself.
    /// </summary>
    private Use? Bonus(BattleState state, ImmutableSortedSet<UnitId> leaving, bool selfOnly)
    {
        var me = state.Units[_me.Id];
        if (!me.Economy.Bonus)
            return null;
        Use? best = null;
        var bestScore = Epsilon;
        var justConcentrating = me.Concentrating is not null && me.Concentrating != _me.Concentrating;
        foreach (var usable in _usables.Where(u => u.Phase == Phase.Bonus && !u.Escape && !u.Dash))
        {
            if (Battle.CostRefusal(me, usable.Ability) is not null || usable.Ability.Cost.Concentration && justConcentrating
                || selfOnly && usable.Ability.Target.Kind != TargetKind.Self)
                continue;
            var hoard = Hoard(usable);
            foreach (var action in Actions(usable, me.Pos, state))
            {
                if (action.Target.Unit is { } unit && leaving.Contains(unit) || !ItemOk(usable, me.Pos, action))
                    continue;
                if (Round(action.Value - hoard) > Round(bestScore))
                    (best, bestScore) = (new Use(_me.Id, usable.Command, action.Target, usable.Item), action.Value - hoard);
            }
        }
        return best;
    }

    /// <summary>The state the plan expects after <paramref name="command"/>: no dice, just positions and what is spent.</summary>
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
                    if (usable.Dash)
                        economy = economy with { MoveLeft = economy.MoveLeft + Modifiers.Move(me.Sheet, _scorer.Mods(me)) };
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
                        Economy = economy, Resources = resources, UsesLeft = uses, Bag = bag, Conditions = conditions,
                        Concentrating = cost.Concentration ? usable.Ability.Id : me.Concentrating,
                    };
                    break;
                }
        }
        return state with { Units = state.Units.SetItem(me.Id, me) };
    }
}
