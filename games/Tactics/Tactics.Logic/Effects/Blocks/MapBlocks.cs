using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Zones on the map (<see cref="BattleState.Zones"/>): laying them, what they give the units standing in them, their
/// durations and their end. A zone's passives belong to every unit in the battle standing on one of its tiles: their
/// hooks fire for that unit (<see cref="Hooks"/>: <c>onEnterTile</c> as it arrives, <c>onTurnStart</c> as its turn
/// starts, …) with the zone's owner as the source of their effects, and their static modifiers apply to it
/// (<see cref="Modifiers"/>). A difficult zone's tiles cost 2 Move (<see cref="BattleWriter.Terrain"/>).
/// </summary>
public static class ZoneRules
{
    /// <summary>The passives of the zones covering <paramref name="unit"/>'s tile, with their zone, in zone order; none when it isn't in the battle.</summary>
    public static IEnumerable<(PassiveDef Passive, Zone Zone)> PassivesAt(BattleState state, UnitState unit)
    {
        if (!DownedRules.InBattle(unit) || state.Zones.IsDefaultOrEmpty)
            yield break;
        foreach (var zone in state.Zones)
            if (zone.Tiles.Contains(unit.Pos))
                foreach (var passive in zone.Passives.IsDefault ? [] : zone.Passives)
                    yield return (passive, zone);
    }

    /// <summary>The tiles of every zone that blocks line of sight (<see cref="Zone.BlocksSight"/>).</summary>
    public static ImmutableSortedSet<Tile> Obscured(BattleState state)
        => state.Zones.IsDefaultOrEmpty || !state.Zones.Any(z => z.BlocksSight)
            ? ImmutableSortedSet<Tile>.Empty
            : [.. state.Zones.Where(z => z.BlocksSight).SelectMany(z => z.Tiles)];

    /// <summary>
    /// Lays a zone numbered <see cref="BattleState.NextSerial"/> (which moves on) and emits <see cref="ZoneCreated"/>. Made
    /// during its owner's turn, it is marked <see cref="Zone.AppliedThisTurn"/>. A concentrating unit on its tiles that it
    /// leaves incapacitated loses its concentration. Units already there aren't "entering" it.
    /// </summary>
    public static Zone Create(BattleWriter w, UnitId? owner, string kind, ImmutableArray<Tile> tiles, int? rounds,
        bool concentration, bool difficult, ImmutableArray<PassiveDef> passives, bool blocksSight = false)
    {
        var zone = new Zone(w.State.NextSerial, owner, kind, tiles, rounds, concentration, difficult,
            passives.IsDefault ? [] : passives, AppliedThisTurn: owner is { } id && w.State.Active == id, BlocksSight: blocksSight);
        w.State = w.State with { Zones = w.State.Zones.Add(zone), NextSerial = w.State.NextSerial + 1 };
        w.Emit(new ZoneCreated(zone));
        foreach (var unit in w.State.Units.Values.Where(u => DownedRules.InBattle(u) && tiles.Contains(u.Pos)).Select(u => u.Id).ToArray())
            Conditions.BreakIfIncapacitated(w, unit);
        return zone;
    }

    /// <summary>Ends every zone <paramref name="which"/> picks, emitting <see cref="ZoneEnded"/> for each in zone order.</summary>
    public static void End(BattleWriter w, Func<Zone, bool> which)
    {
        var ended = w.State.Zones.Where(which).Select(z => z.Serial).ToArray();
        if (ended.Length == 0)
            return;
        w.State = w.State with { Zones = w.State.Zones.RemoveAll(z => ended.Contains(z.Serial)) };
        foreach (var serial in ended)
            w.Emit(new ZoneEnded(serial));
    }

    /// <summary>
    /// The end of <paramref name="unit"/>'s turn (its own or a skipped one): its zones with a duration tick like its
    /// conditions (<see cref="Conditions.EndTurn"/>): one at 0 rounds ends; one made this turn only loses that mark; any
    /// other loses a round and ends at 0.
    /// </summary>
    internal static void EndTurn(BattleWriter w, UnitId unit) => Tick(w, z => z.Owner == unit);

    /// <summary>A new round: zones whose owner is no longer in the battle (or that have none) tick, as their owner would have.</summary>
    internal static void TickOrphans(BattleWriter w)
    {
        var units = w.State.Units;
        Tick(w, z => z.Owner is not { } owner || !units.TryGetValue(owner, out var u) || !DownedRules.InBattle(u));
    }

    private static void Tick(BattleWriter w, Func<Zone, bool> ticks)
    {
        if (!w.State.Zones.Any(z => z.RoundsLeft is not null && ticks(z)))
            return;
        var expired = new List<int>();
        var zones = w.State.Zones.Select(z =>
        {
            if (z.RoundsLeft is not { } left || !ticks(z))
                return z;
            if (left <= 0 || (!z.AppliedThisTurn && left == 1))
            {
                expired.Add(z.Serial);
                return z;
            }
            return z.AppliedThisTurn ? z with { AppliedThisTurn = false } : z with { RoundsLeft = left - 1 };
        }).ToImmutableArray();
        w.State = w.State with { Zones = zones };
        End(w, z => expired.Contains(z.Serial));
    }
}

