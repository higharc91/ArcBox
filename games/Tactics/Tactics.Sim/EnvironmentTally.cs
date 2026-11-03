using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>Same-side/self conduction HP and temporary HP lost; unique burning tiles present in each round; directly lethal surface packets.</summary>
public sealed record EnvironmentStats(int Reactions, int FriendlyConductionDamage, int BurningTileRounds, int SurfaceDeaths,
    ImmutableSortedDictionary<string, int> ByRule);

/// <summary>Event-only environmental measures, independent of the simulator's class attribution.</summary>
internal sealed class EnvironmentTally(RulesPack rules)
{
    private readonly Dictionary<UnitId, Side> _sides = [];
    private readonly Dictionary<UnitId, Tile> _positions = [];
    private readonly Dictionary<UnitId, int> _hp = [];
    private readonly Dictionary<UnitId, int> _temp = [];
    private readonly Dictionary<int, Zone> _zones = [];
    private readonly HashSet<Tile> _burnedThisRound = [];
    private readonly HashSet<UnitId> _conductCandidates = [];
    private readonly HashSet<UnitId> _lethalSurface = [];
    private readonly HashSet<UnitId> _deadToSurface = [];
    private readonly SortedDictionary<string, int> _byRule = new(StringComparer.Ordinal);
    private readonly HashSet<string> _surfacePassives = [.. rules.Environment.Surfaces.Values.SelectMany(s => s.Passives.IsDefault ? [] : s.Passives)
        .Concat(rules.Environment.Reactions.SelectMany(r => r.Passives.IsDefault ? [] : r.Passives)).Select(p => p.Id)];
    private UnitId? _conductSource;
    private UnitId? _conductTarget;
    private bool _surface;
    private bool _roundPending;
    private bool _started;
    private int _burningRounds;
    private int _friendly;

    public EnvironmentStats Snapshot() => new(_byRule.Values.Sum(), _friendly, _burningRounds + _burnedThisRound.Count,
        _deadToSurface.Count, _byRule.ToImmutableSortedDictionary(StringComparer.Ordinal));

    public void Read(ImmutableArray<BattleEvent> events, BattleState state)
    {
        if (!state.Environment)
            return;
        foreach (var (id, u) in state.Units)
        {
            _sides[id] = u.Sheet.Side;
            _positions.TryAdd(id, u.Pos);
            _hp.TryAdd(id, u.Hp);
            _temp.TryAdd(id, u.TempHp);
        }
        if (!_started && events.IsEmpty)
            foreach (var zone in state.Zones)
            {
                _zones[zone.Serial] = zone;
                if (zone.Surface && zone.Kind == "burning") _burnedThisRound.UnionWith(zone.Tiles);
            }
        _started = true;
        foreach (var e in events)
        {
            switch (e)
            {
                case RoundStarted:
                    _burningRounds += _burnedThisRound.Count;
                    _burnedThisRound.Clear();
                    _roundPending = true;
                    ResetDamage();
                    break;
                case TurnStarted or TurnSkipped:
                    if (_roundPending)
                    {
                        foreach (var zone in _zones.Values.Where(z => z.Surface && z.Kind == "burning"))
                            _burnedThisRound.UnionWith(zone.Tiles);
                        _roundPending = false;
                    }
                    ResetDamage();
                    break;
                case AbilityUsed or TurnEnded:
                    ResetDamage();
                    break;
                case TileReacted reacted:
                    _byRule[reacted.Rule] = _byRule.GetValueOrDefault(reacted.Rule) + 1;
                    if (reacted.Rule == "conduct")
                    {
                        _conductSource = reacted.Source;
                        _conductTarget = null;
                        _conductCandidates.Clear();
                        _conductCandidates.UnionWith(_positions.Where(p => reacted.Tiles.Contains(p.Value)).Select(p => p.Key));
                    }
                    break;
                case Rolled { Kind: RollKind.Save } save when _conductSource is not null && _conductCandidates.Remove(save.Roller):
                    _conductTarget = save.Roller;
                    _surface = false;
                    break;
                case PassiveTriggered passive:
                    _surface = _surfacePassives.Contains(passive.Passive);
                    _conductTarget = null;
                    _lethalSurface.Clear();
                    break;
                case Damaged damage:
                    var lost = Math.Max(0, _hp.GetValueOrDefault(damage.Unit, damage.HpAfter) - damage.HpAfter);
                    var absorbed = Math.Min(_temp.GetValueOrDefault(damage.Unit), Math.Max(0, damage.Amount - lost));
                    _hp[damage.Unit] = damage.HpAfter;
                    _temp[damage.Unit] = _temp.GetValueOrDefault(damage.Unit) - absorbed;
                    if (_conductTarget == damage.Unit && damage.Type == "lightning" && _conductSource is { } caster
                        && _sides.TryGetValue(caster, out var side) && _sides.GetValueOrDefault(damage.Unit) == side
                        && (side != Side.Neutral || caster == damage.Unit))
                        _friendly += lost + absorbed;
                    _conductTarget = null;
                    if (_surface && damage.Amount > 0 && damage.HpAfter == 0) _lethalSurface.Add(damage.Unit);
                    else _lethalSurface.Remove(damage.Unit);
                    break;
                case TempHpGained gained: _temp[gained.Unit] = gained.TempHp; break;
                case Healed healed: _hp[healed.Unit] = healed.HpAfter; break;
                case Revived revived: _hp[revived.Unit] = revived.Hp; _lethalSurface.Remove(revived.Unit); break;
                case CountdownTicked tick when _surface && tick.Left == 0:
                    // A surface hit on an already-Downed unit advances its countdown instead of emitting Damaged.
                    _lethalSurface.Add(tick.Unit);
                    break;
                case Died died when _lethalSurface.Contains(died.Unit): _deadToSurface.Add(died.Unit); break;
                case UnitOut gone when _lethalSurface.Contains(gone.Unit): _deadToSurface.Add(gone.Unit); break;
                case Moved moved when !moved.Path.IsEmpty: _positions[moved.Unit] = moved.Path[^1]; break;
                case Pushed pushed: _positions[pushed.Unit] = pushed.To; break;
                case Teleported port: _positions[port.Unit] = port.To; break;
                case Spawned spawned: _positions[spawned.Unit] = spawned.At; break;
                case ZoneCreated created:
                    _zones[created.Zone.Serial] = created.Zone;
                    if (created.Zone.Surface && created.Zone.Kind == "burning") _burnedThisRound.UnionWith(created.Zone.Tiles);
                    break;
                case ZoneEnded ended: _zones.Remove(ended.Serial); break;
            }
        }
        foreach (var (id, u) in state.Units)
            (_hp[id], _temp[id], _positions[id]) = (u.Hp, u.TempHp, u.Pos);
    }

    private void ResetDamage()
    {
        (_conductSource, _conductTarget, _surface) = (null, null, false);
        _conductCandidates.Clear();
        _lethalSurface.Clear();
    }
}
