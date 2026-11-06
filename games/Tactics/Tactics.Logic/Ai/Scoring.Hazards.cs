using System.Collections.Immutable;

namespace Tactics.Logic;

internal sealed partial class Scorer
{
    private Dictionary<Tile, List<(Zone Zone, PassiveDef Passive)>>? _hazards;
    private readonly Dictionary<(UnitId, Tile, int, string, bool), (bool Fires, double Damage)> _hazardDamage = [];

    /// <summary>
    /// Expected zone damage on the existing path and from staying until the next turn starts. This scores, rather than
    /// changes, the path search. Entry excludes the starting tile. Passive limits are shared across the path and turn
    /// end, and reset for the next turn/round, while per-battle uses remain spent. No effects are applied or dice drawn.
    /// </summary>
    public (double Entering, double Staying) Hazards(UnitState unit, Tile destination, ImmutableArray<Tile> path)
    {
        if (State.Zones.IsDefaultOrEmpty)
            return (0, 0);
        if (_hazards is null)
        {
            _hazards = [];
            foreach (var zone in State.Zones)
                foreach (var passive in zone.Passives.IsDefault ? [] : zone.Passives)
                    if (passive.Target == PassiveTarget.Self
                        && passive.Hook is HookKind.OnEnterTile or HookKind.OnTurnEnd or HookKind.OnTurnStart
                        && Deals(passive.Effects))
                        foreach (var tile in zone.Tiles)
                        {
                            if (!_hazards.TryGetValue(tile, out var entries))
                                _hazards[tile] = entries = [];
                            entries.Add((zone, passive));
                        }
        }
        if (_hazards.Count == 0)
            return (0, 0);

        var turn = new HashSet<string>(unit.UsedThisTurn, StringComparer.Ordinal);
        var round = new HashSet<string>(unit.UsedThisRound, StringComparer.Ordinal);
        var uses = new Dictionary<string, int>(StringComparer.Ordinal);

        double DamageAt(Tile tile, HookKind hook, bool nextTurn = false)
        {
            if (!_hazards.TryGetValue(tile, out var entries))
                return 0;
            var damage = 0.0;
            foreach (var (zone, passive) in entries)
            {
                if (passive.Hook != hook || passive.OncePerTurn && turn.Contains(passive.Id)
                    || passive.OncePerRound && round.Contains(passive.Id))
                    continue;
                var left = uses.GetValueOrDefault(passive.Id, unit.UsesLeft.GetValueOrDefault(passive.Id, passive.UsesPerBattle ?? 0));
                if (passive.UsesPerBattle is not null && left <= 0)
                    continue;
                var outcome = HazardDamage(unit, tile, zone, passive, nextTurn);
                if (!outcome.Fires)
                    continue;
                damage += outcome.Damage;
                if (passive.OncePerTurn)
                    turn.Add(passive.Id);
                if (passive.OncePerRound)
                    round.Add(passive.Id);
                if (passive.UsesPerBattle is not null)
                    uses[passive.Id] = left - 1;
            }
            return damage;
        }

        var entering = 0.0;
        // PathMap.PathTo already excludes the origin: its first tile is a real entry.
        for (var i = 0; i < path.Length; i++)
            entering += DamageAt(path[i], HookKind.OnEnterTile);
        var staying = DamageAt(destination, HookKind.OnTurnEnd);
        // The mover's next ordinary turn is in the following round. Turn-start clears all per-turn uses.
        turn.Clear();
        round.Clear();
        staying += DamageAt(destination, HookKind.OnTurnStart, nextTurn: true);
        return (entering, staying);
    }

    private (bool Fires, double Damage) HazardDamage(UnitState unit, Tile tile, Zone zone, PassiveDef passive, bool nextTurn)
    {
        var key = (unit.Id, tile, zone.Serial, passive.Id, nextTurn);
        if (_hazardDamage.TryGetValue(key, out var cached))
            return cached;
        var target = unit with { Pos = tile };
        var state = State with { Units = State.Units.SetItem(unit.Id, target), Round = State.Round + (nextTurn ? 1 : 0) };
        var writer = new BattleWriter(state, Rules);
        if (passive.When is { } when && !Hooks.Holds(when, writer, new HookArgs(unit.Id, Tile: tile), Rules))
            return _hazardDamage[key] = (false, 0);
        // Match Hooks.Candidates: the zone owner supplies its level and save DC; orphaned zones use the affected unit.
        var source = zone.Owner is { } owner && state.Units.TryGetValue(owner, out var caster) ? caster : target;
        var strike = new Strike(writer, source, null, tile, neutral: false);
        return _hazardDamage[key] = (true, Walk(passive.Effects, strike, target).Damage.Mean);
    }
}
