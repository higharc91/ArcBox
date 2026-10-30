namespace Tactics.Logic;

/// <summary>
/// Forced movement: pushes and pulls with their collisions and falls, and the units that stand on tiles. Forced movement
/// never provokes opportunity attacks and spends no Move.
/// </summary>
public static class Motion
{
    private const string Bludgeoning = "bludgeoning";

    /// <summary>The condition a long fall leaves the unit in.</summary>
    public static readonly ConditionId Prone = new("prone");

    /// <summary>
    /// The unit step from <paramref name="from"/> toward <paramref name="to"/> along the dominant axis (a tie goes to X), or
    /// (0, 0) when they are the same tile.
    /// </summary>
    public static (int Dx, int Dy) Toward(Tile from, Tile to)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        if (dx == 0 && dy == 0)
            return (0, 0);
        return Math.Abs(dx) >= Math.Abs(dy) ? (Math.Sign(dx), 0) : (0, Math.Sign(dy));
    }

    /// <summary>The unit other than <paramref name="except"/> standing or lying (Up or Downed) on the tile, if any.</summary>
    public static UnitState? OccupantAt(BattleWriter w, Tile tile, UnitId except)
        => w.State.Units.Values.FirstOrDefault(u => u.Id != except && u.Pos == tile && u.Life is LifeState.Up or LifeState.Downed);

    /// <summary>True when <paramref name="unit"/> could be put on <paramref name="tile"/>: walkable and no one else on it.</summary>
    public static bool CanLand(BattleWriter w, Tile tile, UnitId unit)
        => w.Terrain.Walkable(tile) && OccupantAt(w, tile, unit) is null;

    /// <summary>
    /// Pushes <paramref name="unit"/> up to <paramref name="distance"/> tiles, one at a time, by <paramref name="direction"/>.
    /// Each tile emits <see cref="Pushed"/>. The push stops:
    /// <list type="bullet">
    /// <item>before a blocked tile: off the map, impassable, a blocking prop, a climb of 2+ levels or a unit. The pushed
    /// unit takes 1d4 bludgeoning, and so does the unit it hit (<see cref="Collided"/> for each);</item>
    /// <item>on a deep tile (<see cref="Fell"/>, into deep): the unit drops as at 0 HP (<see cref="DownedRules.Drop"/>, by
    /// <paramref name="by"/>), so a party member is Downed and anyone else dies; one already Downed keeps its
    /// countdown;</item>
    /// <item>after a drop of more than 3 levels: 1d6 bludgeoning per level beyond 3, then prone if still standing
    /// (<see cref="Fell"/>).</item>
    /// </list>
    /// Each tile entered fires the unit's <see cref="HookKind.OnEnterTile"/> hooks.
    /// </summary>
    public static void Push(BattleWriter w, UnitId unit, (int Dx, int Dy) direction, int distance, UnitId? by = null)
    {
        if (direction == (0, 0))
            return;

        for (var i = 0; i < distance; i++)
        {
            var u = w.Unit(unit);
            var terrain = w.Terrain;
            var from = u.Pos;
            var next = new Tile(from.X + direction.Dx, from.Y + direction.Dy);

            var wall = !terrain.Map.InBounds(next)
                       || (terrain.Map[next].Flags & TileFlags.Impassable) != 0
                       || terrain.PropAt(next) is { Blocks: true }
                       || terrain.HeightAt(next) - terrain.HeightAt(from) > 1;
            var other = wall ? null : OccupantAt(w, next, unit);
            if (wall || other is not null)
            {
                Collide(w, unit, other?.Id);
                return;
            }

            var drop = terrain.HeightAt(from) - terrain.HeightAt(next);
            w.Set(u with { Pos = next });
            w.Emit(new Pushed(unit, from, next));
            Entered(w, unit, next);

            if (terrain.Deep(next))
            {
                w.Emit(new Fell(unit, Math.Max(0, drop), 0, IntoDeep: true));
                DownedRules.Drop(w, unit, by);   // an already-Downed party member keeps its countdown
                return;
            }

            if (drop > 3)
            {
                var damage = Rolls.Damage(w, new Dice(drop - 3, 6, 0), crit: false);
                w.Emit(new Fell(unit, drop, damage, IntoDeep: false));
                Hp.Damage(w, unit, damage, Bludgeoning);
                if (w.Unit(unit).Life == LifeState.Up)
                    KnockProne(w, unit);
                return;
            }
        }
    }

    private static void Collide(BattleWriter w, UnitId unit, UnitId? other)
    {
        var damage = w.Roll(4);
        w.Emit(new Collided(unit, other, damage));
        Hp.Damage(w, unit, damage, Bludgeoning);
        if (other is not { } hit)
            return;

        var otherDamage = w.Roll(4);
        w.Emit(new Collided(hit, unit, otherDamage));
        Hp.Damage(w, hit, otherDamage, Bludgeoning);
    }

    /// <summary>Puts <paramref name="unit"/> prone (<see cref="ConditionApplied"/>), replacing any prone it had.</summary>
    internal static void KnockProne(BattleWriter w, UnitId unit)
    {
        var u = w.Unit(unit);
        var prone = new ActiveCondition(Prone, Source: null, RoundsLeft: null, UntilSave: false, SaveDc: 0, Stat.Con,
            Concentration: false);
        w.Set(u with { Conditions = u.Conditions.RemoveAll(c => c.Id == Prone).Add(prone) });
        w.Emit(new ConditionApplied(unit, Prone));
    }

    /// <summary>Puts <paramref name="unit"/> on <paramref name="to"/>, emits <see cref="Teleported"/> and fires <see cref="HookKind.OnEnterTile"/>.</summary>
    internal static void Teleport(BattleWriter w, UnitId unit, Tile to)
    {
        var u = w.Unit(unit);
        w.Set(u with { Pos = to });
        w.Emit(new Teleported(unit, u.Pos, to));
        Entered(w, unit, to);
    }

    /// <summary>Fires <paramref name="unit"/>'s <see cref="HookKind.OnEnterTile"/> hooks for <paramref name="tile"/>.</summary>
    internal static void Entered(BattleWriter w, UnitId unit, Tile tile)
        => Hooks.Fire(w, HookKind.OnEnterTile, new HookArgs(unit, Tile: tile), w.Rules);
}

