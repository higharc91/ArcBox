using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace Tactics.Logic;

/// <summary>
/// The one-ply turn planner behind <see cref="Ai.Plan"/> (spec §5.2):
/// <list type="number">
/// <item>The tiles the unit can reach with its Move left (<see cref="Paths.From"/>).</item>
/// <item>Each tile's score on position alone (<see cref="Position"/>); the best 12 plus the current tile are kept.</item>
/// <item>From each kept tile, each ability it can pay for on each legal target or aim (an area aim only on tiles in range
/// that cover a unit it is meant for): "move, then act", scored as what the action is worth plus the tile, less the
/// resources it spends (hoard).</item>
/// <item>"Act from here, then move to the best tile the Move left reaches", for actions that leave the map as it is;
/// "just move"; and Dash then move.</item>
/// <item>The best bonus action, added greedily where the turn ends.</item>
/// </list>
/// Ties break on the ordinal (ability id, tile, target). Every command of the chosen plan is checked with
/// <see cref="Battle.Check"/> against the state the plan expects at that point; a plan that fails is passed over.
/// </summary>
internal sealed class Planner
{
    private const int KeptTiles = 12;
    private const double Epsilon = 1e-6;

    private readonly BattleState _s;
    private readonly RulesPack _rules;
    private readonly UnitState _me;
    private readonly AiProfile _p;
    private readonly AiOptions _options;
    private readonly Scorer _scorer;
    private readonly bool _flee;
    private readonly ThreatMap _threat;
    private readonly PathMap _paths;
    private readonly Dictionary<Tile, Spot> _spots = [];
    private readonly ImmutableArray<UnitState> _foes;
    private readonly ImmutableArray<UnitState> _reactors;
    private readonly int[] _walk;
    private readonly int _reach;
    private readonly int _move;

    /// <summary>
    /// How much the threat map still weighs: fully for the first 5 rounds, then 10% less each round down to 30%, so two
    /// careful sides that keep out of each other's reach don't stall the battle for good.
    /// </summary>
    private readonly double _wariness;
    private readonly ImmutableArray<Usable> _usables;
    private readonly Dictionary<(AbilityId, ItemId?, Tile), double> _aimValues = [];
    private readonly Dictionary<Tile, (BattleState State, BattleWriter W)> _hypo = [];

    private Planner(BattleState s, UnitState me, RulesPack rules, AiProfile profile, AiOptions options)
    {
        _s = s;
        _rules = rules;
        _me = me;
        _options = options;
        _scorer = new Scorer(s, rules, me.Id);
        _flee = profile.FleeBelowHp > 0 && me.Hp < profile.FleeBelowHp * me.Sheet.MaxHp;
        _p = _flee ? profile with { Damage = 0, Kill = 0, Control = 0, Caution = Math.Max(1, profile.Caution) * 2 } : profile;
        _threat = ThreatMap.Build(_scorer, me.Sheet.Side, me, me.Id);

        var units = s.Units.Values;
        _foes = [.. units.Where(u => u.Life == LifeState.Up && Scorer.Hostile(me, u) && u.Sheet.Side != Side.Neutral)];
        _reactors = [.. _foes.Where(f => f.Economy.Reaction && !f.Sheet.Weapon.Ranged && _scorer.CanAct(f))];
        var mover = Mover(me.Economy.MoveLeft);
        _paths = Paths.From(_scorer.Terrain, me.Pos, mover);
        _walk = WalkDistances();
        _usables = Usables();
        _move = Modifiers.Move(me.Sheet, _scorer.Mods(me));
        _wariness = _flee ? 1 : Math.Max(0.3, 1 - 0.1 * Math.Max(0, s.Round - 5));
        _reach = Math.Max(me.Sheet.Weapon.Ranged ? me.Sheet.Weapon.Range : 1,
            _usables.Where(u => Scorer.Deals(u.Ability.Effects) && u.Ability.Target.Kind != TargetKind.Self)
                .Select(u => u.Ability.Target.Range).DefaultIfEmpty(1).Max());
    }

    /// <summary>See <see cref="Ai.Plan"/>.</summary>
    public static TurnPlan Plan(BattleState s, UnitId unit, RulesPack rules, AiOptions? options)
    {
        if (s.Outcome is not null || s.Pending is not null || s.Active != unit || !s.Units.TryGetValue(unit, out var me))
            return new TurnPlan([], 0, $"{unit}: not its turn.");
        if (!Modifiers.CanAct(s, unit, rules))
            return new TurnPlan([new EndTurn(unit)], 0, $"{unit} can't act: end turn.");
        var profile = AiProfile.For(s, unit, rules, options?.ProfileOverride);
        return new Planner(s, me, rules, profile, options ?? new AiOptions()).Run();
    }

