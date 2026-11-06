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
        // the profile is cruel, may it strike a foe the main action may have just Downed (fair play, spec §5.4), nor one it may
        // push out of the bonus action's reach. A surged second action adds what it may take away, and any foe the two may drop
        // between them. And after an action that moves units or changes the map, only a bonus action on the unit itself is
        // sure to still hold.
        var main = c.Kind is Kind.Move or Kind.DashMove ? null : c.Usable;
        var at = Hypo(c.ActTile);
        var off = Leaving(main, c.Action, at);
        var selfOnly = main is { Moves: true };
        if (Surge(state, off, selfOnly) is { } surge)
        {
            commands.AddRange([surge.Surge, surge.Second]);
            state = Advance(Advance(state, surge.Surge), surge.Second);
            var together = TogetherDrop((main, c.Action, at), (surge.Usable, surge.Action, surge.At));
            off = off.Union(surge.Off).Union(Off.None with { Gone = together });
            selfOnly |= surge.Usable.Moves;
        }
        if (Bonus(state, off, selfOnly) is { } bonus && Battle.Check(state, bonus.Use, _rules) is null)
            commands.Add(bonus.Use);
        commands.Add(new EndTurn(_me.Id));
        return [.. commands];
    }

    /// <summary>
    /// What a later action in the turn (the surged second action, the bonus action) must keep off: the units that may be gone
    /// by then (<see cref="Gone"/>: dropped, finished, sent away), and the units a passive may push when the action hits them,
    /// with how far (<see cref="Pushed"/>), which a later action may still aim at if they stay in its reach pushed that far.
    /// </summary>
    private sealed record Off(ImmutableSortedSet<UnitId> Gone, ImmutableSortedDictionary<UnitId, int> Pushed)
    {
        public static Off None { get; } = new([], ImmutableSortedDictionary<UnitId, int>.Empty);

        public Off Union(Off other) => new(Gone.Union(other.Gone),
            other.Pushed.Aggregate(Pushed, (map, p) => map.SetItem(p.Key, Math.Max(p.Value, map.GetValueOrDefault(p.Key)))));
    }

    /// <summary>
    /// True when <paramref name="off"/> rules out <paramref name="action"/> of <paramref name="usable"/> by the unit on
    /// <paramref name="from"/>: a unit it aims at may be gone, or may be pushed out of the ability's reach (its range, or its
    /// weapon's for a weapon attack).
    /// </summary>
    private bool RulesOut(Off off, Usable usable, Tile from, Action action)
    {
        var reach = usable.Ability.IsWeaponAttack
            ? _me.Sheet.Weapon.Ranged ? _me.Sheet.Weapon.Range : _me.Sheet.Weapon.Reach ? 2 : 1
            : usable.Ability.Target.Range;
        IEnumerable<UnitId> aimed = action.Targets.IsDefaultOrEmpty ? [] : action.Targets;
        if (action.Target.Unit is { } unit)
            aimed = aimed.Append(unit);
        return aimed.Any(id => off.Gone.Contains(id)
                               || off.Pushed.TryGetValue(id, out var push) && Reach.Distance(from, _s.Units[id].Pos) + push > reach);
    }

    /// <summary>
    /// The units <paramref name="usable"/>'s <paramref name="action"/>, taken by the unit as it stands in <paramref name="at"/>,
    /// may take out of reach of a later action: a Downed one it hits (it may finish it), any it may kill (one that dies at
    /// 0 HP, when its damage can reach the HP left: <see cref="MayDrop"/>), and unless the profile is cruel any foe it may
    /// Down; and when an attack of several may drop its target, the foe the rest go on to (#107: the standing hostile in
    /// reach with the least HP, ties by id), if they may drop it. Whatever the damage: the unit's concentration summons when
    /// the action starts a new concentration, which sends them away. And the targets the unit's own passives may push when
    /// the action hits them (Repelling Blast on a spell attack: <see cref="PushOnHit"/>), with how far.
    /// </summary>
    private Off Leaving(Usable? usable, Action? action, (BattleState State, BattleWriter W) at)
    {
        if (usable is null || action is null)
            return Off.None;
        var pushed = ImmutableSortedDictionary<UnitId, int>.Empty;
        foreach (var id in action.Affected)
            if (PushOnHit(usable, at.State.Units[id], at.W) is var push and > 0)
                pushed = pushed.SetItem(id, push);
        var gone = ImmutableSortedSet<UnitId>.Empty;
        if (usable.Ability.Cost.Concentration && _me.Concentrating is not null)
            gone = gone.Union(_s.Units.Values
                .Where(u => u.Conditions.Any(c => c.Id == Summons.Marker && c.Concentration && c.Source == _me.Id)).Select(u => u.Id));
        if (!Scorer.Deals(usable.Ability.Effects))
            return new Off(gone, pushed);
        var leaving = gone.Union(action.Affected.Where(id => _s.Units[id] is var u
                                                             && (u.Life == LifeState.Downed || MayFall(u) && MayDrop(usable, action, at, id))));
        var me = at.State.Units[_me.Id];
        if (Strikes.Aimed(usable.Ability) && me.Sheet.Attacks > 1 && action.Affected.Any(id => MayDrop(usable, action, at, id))
            && at.State.Units.Values
                .Where(u => u.Life == LifeState.Up && Scorer.Hostile(me, u) && u.Sheet.Side != Side.Neutral && !action.Affected.Contains(u.Id)
                            && Battle.ResolveTarget(at.W, me, usable.Ability, new Target(Unit: u.Id), out _, out var hit) is null && hit.Contains(u.Id))
                .OrderBy(u => u.Hp).ThenBy(u => u.Id).FirstOrDefault() is { } next
            && MayDrop(usable, action, at, next.Id))
            leaving = leaving.Add(next.Id);
        return new Off(leaving, pushed);
    }

    /// <summary>
    /// True when dropping <paramref name="u"/> takes it out of a later action's reach: one that dies at 0 HP, and unless the
    /// profile is cruel a foe that would be Downed (fair play, spec §5.4).
    /// </summary>
    private bool MayFall(UnitState u) => !DownedRules.GoesDown(u) || !_p.Cruel && Scorer.Hostile(_me, u);

    /// <summary>
    /// The units both <paramref name="first"/> and <paramref name="second"/> hit that neither may drop alone but both may
    /// together: their damage on it added up can reach the HP left (each counted where it is taken from).
    /// </summary>
    private ImmutableSortedSet<UnitId> TogetherDrop((Usable? Usable, Action? Action, (BattleState State, BattleWriter W) At) first,
        (Usable Usable, Action Action, (BattleState State, BattleWriter W) At) second)
    {
        if (first is not ({ } usable, { } action, var at) || !Scorer.Deals(usable.Ability.Effects) || !Scorer.Deals(second.Usable.Ability.Effects))
            return [];
        var drop = ImmutableSortedSet.CreateBuilder<UnitId>();
        foreach (var id in action.Affected.Intersect(second.Action.Affected))
        {
            var u = _s.Units[id];
            if (u.Life == LifeState.Up && MayFall(u)
                && DamageOn(usable, action, at, id).Plus(DamageOn(second.Usable, second.Action, second.At, id))
                    .AtLeast(Math.Max(1, u.Hp + u.TempHp)) > 0)
                drop.Add(id);
        }
        return drop.ToImmutable();
    }

    private ImmutableArray<PassiveDef>? _onHitPushes;

    /// <summary>
    /// How far the unit's own on-hit passives may push or pull <paramref name="target"/> when <paramref name="usable"/>'s
    /// attack hits it (Repelling Blast: 2 tiles, on a spell attack), their conditions checked as the engine checks them; a
    /// swap or teleport counts as out of any reach. 0 when none would.
    /// </summary>
    private int PushOnHit(Usable usable, UnitState target, BattleWriter w)
    {
        _onHitPushes ??= [.. Modifiers.Passives(_me, _rules).Where(p => p.Hook == HookKind.OnAttackHit && !p.Effects.IsDefaultOrEmpty
            && Scorer.All(p.Effects).Any(e => e is PushEffect or PullEffect or SwapEffect or TeleportEffect))];
        if (_onHitPushes.Value.IsEmpty || Scorer.FirstAttack(usable.Ability.Effects) is not { } attack)
            return 0;
        var hit = new RollResult([], 0, 0, 0, 0, Success: true, Crit: false, Fumble: false);
        var args = new HookArgs(_me.Id, target.Id, hit, Tile: target.Pos, Ability: usable.Ability.Id, AttackKind: attack.Kind, Edge: Edge.None);
        return _onHitPushes.Value.Where(p => p.When is not { } when || Hooks.Holds(when, w, args, _rules))
            .SelectMany(p => Scorer.All(p.Effects))
            .Select(e => e switch { PushEffect push => push.Distance, PullEffect pull => pull.Distance, SwapEffect or TeleportEffect => 99, _ => 0 })
            .DefaultIfEmpty(0).Max();
    }

    /// <summary>
    /// An extra action and the second action it buys: its command, its ability and target, where it is taken from, and what
    /// later actions must keep off after it.
    /// </summary>
    private sealed record Surging(Use Surge, Use Second, Usable Usable, Action Action, (BattleState State, BattleWriter W) At, Off Off);

    /// <summary>
    /// With the action spent in <paramref name="state"/> and an extra action (Action Surge) the unit can pay for: the best
    /// second action from where the plan leaves it (any action but a dash, never one <paramref name="off"/> rules out, and
    /// with <paramref name="selfOnly"/>, after an action that moved units or changed the map, only one on the unit itself),
    /// when what it is worth less its own cost is more than the surge costs. An extra action is worth the best second action
    /// it buys; null when none is worth it.
    /// </summary>
    private Surging? Surge(BattleState state, Off off, bool selfOnly)
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
                if (RulesOut(off, usable, after.Pos, action) || !ItemOk(usable, after.Pos, action))
                    continue;
                if (Round(action.Value - hoard) > Round(bestScore))
                    (best, bestScore) = ((usable, action), action.Value - hoard);
            }
        }
        if (best is not { } second)
            return null;
        var next = new Use(_me.Id, second.Usable.Command, second.Action.Target, second.Usable.Item) { Targets = second.Action.Targets };
        return Battle.Check(surged, next, _rules) is null
            ? new Surging(use, next, second.Usable, second.Action, (surged, w), Leaving(second.Usable, second.Action, (surged, w)))
            : null;
    }

    /// <summary>
    /// The best bonus action (or free action) worth more than it costs where the plan leaves the unit, if its Bonus is
    /// still there: escapes and dashes are left out (they only help a move), and so are an extra action (planned with the
    /// second action it buys, <see cref="Surge"/>) and a concentration ability after the plan has just begun concentrating on
    /// another. A teleport or leap goes to the landing tile worth most over staying. It never takes an action
    /// <paramref name="off"/> rules out (the main action may have killed, Downed or pushed its target) and, with
    /// <paramref name="selfOnly"/>, only uses abilities on the unit itself. Its score is what it is worth less its cost.
    /// </summary>
    private (Use Use, double Score)? Bonus(BattleState state, Off off, bool selfOnly)
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
                if (RulesOut(off, usable, me.Pos, action) || !ItemOk(usable, me.Pos, action))
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
        return _bonusLeft[key] = Bonus(state, Off.None, selfOnly)?.Score ?? 0;
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
