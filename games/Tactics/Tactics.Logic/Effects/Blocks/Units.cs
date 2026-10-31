using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// Summoned units. A summon gets the id <c>"&lt;owner&gt;.&lt;monster&gt;.&lt;serial&gt;"</c> (<see cref="BattleState.NextSerial"/>),
/// its owner's side and controller and <see cref="UnitState.SummonedBy"/>; it joins the turn order at the next round,
/// right after its owner (<see cref="BattleState.Joining"/>, <see cref="TurnOrder.Sort"/>). A summon with a duration or
/// tied to concentration carries <see cref="Marker"/> from its owner, which ends it like any condition (its rounds tick at
/// the owner's turn ends; the owner's concentration ending removes it); ending the marker despawns the summon
/// (<see cref="DownedRules.Despawn"/>). At 0 HP a summon dies (<see cref="DownedRules.GoesDown"/>).
/// </summary>
public static class Summons
{
    /// <summary>The condition that ends a summon's stay when it ends.</summary>
    public static readonly ConditionId Marker = new("summoned");

    /// <summary>
    /// Brings a <paramref name="monster"/> (<see cref="RulesPack.MonsterSheet"/>) into the battle on <paramref name="at"/>
    /// for <paramref name="owner"/>, for <paramref name="rounds"/> rounds (null: the whole battle) or, with
    /// <paramref name="concentration"/>, while the owner's concentration lasts. Emits <see cref="Spawned"/>. Throws
    /// <see cref="InvalidOperationException"/> when the rules pack has no such monster.
    /// </summary>
    public static UnitId Spawn(BattleWriter w, UnitId owner, string monster, Tile at, int? rounds, bool concentration)
    {
        var summoner = w.Unit(owner);
        var sheet = (w.Rules.MonsterSheet(monster)
                     ?? throw new InvalidOperationException($"There is no monster '{monster}' to summon.")) with { Side = summoner.Sheet.Side };
        var id = new UnitId($"{owner.Value}.{monster}.{w.State.NextSerial}");
        ImmutableArray<ActiveCondition> marker = rounds is null && !concentration
            ? []
            : [new ActiveCondition(Marker, owner, rounds, UntilSave: false, SaveDc: 0, Stat.Con, concentration,
                AppliedThisTurn: w.State.Active == owner)];
        var unit = new UnitState(id, sheet, summoner.Controller, at, sheet.MaxHp, TempHp: 0,
            sheet.Resources.WithComparers(StringComparer.Ordinal), marker, Economy.Fresh(sheet), LifeState.Up, Countdown: 0,
            UsesLeft: Ordinal.Map<int>(), Concentrating: null, Bag: [], ItemsUsedByAi: 0, SummonedBy: owner, Protected: false,
            UsedThisTurn: Ordinal.Set(), UsedThisRound: Ordinal.Set());
        w.State = w.State with
        {
            Units = w.State.Units.Add(id, unit), NextSerial = w.State.NextSerial + 1, Joining = w.State.Joining.Add(id),
        };
        w.Emit(new Spawned(id, at, sheet.Sprite));
        return id;
    }

    /// <summary>
    /// Where the next summon goes: the first free tile (<see cref="Motion.CanLand"/>) of the map zone
    /// <paramref name="mapZone"/> in its listed order when the map has it, else the free tile nearest
    /// <paramref name="aim"/> (ties in <see cref="Tile"/> order). Null when there is none.
    /// </summary>
    public static Tile? FreeTile(BattleWriter w, Tile aim, string? mapZone)
    {
        var map = w.Terrain.Map;
        IEnumerable<Tile> candidates = mapZone is not null && map.Zones.TryGetValue(mapZone, out var zone)
            ? zone
            : Enumerable.Range(0, map.Width).SelectMany(x => Enumerable.Range(0, map.Height).Select(y => new Tile(x, y)))
                .OrderBy(t => Reach.Distance(aim, t)).ThenBy(t => t);
        foreach (var tile in candidates)
            if (map.InBounds(tile) && Motion.CanLand(w, tile, default))
                return tile;
        return null;
    }
}