    // ---- The plan ---------------------------------------------------------------------------------------------------

    private TurnPlan Run()
    {
        foreach (var t in _paths.Reachable)
            _spots[t] = SpotAt(t, _paths.PathTo(t));

        var candidates = new List<Candidate>();
        var here = _me.Pos;
        var bonusEscape = _usables.Where(u => u.Escape && u.Phase == Phase.Bonus).FirstOrDefault(u => Affordable(_me, u));
        var actionEscape = _usables.Where(u => u.Escape && u.Phase == Phase.Main).FirstOrDefault(u => Affordable(_me, u));

        // Just move (or stay), escaping first if that beats the opportunity attacks.
        foreach (var (t, spot) in _spots)
        {
            var (score, escape) = Arrive(spot, bonusEscape ?? actionEscape);
            candidates.Add(new Candidate(Kind.Move, score, null, null, here, t, escape, 0, spot.Position));
        }

        // Move, then act.
        var kept = _spots.Values.OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).Take(KeptTiles)
            .Select(s => s.Tile).Append(here).Distinct().Order().ToArray();
        foreach (var t in kept)
        {
            var spot = _spots[t];
            var me = Moved(t);
            foreach (var usable in _usables.Where(u => u.Phase == Phase.Main && !u.Dash && Affordable(me, u)))
            {
                var hoard = Hoard(usable);
                var (arrive, escape) = Arrive(spot, usable.Ability.Cost.Bonus > 0 ? null : bonusEscape);
                foreach (var action in Actions(usable, t))
                    if (action.Value > Epsilon)
                        candidates.Add(new Candidate(Kind.MoveAct, arrive + action.Value - hoard, usable, action, t, t, escape,
                            action.Value - hoard, spot.Position));
            }
        }