/// <summary><c>"zone"</c>: lays a zone owned by the source on the area's tiles around the aim (<see cref="ZoneRules.Create"/>).</summary>
public sealed class ZoneBlock : EffectBlock<ZoneEffect>
{
    /// <inheritdoc />
    public override string Key => "zone";

    /// <inheritdoc />
    protected override string? Describe(ZoneEffect def, int level, RulesPack rules)
    {
        var text = $"creates a {TooltipText.Humanize(def.Kind)} zone ({TooltipText.Area(def.Area)}) "
                   + (def.Rounds is { } rounds ? TooltipText.ForRounds(rounds) : "for the battle");
        if (def.Difficult)
            text += ", difficult terrain";
        if (def.BlocksSight)
            text += ", blocks line of sight";
        if (def.Concentration)
            text += ", ends if you lose concentration";
        if (!def.Passives.IsDefaultOrEmpty)
            text += "; units inside: " + string.Join(" / ", def.Passives.Select(p => Tooltips.For(p, level, rules)));
        return text;
    }

    /// <inheritdoc />
    protected override void Run(ZoneEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        var tiles = Areas.Tiles(def.Area, source.Pos, ctx.Aim ?? source.Pos, ctx.W.Terrain);
        if (!tiles.IsEmpty)
            ZoneRules.Create(ctx.W, source.Id, def.Kind, tiles, Metamagic.ExtendRounds(ctx, def.Rounds, def.Concentration),
                def.Concentration, def.Difficult, def.Passives, def.BlocksSight);
    }
}

/// <summary>
/// <c>"hazard"</c>: a zone of the hazard's art key on the area's tiles around the aim, whose one passive
/// (<c>hazard.&lt;art&gt;</c>, on <c>onEnterTile</c>, on the unit entering) runs the hazard's effects.
/// </summary>
public sealed class HazardBlock : EffectBlock<HazardEffect>
{
    /// <inheritdoc />
    public override string Key => "hazard";

    /// <inheritdoc />
    protected override string? Describe(HazardEffect def, int level, RulesPack rules)
    {
        var text = $"creates a {TooltipText.Humanize(def.HazardArt)} hazard ({TooltipText.Area(def.Area)}) {TooltipText.ForRounds(def.Rounds)}";
        return def.OnEnter.IsDefaultOrEmpty ? text : $"{text}; entering it: {Tooltips.Group(def.OnEnter, level, rules)}";
    }

    /// <inheritdoc />
    protected override void Run(HazardEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        var tiles = Areas.Tiles(def.Area, source.Pos, ctx.Aim ?? source.Pos, ctx.W.Terrain);
        if (tiles.IsEmpty)
            return;
        var passive = new PassiveDef("hazard." + def.HazardArt, HookKind.OnEnterTile, null, def.OnEnter.IsDefault ? [] : def.OnEnter,
            null, Target: PassiveTarget.Self);
        ZoneRules.Create(ctx.W, source.Id, def.HazardArt, tiles, def.Rounds, concentration: false, difficult: false, [passive]);
    }
}

/// <summary>
/// <c>"alterTerrain"</c>: changes the height of the area's tiles around the aim by the delta, each kept within 0–9
/// (<see cref="BattleState.HeightDelta"/>, which drops a tile back at its base height). Units stand on top of their tile
/// wherever it goes. Emits <see cref="TerrainAltered"/> with the tiles that changed, if any did.
/// </summary>
public sealed class AlterTerrainBlock : EffectBlock<AlterTerrainEffect>
{
    /// <summary>The highest a tile can be.</summary>
    public const int MaxHeight = 9;

    /// <inheritdoc />
    public override string Key => "alterTerrain";

    /// <inheritdoc />
    protected override string? Describe(AlterTerrainEffect def, int level, RulesPack rules)
        => def.Delta == 0
            ? $"leaves the terrain ({TooltipText.Area(def.Area)}) as it is"
            : $"{(def.Delta > 0 ? "raises" : "sinks")} the terrain ({TooltipText.Area(def.Area)}) by {Math.Abs(def.Delta)}";

    /// <inheritdoc />
    protected override void Run(AlterTerrainEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        var terrain = ctx.W.Terrain;
        var deltas = ctx.W.State.HeightDelta;
        var changed = ImmutableArray.CreateBuilder<Tile>();
        foreach (var tile in Areas.Tiles(def.Area, source.Pos, ctx.Aim ?? source.Pos, terrain))
        {
            var now = terrain.HeightAt(tile);
            var height = Math.Clamp(now + def.Delta, 0, MaxHeight);
            if (height == now)
                continue;
            var delta = height - terrain.Map[tile].Height;
            deltas = delta == 0 ? deltas.Remove(tile) : deltas.SetItem(tile, delta);
            changed.Add(tile);
        }
        if (changed.Count == 0)
            return;
        ctx.W.State = ctx.W.State with { HeightDelta = deltas };
        ctx.W.Emit(new TerrainAltered(changed.ToImmutable(), def.Delta));
    }
}