/// <summary><c>"push"</c>: pushes the target away from the source (or the aimed tile); see <see cref="Motion.Push"/>.</summary>
public sealed class PushBlock : EffectBlock<PushEffect>
{
    /// <inheritdoc />
    public override string Key => "push";

    /// <inheritdoc />
    protected override void Run(PushEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var origin = def.AwayFromSource ? ctx.SourceUnit.Pos : ctx.Aim ?? ctx.SourceUnit.Pos;
        Motion.Push(ctx.W, target.Id, Motion.Toward(origin, target.Pos), def.Distance, ctx.Source);
    }
}

/// <summary>
/// <c>"pull"</c>: draws the target toward the source along the dominant axis, at most until it is level with the source on
/// that axis and never onto the source's tile. Collisions and falls work as for <see cref="Motion.Push"/>.
/// </summary>
public sealed class PullBlock : EffectBlock<PullEffect>
{
    /// <inheritdoc />
    public override string Key => "pull";

    /// <inheritdoc />
    protected override void Run(PullEffect def, EffectContext ctx)
    {
        var target = ctx.TargetUnit(Key);
        var source = ctx.SourceUnit.Pos;
        var direction = Motion.Toward(target.Pos, source);
        var (along, across) = direction.Dx != 0
            ? (Math.Abs(source.X - target.Pos.X), Math.Abs(source.Y - target.Pos.Y))
            : (Math.Abs(source.Y - target.Pos.Y), Math.Abs(source.X - target.Pos.X));
        var room = across == 0 ? along - 1 : along;
        Motion.Push(ctx.W, target.Id, direction, Math.Min(def.Distance, room), ctx.Source);
    }
}

/// <summary><c>"teleport"</c>: moves the source (or target) to the aimed tile when it is walkable, empty and in range.</summary>
public sealed class TeleportBlock : EffectBlock<TeleportEffect>
{
    /// <inheritdoc />
    public override string Key => "teleport";

