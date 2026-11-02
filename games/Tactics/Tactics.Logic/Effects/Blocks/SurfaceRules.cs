using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>Deterministic surface geometry and round lifetimes. Only opted-in battles call these rules.</summary>
public static class Surfaces
{
    public static IEnumerable<Tile> Neighbours(Tile t)
        => [new(t.X - 1, t.Y), new(t.X, t.Y - 1), new(t.X, t.Y + 1), new(t.X + 1, t.Y)];
    public static bool Water(BattleWriter w, Tile tile)
        => w.Terrain.Map.InBounds(tile) && (w.Terrain.Map[tile].Flags & TileFlags.Water) != 0;
    public static bool Has(BattleWriter w, Tile t, string kind)
        => w.State.Zones.Any(z => z.Surface && z.Kind == kind && z.Tiles.Contains(t));
    private static bool Insulates(BattleWriter w, Tile t)
        => w.State.Zones.Any(z => z.Surface && z.Tiles.Contains(t)
            && w.Rules.Environment.Surfaces.GetValueOrDefault(z.Kind)?.Insulates == true);
    public static bool Fuel(BattleWriter w, EnvironmentDef def, Tile t)
    {
        if (!w.Terrain.Map.InBounds(t) || Water(w, t) || Has(w, t, "burning") || Has(w, t, "scorched"))
            return false;
        return def.IsFuelMaterial(w.Terrain.Map.Materials[w.Terrain.Map[t].Material])
            || w.State.Zones.Any(z => z.Tiles.Contains(t) && ((!def.ZoneFuel.IsDefaultOrEmpty && def.ZoneFuel.Contains(z.Kind))
                || z.Surface && def.Surfaces.GetValueOrDefault(z.Kind)?.Fuel == true));
    }
    public static bool Matches(BattleWriter w, EnvironmentDef def, string? selector, Tile t)
        => selector switch
        {
            "water" => Water(w, t),
            "shallows" => Water(w, t) && !w.Terrain.Deep(t) && !Has(w, t, "ice_sheet"),
            "fuel" => Fuel(w, def, t),
            { } value when value.StartsWith("surface:", StringComparison.Ordinal) => Has(w, t, value[8..]),
            { } value when value.StartsWith("zone:", StringComparison.Ordinal) => w.State.Zones.Any(z => z.Kind == value[5..] && z.Tiles.Contains(t)),
            _ => false,
        };

    /// <summary>All connected, uninsulated water within reach steps of any footprint water tile, breadth-first.</summary>
    public static ImmutableArray<Tile> Conducted(BattleWriter w, ImmutableArray<Tile> footprint, int reach)
    {
        var layer = new SortedSet<Tile>(footprint.Where(t => Water(w, t) && !Insulates(w, t)));
        var visited = new HashSet<Tile>();
        var result = ImmutableArray.CreateBuilder<Tile>();
        for (var distance = 0; distance <= reach && layer.Count > 0; distance++)
        {
            var next = new SortedSet<Tile>();
            foreach (var tile in layer)
            {
                if (!visited.Add(tile))
                    continue;
                result.Add(tile);
                if (distance == reach)
                    continue;
                foreach (var neighbour in Neighbours(tile))
                    if (!visited.Contains(neighbour) && Water(w, neighbour) && !Insulates(w, neighbour))
                        next.Add(neighbour);
            }
            layer = next;
        }
        return result.ToImmutable();
    }

    public static Zone? Create(BattleWriter w, string kind, ImmutableArray<Tile> tiles, UnitId? owner, string source,
        int? rounds = null, ImmutableArray<PassiveDef>? passives = null)
    {
        if (!w.State.Environment)
            return null;
        var def = w.Rules.Environment.Surfaces[kind];
        var valid = tiles.Where(w.Terrain.Map.InBounds).Distinct().Order().Where(t => !Has(w, t, kind)
            && (kind != "burning" || !Water(w, t) && !Has(w, t, "scorched"))
            && (kind != "ice_sheet" || Water(w, t) && !w.Terrain.Deep(t))
            && (kind != "oil" || !Has(w, t, "burning") && !Has(w, t, "ice_sheet"))).ToImmutableArray();
        if (valid.IsEmpty)
            return null;
        if (kind is "burning" or "ice_sheet" or "oil")
            RemoveTiles(w, z => z.Surface && z.Kind is "burning" or "ice_sheet" or "oil", valid);
        return ZoneRules.Create(w, owner, kind, valid, rounds ?? def.Rounds, concentration: false, def.Difficult,
            passives ?? def.Passives, surface: true, source: source);
    }

    /// <summary>Remove only the affected portion; retain all other zone lifetime and source metadata.</summary>
    public static void RemoveTiles(BattleWriter w, Func<Zone, bool> which, ImmutableArray<Tile> tiles)
    {
        var remove = tiles.ToHashSet();
        foreach (var zone in w.State.Zones.Where(z => which(z) && z.Tiles.Any(remove.Contains)).ToArray())
        {
            var left = zone.Tiles.Where(t => !remove.Contains(t)).ToImmutableArray();
            ZoneRules.End(w, z => z.Serial == zone.Serial);
            Zone? replacement = null;
            if (!left.IsEmpty)
                replacement = ZoneRules.Create(w, zone.Owner, zone.Kind, left, zone.RoundsLeft, zone.Concentration,
                    zone.Difficult, zone.Passives, zone.BlocksSight, zone.Surface, zone.Source, zone.AppliedThisTurn);
            RebindConditions(w, zone, replacement, remove);
        }
    }

