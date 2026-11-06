using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// The plans that move the unit more than one walk does (#110): move–act–move for the ranged and skirmisher profiles, a
/// bonus-action teleport or leap before acting, and a bonus-action Dash before acting.
/// </summary>
internal sealed partial class Planner
{
    /// <summary>The profiles that walk out, act and walk back (move–act–move): the ranged one and the skirmisher.</summary>
    private static readonly ImmutableSortedSet<string> HitAndRunProfiles = Ordinal.Set("archer", "skirmisher");

    private bool? _hitAndRun;

    /// <summary>
    /// True when the unit plays a ranged or skirmisher profile (<see cref="HitAndRunProfiles"/>): for a companion, the one its
    /// class's roles name (<see cref="AiProfile.IdForRoles"/>), whose weights it plays with.
    /// </summary>
    private bool HitAndRun => _hitAndRun ??= HitAndRunProfiles.Contains(
        _companion ? AiProfile.IdForRoles(Companion.RoleTags(_me, _rules)) : _p.Id);

    /// <summary>True when the unit fights in melee: no ranged weapon, and nothing that deals damage reaches past 2 tiles.</summary>
    private bool Melee => !_me.Sheet.Weapon.Ranged && _reach <= 2;

    /// <summary>How many tiles to strike from are kept for move–act–move, besides the best positions.</summary>
    private const int KeptStrikeTiles = 4;

