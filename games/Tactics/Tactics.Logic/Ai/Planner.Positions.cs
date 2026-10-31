using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Planner
{
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
    /// getting away, staying near a protected ally and, for the party when the win asks it to reach a zone, closing on the
    /// zone (the objectives). A companion also guards: standing next to a healer or
    /// protected ally adds half the objective weight × the damage the foes threaten it with next round (capped at its HP).
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
        foreach (var (pos, danger) in Guarded())
            if (Reach.Distance(pos, t) <= 1)
                score += 0.5 * _p.Objective * danger;
        var goal = _scorer.ReachTiles(_me);
        if (!goal.IsEmpty)
            score -= 0.3 * Math.Max(0.5, _p.Objective) * goal.Min(g => Reach.Distance(g, t));
        return score;
    }

    private ImmutableArray<(Tile Pos, double Danger)>? _guarded;

    /// <summary>
    /// For a companion, the allies it guards (spec §5.5: the healer and protected NPCs: <see cref="Companion.HealerRole"/>
    /// in their class's roles, or protected) that the foes threaten, with that threat (capped at their HP); none otherwise.
    /// </summary>
    private ImmutableArray<(Tile Pos, double Danger)> Guarded() => _guarded ??= !_companion ? [] : [.. _s.Units.Values
        .Where(u => u.Id != _me.Id && u.Life == LifeState.Up && !Scorer.Hostile(_me, u)
                    && (u.Protected || Companion.RoleTags(u, _rules).Contains(Companion.HealerRole)))
        .Select(u => (u.Pos, Danger: Math.Min(_scorer.ThreatAgainst(u.Sheet.Side).At(u.Pos), u.Hp + u.TempHp)))
        .Where(g => g.Danger > 0)];

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
}
