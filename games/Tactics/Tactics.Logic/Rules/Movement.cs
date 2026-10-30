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
    /// Up can't move (<see cref="RejectCode.Incapacitated"/>). Returns the rejection, leaving the queue untouched.
    /// </summary>
    internal static Rejection? Plan(BattleWriter w, Move move)
    {
        var unit = w.Unit(move.Actor);
        if (unit.Life != LifeState.Up)
            return Rejection.Of(RejectCode.Incapacitated);

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

        w.PushFront([.. Enumerable.Range(1, path.Length - 1).Select(i => new MoveStep(unit.Id, path[i - 1], path[i]))]);
        return null;
    }

    /// <summary>True when the two units fight on different sides.</summary>
    public static bool Hostile(UnitState a, UnitState b) => a.Sheet.Side != b.Sheet.Side;
}

/// <summary>
/// One step of a walk from <see cref="From"/> to the adjacent <see cref="To"/>. Unless the mover is disengaged, the step
/// first opens a <see cref="ReactionTriggerKind.LeavesReach"/> window (<see cref="Reactions.OffersFor"/>): one
/// <see cref="OfferReactionStep"/> per eligible reactor runs ahead of this step, which is re-queued as
/// <see cref="Checked"/>. Then the step spends its Move, moves the unit and emits <see cref="Moved"/>.
/// <para>The step (and so the rest of the move) is dropped when the mover is no longer Up, has no HP left, isn't on
/// <see cref="From"/> any more, or can no longer make the step (illegal, a hostile on <see cref="To"/>, or too little Move).</para>
/// </summary>
public sealed record MoveStep(UnitId Unit, Tile From, Tile To, bool Checked = false) : Step
{
    /// <inheritdoc />
    public override void Run(BattleWriter w, RulesPack rules)
    {
        var unit = w.Unit(Unit);
        if (unit.Life != LifeState.Up || unit.Hp <= 0 || unit.Pos != From)
            return;

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
        if (cost == int.MaxValue || cost > unit.Economy.MoveLeft
            || Motion.OccupantAt(w, To, Unit) is { } other && Movement.Hostile(unit, other))
            return;

        w.Set(unit with { Pos = To, Economy = unit.Economy with { MoveLeft = unit.Economy.MoveLeft - cost } });
        w.Emit(new Moved(Unit, [From, To]));
    }
}