/// <summary>
/// Units taking another form (wild shape, polymorph). The form's sheet replaces the unit's, keeping its side and max HP,
/// and the form's HP become temporary HP; the unit's own sheet waits in <see cref="UnitState.TrueSheet"/>. The unit turns
/// back (<see cref="Revert"/>) when those temporary HP run out (<see cref="Hp.Damage"/>) or when <see cref="Marker"/>
/// ends (a polymorph's save or its caster's concentration).
/// </summary>
public static class Transforms
{
    /// <summary>The condition that ends a transformation when it ends.</summary>
    public static readonly ConditionId Marker = new("transformed");

    /// <summary>
    /// Gives <paramref name="unit"/> the form <paramref name="form"/> (with its own side and max HP) and
    /// <paramref name="tempHp"/> temporary HP; emits <see cref="Transformed"/>. A unit already in another form keeps its own
    /// sheet as the one it goes back to.
    /// </summary>
    public static void Into(BattleWriter w, UnitId unit, UnitSheet form, int tempHp)
    {
        var u = w.Unit(unit);
        var own = u.TrueSheet ?? u.Sheet;
        form = form with { Side = own.Side, MaxHp = own.MaxHp };
        w.Set(u with { Sheet = form, TrueSheet = own, TempHp = Math.Max(0, tempHp) });
        w.Emit(new Transformed(unit, form.Sprite ?? form.Name));
    }

    /// <summary>
    /// Turns <paramref name="unit"/> back into its own sheet, if it is in another form: the form's temporary HP go, its
    /// HP stay; emits <see cref="Transformed"/> and ends <see cref="Marker"/>.
    /// </summary>
    public static void Revert(BattleWriter w, UnitId unit)
    {
        var u = w.Unit(unit);
        if (u.TrueSheet is not { } own)
            return;
        w.Set(u with { Sheet = own, TrueSheet = null, TempHp = 0 });
        w.Emit(new Transformed(unit, own.Sprite ?? own.Name));
        Conditions.Remove(w, unit, Marker);
    }
}

/// <summary>
/// <c>"summon"</c>: brings the monster in as many times as asked (<see cref="Summons.Spawn"/>) on free tiles
/// (<see cref="Summons.FreeTile"/>); stops early when no tile is free.
/// </summary>
public sealed class SummonBlock : EffectBlock<SummonEffect>
{
    /// <inheritdoc />
    public override string Key => "summon";

    /// <inheritdoc />
    protected override string? Describe(SummonEffect def, int level, RulesPack rules)
    {
        var name = TooltipText.Humanize(def.Monster);
        var text = def.Count == 1 ? $"summons {TooltipText.Article(name)} {name}" : $"summons {def.Count} × {name}";
        return def.Concentration ? $"{text} while you concentrate"
            : def.Rounds is { } rounds ? $"{text} for {rounds} {(rounds == 1 ? "round" : "rounds")}"
            : $"{text} for the battle";
    }

    /// <inheritdoc />
    protected override void Run(SummonEffect def, EffectContext ctx)
    {
        var aim = ctx.Aim ?? ctx.SourceUnit.Pos;
        var rounds = Metamagic.ExtendRounds(ctx, def.Rounds, def.Concentration);
        for (var i = 0; i < def.Count; i++)
        {
            if (Summons.FreeTile(ctx.W, aim, def.Zone) is not { } at)
                return;
            Summons.Spawn(ctx.W, ctx.Source, def.Monster, at, rounds, def.Concentration);
        }
    }
}

/// <summary>
/// <c>"transform"</c>: runs the named hook (<see cref="NamedHooks"/>). Throws <see cref="InvalidOperationException"/> naming
/// an unknown hook.
/// </summary>
public sealed class TransformBlock : EffectBlock<TransformEffect>
{
    /// <inheritdoc />
    public override string Key => "transform";

    /// <inheritdoc />
    protected override string? Describe(TransformEffect def, int level, RulesPack rules)
        => NamedHooks.TryFor(def.Hook)?.Describe(def.Args, rules);

    /// <inheritdoc />
    protected override void Run(TransformEffect def, EffectContext ctx)
        => (NamedHooks.TryFor(def.Hook) ?? throw new InvalidOperationException($"There is no named hook '{def.Hook}'."))
            .Run(def.Args, ctx);
}