        // Act here, then move with what is left (only when the action leaves the map as it is).
        var byScore = _spots.Values.OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).ToArray();
        foreach (var usable in _usables.Where(u => u.Phase == Phase.Main && !u.Dash && u.Stable && Affordable(_me, u)))
        {
            var hoard = Hoard(usable);
            var budget = _me.Economy.MoveLeft - usable.Ability.Cost.Move;
            var escapeAfter = usable.Ability.Cost.Bonus > 0 ? null : bonusEscape;
            Spot? best = null;
            var (bestArrive, bestEscape) = (double.NegativeInfinity, (Usable?)null);
            foreach (var spot in byScore.Where(s => s.Cost <= budget))
            {
                var (arrive, escape) = Arrive(spot, escapeAfter);
                if (Round(arrive) > Round(bestArrive))
                    (best, bestArrive, bestEscape) = (spot, arrive, escape);
            }
            if (best is null)
                continue;
            foreach (var action in Actions(usable, here))
                if (action.Value > Epsilon)
                    candidates.Add(new Candidate(Kind.ActMove, bestArrive + action.Value - hoard, usable, action, here, best.Tile,
                        bestEscape, action.Value - hoard, best.Position));
        }

        // Dash, then move further.
        foreach (var dash in _usables.Where(u => u.Phase == Phase.Main && u.Dash && Affordable(_me, u)))
        {
            var extra = Modifiers.Move(_me.Sheet, _scorer.Mods(_me));
            var far = Paths.From(_scorer.Terrain, _me.Pos, Mover(_me.Economy.MoveLeft - dash.Ability.Cost.Move + extra));
            foreach (var t in far.Reachable.Where(t => !_spots.ContainsKey(t)))
            {
                var spot = SpotAt(t, far.PathTo(t));
                var (arrive, escape) = Arrive(spot, bonusEscape);
                candidates.Add(new Candidate(Kind.DashMove, arrive - Hoard(dash), dash, new Action(new Target(), here, [], 0), here, t,
                    escape, -Hoard(dash), spot.Position, spot.Path));
            }
        }

        candidates.Sort(Candidate.Order);
        foreach (var candidate in candidates)
            if (Commands(candidate) is { } commands)
                return Finish(candidate, commands, candidates);
        return new TurnPlan([new EndTurn(_me.Id)], 0, $"{_me.Id} ({_p.Id}): no legal plan; end turn.");
    }

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

        // The main action may kill a unit it hits (a Downed one may go Out): the bonus action mustn't count on it. And after
        // an action that moves units or changes the map, only a bonus action on the unit itself is sure to still hold.
        var main = c.Kind is Kind.MoveAct or Kind.ActMove ? c.Usable : null;
        var leaving = main is not null && Scorer.Deals(main.Ability.Effects)
            ? c.Action!.Affected.Where(id => _s.Units[id] is var u && (u.Life == LifeState.Downed || !DownedRules.GoesDown(u)))
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
    /// may have killed it) and, with <paramref name="selfOnly"/>, only uses abilities on the unit itself.
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
                if (action.Target.Unit is { } unit && leaving.Contains(unit))
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
                    if (cost.Action > 0)
                        economy = economy.Action ? economy with { Action = false } : economy with { ExtraActions = economy.ExtraActions - 1 };
                    if (cost.Bonus > 0)
                        economy = economy with { Bonus = false };
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
                        Economy = economy, Resources = resources, UsesLeft = uses, Bag = bag,
                        Concentrating = cost.Concentration ? usable.Ability.Id : me.Concentrating,
                    };
                    break;
                }
        }
        return state with { Units = state.Units.SetItem(me.Id, me) };
    }

    // ---- Positions --------------------------------------------------------------------------------------------------

    /// <summary>A reachable tile: the path there, its Move cost, its score on position alone and the opportunity attacks
    /// the walk would draw.</summary>
    private sealed record Spot(Tile Tile, ImmutableArray<Tile> Path, int Cost, double Position, double Risk, double Score);

    private Spot SpotAt(Tile t, ImmutableArray<Tile> path)
    {
        var cost = 0;
        var prev = _me.Pos;
        foreach (var step in path)
        {
            cost += Paths.StepCost(_scorer.Terrain, prev, step, _me.Sheet.Jump);
            prev = step;
        }
        var position = Position(t) - 0.01 * cost;
        var risk = Risk(path);
        return new Spot(t, path, cost, position, risk, position - (1 + _p.Caution) * risk);
    }

    /// <summary>
    /// A tile's score on position alone (spec §5.2): minus caution × the threat on it next round (with the unit's own AC and
    /// the cover the tile gives against each foe that would shoot), weighed by how much of the unit's HP that threat is (half
    /// for a scratch, up to double when it could drop it) and by <see cref="_wariness"/>, high
    /// ground, setting up a flank for an ally, staying near an ally who heals while hurt, closing in (a little for each tile
    /// short of striking range, and half its damage per round for a tile it can strike a foe from next turn) or, fleeing,
    /// getting away, and staying near a protected ally (the objective).
    /// </summary>
    private double Position(Tile t)
    {
        var mods = _scorer.Mods(_me.Id, t);
        var ac = _me.Sheet.Ac + mods.Ac;
        var threat = 0.0;
        foreach (var (unit, option) in _threat.Sources(t))
        {
            var ev = option.Ev;
            if (option.AttackModifier is { } modifier && !option.Melee)
            {
                var cover = _scorer.Cover(unit.Pos, t, ranged: true);
                var plain = Odds.Hit(modifier, ac, Edge.None);
                if (cover > 0 && plain > 0)
                    ev *= Odds.Hit(modifier, ac + cover, Edge.None) / plain;
            }
            threat += ev;
        }
        var score = -_p.Caution * _wariness * threat * Math.Clamp(2 * threat / Math.Max(1, _me.Hp + _me.TempHp), 0.5, 2);

        var nearest = _foes.OrderBy(f => Reach.Distance(f.Pos, t)).ThenBy(f => f.Id).FirstOrDefault();
        if (nearest is not null)
        {
            var above = Math.Clamp(_scorer.Terrain.HeightAt(t) - _scorer.Terrain.HeightAt(nearest.Pos), 0, 3);
            score += above * (_reach > 1 ? 0.4 : 0.1);
        }

        if (!_me.Sheet.Weapon.Ranged)
            foreach (var foe in _foes.Where(f => Reach.Distance(f.Pos, t) == 1))
                if (Reach.OppositeOf(t, foe.Pos) is { } opposite
                    && _s.Units.Values.Any(u => u.Id != _me.Id && u.Pos == opposite && !Scorer.Hostile(_me, u) && _scorer.CanAct(u)))
                    score += 0.5;

        if (_me.Hp < _me.Sheet.MaxHp && Healers().Any(h => Reach.Distance(h.Unit.Pos, t) <= h.Reach))
            score += 0.5 * (1 - (double)_me.Hp / _me.Sheet.MaxHp);

        var walk = _walk[t.X * _scorer.Terrain.Map.Height + t.Y];
        var manhattan = _foes.Select(f => Reach.Distance(f.Pos, t)).DefaultIfEmpty(0).Min();
        if (_flee)
            score += 0.6 * (walk == int.MaxValue ? manhattan : walk);
        else if (!_foes.IsEmpty)
        {
            var gap = Math.Max(0, _reach > 1 || walk == int.MaxValue ? manhattan - _reach : walk - 1);
            score -= 0.3 * gap * Math.Max(0.25, _p.Damage);
            if (gap <= _move)
                score += 0.5 * _scorer.Dpr(_me) * _p.Damage;   // it can strike from here next turn
        }

        var protectedAllies = _s.Units.Values.Where(u => u.Protected && u.Id != _me.Id && !Scorer.Hostile(_me, u)
                                                         && DownedRules.InBattle(u)).ToArray();
        if (protectedAllies.Length > 0)
            score -= 0.1 * _p.Objective * Math.Max(0, protectedAllies.Min(u => Reach.Distance(u.Pos, t)) - 3);
        return score;
    }

    private ImmutableArray<(UnitState Unit, int Reach)>? _healers;

    /// <summary>Allies that know a healing ability, with how far they can heal from (their Move plus its range).</summary>
    private ImmutableArray<(UnitState Unit, int Reach)> Healers() => _healers ??= [.. _s.Units.Values
        .Where(u => u.Id != _me.Id && !Scorer.Hostile(_me, u) && _scorer.CanAct(u))
        .Select(u => (Unit: u, Range: u.Sheet.Abilities
            .Select(a => _rules.Abilities.GetValueOrDefault(a))
            .Where(a => a is not null && Scorer.All(a.Effects).Any(e => e is HealEffect))
            .Select(a => a!.Target.Range).DefaultIfEmpty(-1).Max()))
        .Where(h => h.Range >= 0)
        .Select(h => (h.Unit, h.Unit.Sheet.Move + h.Range))];

    /// <summary>
    /// The opportunity attacks walking <paramref name="path"/> would draw (see <see cref="Reactions"/>): each foe with its
    /// Reaction and a melee weapon, once, at the first step that leaves its reach; none when already disengaged.
    /// </summary>
    private double Risk(ImmutableArray<Tile> path)
    {
        if (path.IsEmpty || _me.Conditions.Any(c => c.Id == Movement.Disengaged))
            return 0;
        var risk = 0.0;
        foreach (var foe in _reactors)
        {
            var prev = _me.Pos;
            foreach (var step in path)
            {
                if (Reach.Melee(_scorer.Terrain, foe.Pos, prev, foe.Sheet.Weapon.Reach)
                    && !Reach.Melee(_scorer.Terrain, foe.Pos, step, foe.Sheet.Weapon.Reach))
                {
                    risk += OpportunityAttack(foe);
                    break;
                }
                prev = step;
            }
        }
        return risk;
    }

    private readonly Dictionary<UnitId, double> _opportunity = [];

    private double OpportunityAttack(UnitState foe)
    {
        if (!_opportunity.TryGetValue(foe.Id, out var ev))
        {
            var attack = _rules.Abilities[RulesPack.CommonAttack.Id];
            var st = new Strike(_scorer.W, foe, attack, _me.Pos, neutral: true);
            _opportunity[foe.Id] = ev = _scorer.Walk(attack.Effects, st, _me).Damage.Mean;
        }
        return ev;
    }

    /// <summary>
    /// What arriving at <paramref name="spot"/> is worth, using <paramref name="escape"/> (disengage) first when the
    /// opportunity attacks it avoids cost more than the escape does.
    /// </summary>
    private (double Score, Usable? Escape) Arrive(Spot spot, Usable? escape)
    {
        if (spot.Risk <= 0 || escape is null)
            return (spot.Score, null);
        var escaped = spot.Position - Hoard(escape);
        return escaped > spot.Score ? (escaped, escape) : (spot.Score, null);
    }

    /// <summary>
    /// Walking distance (Move) from each tile to the nearest foe's tile, over the terrain alone (units ignored), with the
    /// unit's Jump; <see cref="int.MaxValue"/> where no foe can be walked to.
    /// </summary>
    private int[] WalkDistances()
    {
        var terrain = _scorer.Terrain;
        var map = terrain.Map;
        var dist = new int[map.Width * map.Height];
        Array.Fill(dist, int.MaxValue);
        var queue = new PriorityQueue<Tile, (int, int)>();
        foreach (var foe in _foes)
        {
            var i = foe.Pos.X * map.Height + foe.Pos.Y;
            dist[i] = 0;
            queue.Enqueue(foe.Pos, (0, i));
        }
        (int, int)[] directions = [(0, -1), (1, 0), (0, 1), (-1, 0)];
        while (queue.TryDequeue(out var current, out var priority))
        {
            var ci = current.X * map.Height + current.Y;
            if (priority.Item1 > dist[ci])
                continue;
            foreach (var (dx, dy) in directions)
            {
                var next = new Tile(current.X + dx, current.Y + dy);
                if (!map.InBounds(next))
                    continue;
                var step = Paths.StepCost(terrain, next, current, _me.Sheet.Jump);
                if (step == int.MaxValue && dist[ci] == 0)
                    step = terrain.Walkable(next) ? 1 : int.MaxValue;   // a foe's own tile is the goal, whatever it stands on
                if (step == int.MaxValue)
                    continue;
                var ni = next.X * map.Height + next.Y;
                if (dist[ci] + step >= dist[ni])
                    continue;
                dist[ni] = dist[ci] + step;
                queue.Enqueue(next, (dist[ni], ni));
            }
        }
        return dist;
    }

    private Mover Mover(int move)
    {
        var units = _s.Units.Values.Where(u => u.Id != _me.Id && DownedRules.InBattle(u)).ToArray();
        var canMove = !_scorer.Mods(_me).CantMove;
        return new Mover(canMove ? Math.Max(0, move) : 0, _me.Sheet.Jump,
            [.. units.Where(u => !Movement.Hostile(_me, u)).Select(u => u.Pos)],
            [.. units.Where(u => Movement.Hostile(_me, u)).Select(u => u.Pos)]);
    }

    // ---- Actions ----------------------------------------------------------------------------------------------------

    private enum Phase { Main, Bonus }

    /// <summary>
    /// An ability the unit may use on its turn: its definition (an item's own ability), the command's ability id
    /// (<see cref="Battle.UseItem"/> for an item) and item, whether it takes the action or the bonus action, whether it is
    /// a disengage (<see cref="Escape"/>) or a dash, and whether it leaves the map as it is (<see cref="Stable"/>), so a
    /// move planned before it still holds after it.
    /// </summary>
    private sealed record Usable(AbilityDef Ability, AbilityId Command, ItemId? Item, Phase Phase, bool Escape, bool Dash, bool Stable);

    /// <summary>
    /// What the unit can use: its sheet's abilities and the built-in <c>common.*</c> actions it can pay for (in id order),
    /// and with <see cref="AiOptions.AllowItems"/> the items in its bag. Reactions are left out.
    /// </summary>
    private ImmutableArray<Usable> Usables()
    {
        var ids = new SortedSet<AbilityId>(_me.Sheet.Abilities);
        ids.UnionWith(_rules.Abilities.Keys.Where(a => a.Value.StartsWith("common.", StringComparison.Ordinal)));
        ids.Remove(Battle.UseItem);
        var usables = ImmutableArray.CreateBuilder<Usable>();
        foreach (var id in ids)
            if (_rules.Abilities.TryGetValue(id, out var ability) && !_scorer.ItemAbilities.Contains(id) && UsableOf(ability, id, null) is { } u)
                usables.Add(u);
        if (_options.AllowItems)
            foreach (var stack in _me.Bag.Where(b => b.Count > 0).DistinctBy(b => b.Item).OrderBy(b => b.Item))
                if (_rules.Items.TryGetValue(stack.Item, out var item) && _rules.Abilities.TryGetValue(item.Use, out var ability)
                    && UsableOf(ability, Battle.UseItem, item.Id) is { } u)
                    usables.Add(u);
        return usables.ToImmutable();
    }

    private Usable? UsableOf(AbilityDef ability, AbilityId command, ItemId? item)
    {
        var cost = ability.Cost;
        if (cost.Reaction > 0 || ability.Ai?.Tags is { IsDefault: false } tags && tags.Any(t => t.StartsWith(Reactions.ReactsToTag, StringComparison.Ordinal)))
            return null;
        var self = ability.Target.Kind == TargetKind.Self;
        var top = ability.Effects.IsDefault ? [] : ability.Effects;
        var escape = self && top.Any(e => e is ApplyEffect { Condition: var c } && c == Movement.Disengaged);
        var dash = self && top.Any(e => e is ExtraActionEffect { Kind: "move" });
        var stable = !Scorer.All(top).Any(e => e is PushEffect or PullEffect or SwapEffect or TeleportEffect or DashEffect
            or JumpToEffect or ZoneEffect or HazardEffect or AlterTerrainEffect or SummonEffect or TransformEffect or TurnOrderEffect
            || e is ApplyEffect a && Halts(a.Condition) || e is StatModEffect m && (m.Mods.CantMove || m.Mods.Incapacitated));
        return new Usable(ability, command, item, cost.Action > 0 ? Phase.Main : Phase.Bonus, escape, dash, stable);
    }

    /// <summary>True when the condition (or one it implies) stops its holder moving or acting.</summary>
    private bool Halts(ConditionId id)
    {
        var seen = new List<ConditionId>();
        for (ConditionId? next = id; next is { } c && !seen.Contains(c) && _rules.Conditions.TryGetValue(c, out var def); next = def.Implies)
        {
            seen.Add(c);
            if (def.Static is { } m && (m.CantMove || m.Incapacitated))
                return true;
        }
        return false;
    }

    private static bool Affordable(UnitState me, Usable usable) => Battle.CostRefusal(me, usable.Ability) is null;

    /// <summary>
    /// What spending <paramref name="usable"/>'s resources costs: hoard × 10 × the share of each pool it takes (a per-battle
    /// use is a share of its uses); an item is worth half a pool.
    /// </summary>
    private double Hoard(Usable usable)
    {
        var cost = usable.Ability.Cost;
        var share = 0.0;
        foreach (var (name, amount) in cost.Resource ?? Ordinal.Map<int>())
            share += (double)amount / Math.Max(1, Math.Max(_me.Sheet.Resources.GetValueOrDefault(name), _me.Resources.GetValueOrDefault(name)));
        if (cost.Uses is { } uses)
            share += 1.0 / Math.Max(1, uses);
        if (usable.Item is not null)
            share += 0.5;
        return _p.Hoard * 10 * share;
    }

    /// <summary>The unit as it would be on <paramref name="t"/>, having walked there.</summary>
    private UnitState Moved(Tile t) => Hypo(t).State.Units[_me.Id];

    /// <summary>The battle as it would be with the unit on <paramref name="t"/> (its Move spent getting there), and a
    /// working copy of it for the engine's targeting checks.</summary>
    private (BattleState State, BattleWriter W) Hypo(Tile t)
    {
        if (_hypo.TryGetValue(t, out var found))
            return found;
        var cost = _spots.TryGetValue(t, out var spot) ? spot.Cost : 0;
        var me = _me with { Pos = t, Economy = _me.Economy with { MoveLeft = _me.Economy.MoveLeft - cost } };
        var state = _s with { Units = _s.Units.SetItem(me.Id, me) };
        return _hypo[t] = (state, new BattleWriter(state, _rules));
    }

    /// <summary>One way to use an ability from a tile: the target, the aim, whom it affects and what that is worth.</summary>
    private sealed record Action(Target Target, Tile Aim, ImmutableArray<UnitId> Affected, double Value);

    private IEnumerable<Action> Actions(Usable usable, Tile from) => Actions(usable, from, Hypo(from).State);

    /// <summary>
    /// Every legal target or aim for <paramref name="usable"/> by the unit on <paramref name="from"/> in
    /// <paramref name="state"/>, with its worth. Single-target unit abilities try each unit; area abilities aim only at tiles
    /// whose area covers a unit they are meant for (a foe; an ally for ally abilities); self lines and cones try the four
    /// directions. Legality is the engine's own (<see cref="Battle.ResolveTarget"/>).
    /// </summary>
    private IEnumerable<Action> Actions(Usable usable, Tile from, BattleState state)
    {
        var ability = usable.Ability;
        var def = ability.Target;
        var area = def.Area;
        var me = state.Units[_me.Id];
        var w = ReferenceEquals(state, Hypo(from).State) ? Hypo(from).W : new BattleWriter(state, _rules);
        var everywhere = area.Shape is AreaShape.AllAllies or AreaShape.AllEnemies;
        var forAllies = def.Kind is TargetKind.Ally or TargetKind.DownedAlly;
        var units = state.Units.Values.Where(DownedRules.InBattle).ToArray();
        var meant = units.Where(u => forAllies ? !Scorer.Hostile(me, u) : Scorer.Hostile(me, u)).ToArray();
        var maxRange = def.Range + 5;

        IEnumerable<Target> targets;
        if (everywhere)
            targets = [new Target()];
        else if (def.Kind == TargetKind.Self)
            targets = area.Shape is AreaShape.Line or AreaShape.Cone
                ? Neighbours(from).Where(_scorer.Terrain.Map.InBounds).Select(t => new Target(Tile: t))
                : [new Target()];
        else if (area.Shape == AreaShape.Single && def.Kind is TargetKind.Ally or TargetKind.Enemy or TargetKind.AnyUnit or TargetKind.DownedAlly)
            targets = units.Where(u => Reach.Distance(from, u.Pos) <= maxRange).Select(u => new Target(Unit: u.Id));
        else if (area.Shape == AreaShape.Single && def.Kind == TargetKind.Tile)
            targets = meant.Where(u => Reach.Distance(from, u.Pos) <= maxRange).Select(u => new Target(Tile: u.Pos));
        else if (area.Shape == AreaShape.Single)
            targets = [];   // empty-tile abilities move the user or place something; the planner leaves them be
        else if (area.Shape is AreaShape.Line or AreaShape.Cone)
            targets = meant.Where(u => Reach.Distance(from, u.Pos) <= maxRange).Select(u => new Target(Tile: u.Pos));
        else
            targets = meant.SelectMany(u => Areas.Tiles(area, u.Pos, u.Pos, _scorer.Terrain)).Distinct().Order()
                .Where(t => Reach.Distance(from, t) <= maxRange).Select(t => new Target(Tile: t));

        var fixedAim = area.Shape is AreaShape.Burst or AreaShape.Cross or AreaShape.Ring && def.Kind != TargetKind.Self
                       && Scorer.FirstAttack(ability.Effects) is null;
        foreach (var target in targets)
        {
            if (Battle.ResolveTarget(w, me, ability, target, out var aim, out var affected) is not null)
                continue;
            double value;
            if (fixedAim && _aimValues.TryGetValue((usable.Command, usable.Item, aim), out var known))
                value = known;
            else
            {
                value = Value(usable, w, me, aim, affected);
                if (fixedAim)
                    _aimValues[(usable.Command, usable.Item, aim)] = value;
            }
            yield return new Action(target, aim, affected, value);
        }
    }

    private static IEnumerable<Tile> Neighbours(Tile t) => [t with { Y = t.Y - 1 }, t with { X = t.X + 1 }, t with { Y = t.Y + 1 }, t with { X = t.X - 1 }];

    /// <summary>
    /// What using <paramref name="usable"/> at <paramref name="aim"/> is worth: for each affected unit, what the ability's
    /// unit blocks (all its attacks, for the Attack action) are expected to do to it (<see cref="Scorer.Worth"/>); plus its
    /// tile blocks at the aim (a zone's effects on the units in it, a summon); less a little for dropping a concentration.
    /// </summary>
    private double Value(Usable usable, BattleWriter w, UnitState me, Tile aim, ImmutableArray<UnitId> affected)
    {
        var ability = usable.Ability;
        var effects = ability.Effects.IsDefault ? [] : ability.Effects;
        if (ability.IsWeaponAttack)
            effects = [.. Enumerable.Repeat(effects, Math.Max(1, me.Sheet.Attacks)).SelectMany(e => e)];
        var unitEffects = effects.Where(e => !e.OnTile).ToImmutableArray();
        var tileEffects = effects.Where(e => e.OnTile).ToImmutableArray();

        var st = new Strike(w, me, ability, aim, neutral: false);
        var value = 0.0;
        if (!unitEffects.IsEmpty)
            foreach (var id in affected)
            {
                var target = w.State.Units[id];
                value += _scorer.Worth(me, target, _scorer.Walk(unitEffects, st, target), _p);
            }
        foreach (var effect in tileEffects)
            value += TileValue(effect, st, aim);
        if (ability.Cost.Concentration && me.Concentrating is not null)
            value -= 2;
        return value;
    }

    /// <summary>
    /// A tile block's worth at the aim: a zone's turn-start and enter effects on the units in it now (enter effects at half,
    /// as units already there don't enter it), a difficult zone slowing foes in it, a hazard's enter effects at half, and
    /// a summon's damage per round for two rounds per unit summoned.
    /// </summary>
    private double TileValue(EffectDef effect, Strike st, Tile aim)
    {
        switch (effect)
        {
            case ZoneEffect zone:
                {
                    var tiles = Areas.Tiles(zone.Area, st.From, aim, _scorer.Terrain).ToImmutableSortedSet();
                    var rounds = Math.Min(3, zone.Rounds ?? 3);
                    var value = 0.0;
                    foreach (var unit in st.W.State.Units.Values.Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos)))
                    {
                        foreach (var passive in zone.Passives.IsDefault ? [] : zone.Passives)
                        {
                            var weight = passive.Hook switch
                            {
                                HookKind.OnTurnStart => rounds,
                                HookKind.OnEnterTile => 0.5,
                                _ => 0,
                            };
                            if (weight > 0 && passive.Target == PassiveTarget.Self && !passive.Effects.IsDefaultOrEmpty)
                                value += weight * _scorer.Worth(st.User, unit, _scorer.Walk(passive.Effects, st, unit), _p);
                        }
                        if (zone.Difficult && Scorer.Hostile(st.User, unit) && unit.Life == LifeState.Up)
                            value += 0.3 * _p.Control;
                    }
                    return value;
                }
            case HazardEffect hazard:
                {
                    var tiles = Areas.Tiles(hazard.Area, st.From, aim, _scorer.Terrain).ToImmutableSortedSet();
                    return st.W.State.Units.Values.Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos))
                        .Sum(u => 0.5 * _scorer.Worth(st.User, u, _scorer.Walk(hazard.OnEnter, st, u), _p));
                }
            case SummonEffect summon:
                return _rules.MonsterSheet(summon.Monster) is { } sheet
                    ? summon.Count * 2 * (1 + sheet.Attacks * Math.Max(0, sheet.Weapon.Damage.Average)) * 0.5 * _p.Damage
                    : 0;
            default:
                return 0;
        }
    }

    // ---- Candidates -------------------------------------------------------------------------------------------------

    private enum Kind { MoveAct, ActMove, Move, DashMove }

    /// <summary>
    /// A whole-turn plan before it is checked: its shape, total score, the ability and target, the tile it acts from, the
    /// tile it ends on, a disengage to use before walking, what the action is worth less its cost, and the end tile's
    /// position score (for the explanation); a dash's path, which the ordinary path map doesn't have.
    /// </summary>
    private sealed record Candidate(Kind Kind, double Score, Usable? Usable, Action? Action, Tile ActTile, Tile End,
        Usable? Escape, double ActionValue, double EndPosition, ImmutableArray<Tile> Path = default)
    {
        /// <summary>Best score first; ties by ability id, then the tile it acts from, then the target, then the shape.</summary>
        public static int Order(Candidate a, Candidate b)
        {
            var c = Round(b.Score).CompareTo(Round(a.Score));
            if (c == 0)
                c = string.CompareOrdinal(a.AbilityKey, b.AbilityKey);
            if (c == 0)
                c = a.ActTile.CompareTo(b.ActTile);
            if (c == 0)
                c = string.CompareOrdinal(a.TargetKey, b.TargetKey);
            if (c == 0)
                c = a.Kind.CompareTo(b.Kind);
            if (c == 0)
                c = a.End.CompareTo(b.End);
            return c;
        }

        private string AbilityKey => Usable is null ? "" : Usable.Command.Value + "/" + Usable.Item?.Value;

        public string TargetKey => Action?.Target switch
        {
            { Unit: { } unit } => "u:" + unit.Value,
            { Tile: { } tile } => $"t:{tile.X:D3},{tile.Y:D3}",
            _ => "",
        };
    }

    /// <summary>Scores compared to a millionth, so sums that differ only by rounding still tie.</summary>
    private static long Round(double score) => (long)Math.Round(score * 1e6);

    private TurnPlan Finish(Candidate chosen, ImmutableArray<Command> commands, List<Candidate> all)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"{_me.Id} ({_p.Id}{(_flee ? ", fleeing" : "")}): ");
        text.Append(string.Join("; ", commands.Select(Describe)));
        text.Append(CultureInfo.InvariantCulture,
            $" [score {chosen.Score:0.###}: action {chosen.ActionValue:0.###}, end tile {chosen.EndPosition:0.###}]");
        foreach (var other in all.Where(c => !ReferenceEquals(c, chosen)).Take(3))
            text.Append(CultureInfo.InvariantCulture,
                $"\n  also {other.Kind} {other.Usable?.Ability.Id.Value ?? "-"} {other.TargetKey} from {other.ActTile} to {other.End}: {other.Score:0.###}");
        return new TurnPlan(commands, chosen.Score, text.ToString());
    }

    private static string Describe(Command command) => command switch
    {
        Move m => $"move {m.Path[0]}→{m.Path[^1]}",
        Use { Item: { } item } u => $"use {item.Value} on {Describe(u.Target)}",
        Use u => $"{u.Ability.Value} on {Describe(u.Target)}",
        EndTurn => "end turn",
        _ => command.GetType().Name,
    };

    private static string Describe(Target target) => target.Unit?.Value ?? target.Tile?.ToString() ?? "self";
}