    private static void RebindConditions(BattleWriter w, Zone removed, Zone? replacement, HashSet<Tile> tiles)
    {
        foreach (var id in w.State.Units.Keys.ToArray())
            foreach (var condition in w.Unit(id).Conditions.Where(c => c.SourceZone == removed.Serial).ToArray())
            {
                if (replacement is not null && !tiles.Contains(w.Unit(id).Pos))
                {
                    var unit = w.Unit(id);
                    var index = unit.Conditions.IndexOf(condition);
                    w.Set(unit with { Conditions = unit.Conditions.SetItem(index, condition with { SourceZone = replacement.Serial }) });
                }
                else if (removed.Kind == "web" && condition.Id.Value == "restrained")
                    Conditions.Remove(w, id, condition.Id);
                else
                {
                    var unit = w.Unit(id);
                    var index = unit.Conditions.IndexOf(condition);
                    w.Set(unit with { Conditions = unit.Conditions.SetItem(index, condition with { SourceZone = null }) });
                }
            }
    }

    public static void Consume(BattleWriter w, string? selector, ImmutableArray<Tile> tiles)
    {
        if (selector is { } value && value.StartsWith("zone:", StringComparison.Ordinal))
            RemoveTiles(w, z => !z.Surface && z.Kind == value[5..], tiles);
        else if (selector == "fuel")
            RemoveTiles(w, z => (!w.Rules.Environment.ZoneFuel.IsDefaultOrEmpty && w.Rules.Environment.ZoneFuel.Contains(z.Kind)), tiles);
    }

    public static void Douse(BattleWriter w, UnitId unit, Tile tile)
    {
        if (!w.State.Environment || !Water(w, tile))
            return;
        foreach (var rule in w.Rules.Environment.Reactions.Where(r => r.Enters == "water" && r.Remove is not null))
        {
            var condition = new ConditionId(rule.Remove!);
            if (!w.Unit(unit).Conditions.Any(c => c.Id == condition))
                continue;
            w.Emit(new TileReacted(rule.Id, unit, [tile]));
            Conditions.Remove(w, unit, condition);
        }
    }

    public static void RecordHit(BattleWriter w, UnitId source, AbilityId? ability, bool hit)
    {
        if (!w.State.Environment || !hit || ability is null)
            return;
        for (var i = 0; i < w.State.Queue.Count; i++)
            if (w.State.Queue[i] is ReactStep reaction && !reaction.Completed && reaction.Source == source && reaction.Ability == ability)
            {
                w.State = w.State with { Queue = w.State.Queue.SetItem(i, reaction with { Hit = true }) };
                break;
            }
    }

    public static void RecordDamage(EffectContext ctx, DamageEffect effect)
    {
        var w = ctx.W;
        if (!w.State.Environment || w.HookDepth != 0 || ctx.SourceZone is not null || ctx.Target is not { } target)
            return;
        effect = effect with { Type = effect.Type ?? (effect.Weapon ? ctx.SourceUnit.Sheet.Weapon.DamageType : null) };
        for (var i = 0; i < w.State.Queue.Count; i++)
            if (w.State.Queue[i] is ReactStep reaction && !reaction.Completed && reaction.Source == ctx.Source && reaction.Ability == ctx.Ability?.Id)
            {
                var seen = reaction.Observed.IsDefault ? [] : reaction.Observed;
                if (seen.Any(d => d.Effect.Type == effect.Type && d.Target != target))
                    return;
                w.State = w.State with { Queue = w.State.Queue.SetItem(i, reaction with { Observed = seen.Add(new(target, effect)) }) };
                return;
            }
    }

    public static void TerrainChanged(BattleWriter w)
    {
        if (!w.State.Environment)
            return;
        var invalid = w.State.Zones.Where(z => z.Surface && z.Kind == "ice_sheet").SelectMany(z => z.Tiles)
            .Where(t => !Water(w, t) || w.Terrain.Deep(t)).Distinct().Order().ToImmutableArray();
        if (!invalid.IsEmpty)
            RemoveTiles(w, z => z.Surface && z.Kind == "ice_sheet", invalid);
    }

    public static void RoundStart(BattleWriter w)
    {
        if (!w.State.Environment)
            return;
        var before = w.State.Zones.Where(z => z.Surface).ToArray();
        var claimed = new HashSet<Tile>();
        var spreading = new List<(UnitId? Owner, ImmutableArray<Tile> Tiles)>();
        foreach (var group in before.Where(z => w.Rules.Environment.Surfaces[z.Kind].Spreads).GroupBy(z => z.Owner).OrderBy(g => g.Key))
        {
            var candidates = group.OrderBy(z => z.Serial).SelectMany(z => z.Tiles.Order()).SelectMany(Neighbours)
                .Where(t => Fuel(w, w.Rules.Environment, t)).Distinct().Order().Where(claimed.Add).ToImmutableArray();
            if (!candidates.IsEmpty)
                spreading.Add((group.Key, candidates));
        }
        // Age only the snapshot: new spread tiles get their full duration.
        foreach (var zone in before)
        {
            if (zone.RoundsLeft is not { } rounds)
                continue;
            if (rounds > 1)
            {
                var index = w.State.Zones.IndexOf(zone);
                w.State = w.State with { Zones = w.State.Zones.SetItem(index, zone with { RoundsLeft = rounds - 1 }) };
                continue;
            }
            ZoneRules.End(w, z => z.Serial == zone.Serial);
            if (w.Rules.Environment.Surfaces[zone.Kind].Leaves is { } spent)
                Create(w, spent, zone.Tiles, zone.Owner, zone.Source ?? "spread");
        }
        foreach (var (owner, tiles) in spreading)
        {
            w.Emit(new TileReacted("spread", owner, tiles));
            Consume(w, "fuel", tiles);
            Create(w, "burning", tiles, owner, "spread");
        }
    }
}