    /// <inheritdoc />
    protected override void Run(TeleportEffect def, EffectContext ctx)
    {
        var unit = def.Self ? ctx.SourceUnit : ctx.TargetUnit(Key);
        if (ctx.Aim is not { } aim || aim == unit.Pos || Reach.Distance(unit.Pos, aim) > def.Range || !Motion.CanLand(ctx.W, aim, unit.Id))
            return;
        Motion.Teleport(ctx.W, unit.Id, aim);
    }
}

/// <summary><c>"swap"</c>: the source and the target exchange tiles (a <see cref="Teleported"/> for each).</summary>
public sealed class SwapBlock : EffectBlock<SwapEffect>
{
    /// <inheritdoc />
    public override string Key => "swap";

    /// <inheritdoc />
    protected override void Run(SwapEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        var target = ctx.TargetUnit(Key);
        if (source.Id == target.Id)
            return;
        Motion.Teleport(ctx.W, source.Id, target.Pos);
        Motion.Teleport(ctx.W, target.Id, source.Pos);
    }
}

/// <summary>
/// <c>"dash"</c>: the source walks toward the aimed tile, one legal step at a time (<see cref="Paths.StepCost"/>) along the
/// dominant axis, up to the tile count, stopping before any unit. One <see cref="Moved"/> for the whole run, then the
/// source's <see cref="HookKind.OnMove"/> and <see cref="HookKind.OnEnterTile"/> hooks. A unit that can't move
/// (<see cref="StaticMods.CantMove"/>) stays put.
/// </summary>
public sealed class DashBlock : EffectBlock<DashEffect>
{
    /// <inheritdoc />
    public override string Key => "dash";

    /// <inheritdoc />
    protected override void Run(DashEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        if (ctx.Aim is not { } aim || Modifiers.For(ctx.W, source.Id, ctx.Rules).CantMove)
            return;

        var terrain = ctx.W.Terrain;
        var path = new List<Tile> { source.Pos };
        var at = source.Pos;
        while (path.Count <= def.Tiles && at != aim)
        {
            var (dx, dy) = Motion.Toward(at, aim);
            var next = new Tile(at.X + dx, at.Y + dy);
            if (Paths.StepCost(terrain, at, next, source.Sheet.Jump) == int.MaxValue || Motion.OccupantAt(ctx.W, next, source.Id) is not null)
                break;
            path.Add(next);
            at = next;
        }
        if (path.Count < 2)
            return;

        ctx.W.Set(source with { Pos = at });
        ctx.W.Emit(new Moved(source.Id, [.. path]));
        var hooks = new List<Step>();
        Hooks.Collect(ctx.W, HookKind.OnMove, new HookArgs(source.Id, Tile: at), ctx.Rules, hooks);
        Hooks.Collect(ctx.W, HookKind.OnEnterTile, new HookArgs(source.Id, Tile: at), ctx.Rules, hooks);
        if (hooks.Count > 0)
            ctx.W.PushFront([.. hooks]);
    }
}

/// <summary>
/// <c>"jumpTo"</c>: the source leaps to the aimed tile, ignoring Jump, when it is walkable, empty and in range and the
/// source can move; then its <see cref="HookKind.OnEnterTile"/> hooks fire.
/// </summary>
public sealed class JumpToBlock : EffectBlock<JumpToEffect>
{
    /// <inheritdoc />
    public override string Key => "jumpTo";

    /// <inheritdoc />
    protected override void Run(JumpToEffect def, EffectContext ctx)
    {
        var source = ctx.SourceUnit;
        if (ctx.Aim is not { } aim || aim == source.Pos || Reach.Distance(source.Pos, aim) > def.Range || !Motion.CanLand(ctx.W, aim, source.Id)
            || Modifiers.For(ctx.W, source.Id, ctx.Rules).CantMove)
            return;

        ctx.W.Set(source with { Pos = aim });
        ctx.W.Emit(new Moved(source.Id, [source.Pos, aim]));
        Motion.Entered(ctx.W, source.Id, aim);
    }
}
