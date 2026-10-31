namespace Tactics.Logic;

/// <summary>Walking: validating a <see cref="Move"/> command and turning it into <see cref="MoveStep"/>s.</summary>
public static class Movement
{
    /// <summary>The condition (applied by the Disengage action) that stops a mover's steps from provoking.</summary>
    public static readonly ConditionId Disengaged = new("disengaged");

    /// <summary>
    /// Checks <paramref name="move"/> and, if it is legal, queues one <see cref="MoveStep"/> per tile. The path starts at the
    /// mover's tile and has at least one step; each step goes to a 4-adjacent tile that <see cref="Paths.StepCost"/> allows
    /// and no hostile unit holds; allies may be walked through but not ended on (<see cref="RejectCode.PathBlocked"/>). The
    /// steps together cost at most <see cref="TurnEconomy.MoveLeft"/> (<see cref="RejectCode.NoMove"/>). A unit that isn't
    /// Up or is incapacitated can't move (<see cref="RejectCode.Incapacitated"/>), nor can one that
    /// <see cref="StaticMods.CantMove"/> (<see cref="RejectCode.NoMove"/>). Returns the rejection, leaving the queue untouched.
    /// </summary>
    internal static Rejection? Plan(BattleWriter w, Move move)
    {
        var unit = w.Unit(move.Actor);
        if (!Modifiers.CanAct(w.State, unit.Id, w.Rules))
            return Rejection.Of(RejectCode.Incapacitated);
        if (Modifiers.For(w, unit.Id, w.Rules).CantMove)
            return Rejection.Of(RejectCode.NoMove);

        var path = move.Path.IsDefault ? [] : move.Path;
        var blocked = Rejection.Of(RejectCode.PathBlocked);
        if (path.Length < 2 || path[0] != unit.Pos)
            return blocked;

        var terrain = w.Terrain;
        var total = 0;
        for (var i = 1; i < path.Length; i++)
        {
            var (from, to) = (path[i - 1], path[i]);
            if (Reach.Distance(from, to) != 1)
                return blocked;
            var cost = Paths.StepCost(terrain, from, to, unit.Sheet.Jump);
            if (cost == int.MaxValue || Motion.OccupantAt(w, to, unit.Id) is { } other && Hostile(unit, other))
                return blocked;
            total += cost;
        }
        if (Motion.OccupantAt(w, path[^1], unit.Id) is not null)
            return blocked;
        if (total > unit.Economy.MoveLeft)
            return Rejection.Of(RejectCode.NoMove);

        // Each step remembers the last tile before it that the mover would hold alone, to fall back to if the move halts.
        var steps = new Step[path.Length - 1];
        var lastFree = path[0];
        for (var i = 1; i < path.Length; i++)
        {
            steps[i - 1] = new MoveStep(unit.Id, path[i - 1], path[i], lastFree);
            if (!Shared(w, path[i], unit.Id))
                lastFree = path[i];
        }
        w.PushFront(steps);
        return null;
    }

    /// <summary>True when another unit occupies the tile (see <see cref="Motion.OccupantAt"/>: Up or Downed).</summary>
    internal static bool Shared(BattleWriter w, Tile tile, UnitId unit) => Motion.OccupantAt(w, tile, unit) is not null;

    /// <summary>True when the two units fight on different sides.</summary>
    public static bool Hostile(UnitState a, UnitState b) => a.Sheet.Side != b.Sheet.Side;
}

/// <summary>
/// One step of a walk from <see cref="From"/> to the adjacent <see cref="To"/>. Unless the mover is disengaged, the step
/// first opens a <see cref="ReactionTriggerKind.LeavesReach"/> window (<see cref="Reactions.OffersFor"/>): one
/// <see cref="OfferReactionStep"/> per eligible reactor runs ahead of this step, which is re-queued as
/// <see cref="Checked"/>. Then the step spends its Move, moves the unit, emits <see cref="Moved"/> and fires the mover's
/// <see cref="HookKind.OnMove"/> and <see cref="HookKind.OnEnterTile"/> hooks.
/// <para>The move halts when the mover is no longer Up, has no HP left, isn't on <see cref="From"/> any more, can't move
/// (<see cref="StaticMods.CantMove"/>) or can no longer make the step (illegal, a hostile on <see cref="To"/>, or too little
/// Move). A halt removes the mover's remaining
/// <see cref="MoveStep"/>s from the queue and, if the mover is left sharing a tile (e.g. passing through an ally), puts it
/// back on <see cref="LastFree"/>, the last tile of its path it held alone, with a <see cref="Moved"/>.</para>
/// </summary>
public sealed record MoveStep(UnitId Unit, Tile From, Tile To, Tile LastFree, bool Checked = false) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var unit = w.Unit(Unit);
        if (unit.Life != LifeState.Up || unit.Hp <= 0 || unit.Pos != From)
        {
            Halt(w);
            return;
        }

        if (!Checked && unit.Conditions.All(c => c.Id != Movement.Disengaged))
        {
            var trigger = new ReactionTrigger(ReactionTriggerKind.LeavesReach, Unit, null, null, null, To);
            var offers = Reactions.OffersFor(w, trigger, rules);
            if (!offers.IsEmpty)
            {
                w.PushFront([.. offers.Select(o => new OfferReactionStep(o.Reactor, trigger)), this with { Checked = true }]);
                return;
            }
        }

        var cost = Paths.StepCost(w.Terrain, From, To, unit.Sheet.Jump);
        if (cost == int.MaxValue || cost > unit.Economy.MoveLeft || Modifiers.For(w, Unit, rules).CantMove
            || Motion.OccupantAt(w, To, Unit) is { } other && (Movement.Hostile(unit, other) || Final(w)))
        {
            Halt(w);
            return;
        }

        w.Set(unit with { Pos = To, Economy = unit.Economy with { MoveLeft = unit.Economy.MoveLeft - cost } });
        w.Emit(new Moved(Unit, [From, To]));
        Conditions.BreakIfIncapacitated(w, Unit);
        var hooks = new List<Step>();
        Hooks.Collect(w, HookKind.OnMove, new HookArgs(Unit, Tile: To), rules, hooks);
        Hooks.Collect(w, HookKind.OnEnterTile, new HookArgs(Unit, Tile: To), rules, hooks);
        if (hooks.Count > 0)
            w.PushFront([.. hooks]);
    }

    /// <summary>
    /// True for the walk's last step. It can pass through an ally but not stop on one; the plan checked the end tile was free,
    /// but a unit can arrive there during the walk (a trigger's spawn, a summon).
    /// </summary>
    private bool Final(BattleWriter w) => !w.State.Queue.Any(s => s is MoveStep step && step.Unit == Unit);

    private void Halt(BattleWriter w)
    {
        w.State = w.State with { Queue = w.State.Queue.RemoveAll(s => s is MoveStep step && step.Unit == Unit) };

        var unit = w.Unit(Unit);
        if (!DownedRules.InBattle(unit) || unit.Pos == LastFree || !Movement.Shared(w, unit.Pos, Unit))
            return;
        w.Set(unit with { Pos = LastFree });
        w.Emit(new Moved(Unit, [unit.Pos, LastFree]));
    }
}