    /// <summary>
    /// The best <see cref="KeptStrikeTiles"/> reachable tiles (by spot score, ties by tile) with a foe within the unit's reach
    /// (<see cref="_reach"/>, straight-line): in move–act–move the unit acts there and walks on, so they needn't be good
    /// places to end the turn, and the best positions alone may all be out of reach.
    /// </summary>
    private IEnumerable<Tile> StrikeTiles() => _spots.Values
        .Where(s => _foes.Any(f => Reach.Distance(f.Pos, s.Tile) <= _reach))
        .OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).Take(KeptStrikeTiles).Select(s => s.Tile);

    /// <summary>
    /// The walk after acting in move–act–move: the tile it ends on, as the spot of the whole walk (both legs), its score with
    /// the disengage used first if that pays, and the path of the second leg.
    /// </summary>
    private sealed record Retreating(Spot Spot, double Score, Usable? Escape, ImmutableArray<Tile> Path);

    private readonly Dictionary<(Tile, int, bool), Retreating?> _retreats = [];

    /// <summary>
    /// The best tile to walk on to from <paramref name="from"/> (walked to first, and acted from) with <paramref name="budget"/>
    /// Move left. Each is scored as the whole walk (<see cref="SpotAt"/> on both legs, so a foe's opportunity attack counts
    /// once and the zone damage on the way in full), with <paramref name="escape"/> used first when that pays
    /// (<see cref="Arrive"/>: a disengage lasts the turn). Null when no other tile is in reach, or when the first walk may
    /// leave the unit stuck (<see cref="MayStop"/>); ties go to the lower tile.
    /// </summary>
    private Retreating? Retreat(Tile from, int budget, Usable? escape)
    {
        var key = (from, budget, escape is not null);
        if (_retreats.TryGetValue(key, out var known))
            return known;
        Retreating? best = null;
        var first = _paths.PathTo(from);
        if (budget > 0 && !MayStop(first))
        {
            var map = Paths.From(_scorer.Terrain, from, Mover(budget));
            foreach (var t in map.Reachable)
            {
                if (t == from)
                    continue;
                var then = map.PathTo(t);
                var spot = SpotAt(t, [.. first, .. then]);
                var (score, used) = Arrive(spot, escape);
                if (best is null || Round(score) > Round(best.Score))
                    best = new Retreating(spot, score, used, then);
            }
        }
        return _retreats[key] = best;
    }

    private ImmutableArray<ImmutableArray<Tile>>? _stoppers;

    /// <summary>
    /// True when walking <paramref name="path"/> enters a zone whose enter effects may stop the unit moving (a web's
    /// Restrained): a plan can't count on moving or leaping after it.
    /// </summary>
    private bool MayStop(ImmutableArray<Tile> path)
    {
        _stoppers ??= [.. (_s.Zones.IsDefault ? [] : _s.Zones)
            .Where(z => !z.Passives.IsDefaultOrEmpty && z.Passives.Any(p => p.Hook == HookKind.OnEnterTile && Scorer.All(p.Effects)
                .Any(e => e is ApplyEffect a && Halts(a.Condition) || e is StatModEffect { Mods.CantMove: true })))
            .Select(z => z.Tiles)];
        return !_stoppers.Value.IsEmpty && !path.IsDefaultOrEmpty && path.Any(t => _stoppers.Value.Any(tiles => tiles.Contains(t)));
    }

    private readonly Dictionary<int, PathMap> _far = [];

    /// <summary>Where the unit can walk after <paramref name="dash"/>: its Move left, less the dash's Move cost, plus its Move.</summary>
    private PathMap Far(Usable dash)
    {
        var budget = _me.Economy.MoveLeft - dash.Ability.Cost.Move + _move;
        if (!_far.TryGetValue(budget, out var map))
            _far[budget] = map = Paths.From(_scorer.Terrain, _me.Pos, Mover(budget));
        return map;
    }

    /// <summary>
    /// "Bonus-action Dash, walk further, act": for each bonus-action dash the unit can use, the best <see cref="KeptTiles"/>
    /// tiles only the dash brings in reach (<see cref="Far"/>), and from each every action, as in "move, then act". A dash that
    /// also disengages (Withdraw) draws no opportunity attack on the way.
    /// </summary>
    private IEnumerable<Candidate> DashThenAct()
    {
        foreach (var dash in _usables.Where(u => u.Dash && u.Phase == Phase.Bonus && Affordable(_me, u)))
        {
            var far = Far(dash);
            var dashed = Advance(_s, new Use(_me.Id, dash.Command, new Target(), dash.Item));
            var spots = far.Reachable.Where(t => !_spots.ContainsKey(t)).Select(t => SpotAt(t, far.PathTo(t), escaped: dash.Escape))
                .OrderByDescending(s => Round(s.Score)).ThenBy(s => s.Tile).Take(KeptTiles).ToArray();
            foreach (var spot in spots)
            {
                var state = Advance(dashed, new Move(_me.Id, [_me.Pos, .. spot.Path]));
                var me = state.Units[_me.Id];
                var w = new BattleWriter(state, _rules);
                foreach (var usable in _usables.Where(u => Leads(u) && Affordable(me, u)))
                {
                    var hoard = Hoard(usable) + Hoard(dash);
                    foreach (var action in Actions(usable, spot.Tile, state, w))
                        if (action.Value > Epsilon && ItemOk(usable, spot.Tile, action))
                        {
                            var end = usable.Hop > 0 ? action.Aim : spot.Tile;
                            yield return new Candidate(Kind.DashAct, spot.Score + action.Value - hoard, usable, action, spot.Tile, end,
                                null, action.Value - Hoard(usable), usable.Hop > 0 ? Land(end) : spot.Position, spot.Path, dash);
                        }
                }
            }
        }
    }

    /// <summary>
    /// "Walk, teleport or leap, act" for each bonus action that moves the unit to an empty tile (<see cref="HopRoutes"/>), then
    /// every action from where it lands, as in "move, then act". Scored as the walk to where it is used from (its Move, zone
    /// damage and opportunity attacks, but not that tile's position), what landing is worth (<see cref="Land"/>) and the
    /// action, less what both cost.
    /// </summary>
    private IEnumerable<Candidate> HopThenAct()
    {
        foreach (var hop in _usables.Where(u => u.Hop > 0 && u.Phase == Phase.Bonus && Affordable(_me, u)))
            foreach (var (via, landing) in HopRoutes(hop))
            {
                var state = Advance(Hypo(via).State, new Use(_me.Id, hop.Command, new Target(Tile: landing), hop.Item));
                var me = state.Units[_me.Id];
                var w = new BattleWriter(state, _rules);
                var walk = _spots[via];
                var lead = walk.Score - walk.Place + Land(landing) - Hoard(hop);
                foreach (var usable in _usables.Where(u => Leads(u) && Affordable(me, u)))
                {
                    var hoard = Hoard(usable);
                    foreach (var action in Actions(usable, landing, state, w))
                        if (action.Value > Epsilon && ItemOk(usable, landing, action))
                            yield return new Candidate(Kind.HopAct, lead + action.Value - hoard, usable, action, landing,
                                usable.Hop > 0 ? action.Aim : landing, null, action.Value - hoard, Land(landing), Lead: hop, Via: via);
                }
            }
    }

    /// <summary>
    /// Where <paramref name="hop"/> is planned from and to: from the unit's own tile to its <see cref="Landings"/> (near the best
    /// position tiles); and for a unit that fights in melee (<see cref="Melee"/>), to the empty tiles beside each foe, each
    /// from the reachable tile whose walk costs least (the unit's own if it is in range), keeping the best
    /// <see cref="KeptLandings"/> by that walk and the landing. Every route is one the engine allows (range, line of sight).
    /// </summary>
    private List<(Tile Via, Tile Landing)> HopRoutes(Usable hop)
    {
        var here = _me.Pos;
        var routes = Landings(here, hop.Hop).Where(t => CanHop(hop, here, t)).Select(t => (here, t)).ToList();
        if (!Melee)
            return routes;
        var terrain = _scorer.Terrain;
        var taken = Taken();
        var vias = _spots.Values.Where(s => !MayStop(s.Path)).OrderByDescending(s => Round(s.Score - s.Place)).ThenBy(s => s.Tile).ToArray();
        var beside = new List<(Tile Via, Tile Landing, double Score)>();
        foreach (var landing in _foes.SelectMany(f => Neighbours(f.Pos)).Distinct().Order())
        {
            if (landing == here || !terrain.Map.InBounds(landing) || !terrain.Walkable(landing) || taken.Contains(landing)
                || routes.Any(r => r.Item2 == landing))
                continue;
            foreach (var via in vias)
                if (Reach.Distance(via.Tile, landing) <= hop.Hop && CanHop(hop, via.Tile, landing))
                {
                    beside.Add((via.Tile, landing, via.Score - via.Place + Land(landing)));
                    break;
                }
        }
        routes.AddRange(beside.OrderByDescending(b => Round(b.Score)).ThenBy(b => b.Landing).Take(KeptLandings)
            .Select(b => (b.Via, b.Landing)));
        return routes;
    }

    /// <summary>True when the engine lets the unit, having walked to <paramref name="via"/>, use <paramref name="hop"/> on <paramref name="landing"/>.</summary>
    private bool CanHop(Usable hop, Tile via, Tile landing)
        => Battle.ResolveTarget(Hypo(via).W, Moved(via), hop.Ability, new Target(Tile: landing), out _, out _) is null;
}
