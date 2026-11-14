namespace Meridian.Logic;

// Applying effects (spec §2.5; plan Task 6). Effects always apply to the seat that caused them.
internal sealed partial class Resolver
{
    /// <summary>
    /// Applies effects in order. A terminal effect (a draw, or ending the turn) is always last (Validation), so a wait
    /// it opens never strands effects behind it.
    /// </summary>
    private void ApplyEffects(SeatId seat, EqArray<Effect> effects)
    {
        foreach (var effect in effects)
        {
            switch (effect)
            {
                case Gain g:
                    ChangeSteps(seat, g.Traits, g.Steps, gaining: true);
                    break;
                case Lose l:
                    ChangeSteps(seat, l.Traits, l.Steps, gaining: false);
                    break;
                case Moves m:
                    State = State with { MovesLeft = Math.Max(0, State.MovesLeft + m.Delta) };
                    Events.Add(new MovesChanged(seat, State.MovesLeft));
                    break;
                case MoveToTile m:
                    MoveToFirst(seat, m.Tile);
                    break;
                case DrawCard d:
                    DrawFor(d.Card);
                    break;
                case EndTurnNow:
                    FinishTurn();
                    break;
                case ExtraDice:
                    break;   // passive: read when a check counts its dice
            }
        }
    }

    /// <summary>
    /// Moves markers one step at a time. A gain goes to the lowest-step trait in the group that can still rise; a loss
    /// comes off the highest-step trait that can still fall, so damage spreads (ties: the earlier trait). Before the
    /// Breach no marker goes below step 1: the rest of a loss is dropped, not remembered (Review focus 3).
    /// </summary>
    private void ChangeSteps(SeatId seat, TraitGroup group, int steps, bool gaining)
    {
        var crew = State.CrewOf(seat);
        var before = crew.Steps;
        var now = before;
        var floor = State.Phase == Phase.Explore ? 1 : 0;
        for (var i = 0; i < steps; i++)
        {
            Trait? pick = null;
            foreach (var trait in group.Members())
            {
                var step = now[trait];
                var movable = gaining ? step < R.TrackSteps : step > floor;
                if (movable && (pick is not { } p || (gaining ? step < now[p] : step > now[p])))
                {
                    pick = trait;
                }
            }
            if (pick is not { } chosen)
            {
                break;
            }
            now = now.With(chosen, now[chosen] + (gaining ? 1 : -1));
        }

        State = State.WithCrew(crew with { Steps = now });
        foreach (var trait in Traits.All.Where(t => before[t] != now[t]))
        {
            Events.Add(new TraitChanged(seat, trait, before[trait], now[trait]));
        }
        CheckDeath(seat);
    }

    private void MoveToFirst(SeatId seat, TileId tile)
    {
        var crew = State.CrewOf(seat);
        if (State.Ship.Compartments.FirstOrDefault(c => c.Tile == tile) is { } target && target.Id != crew.At)
        {
            State = State.WithCrew(crew with { At = target.Id });
            Events.Add(new Moved(seat, crew.At, target.Id, 0));
        }
    }

    /// <summary>Extra dice from carried items' passive effects.</summary>
    private static int ExtraDiceFor(RulesPack pack, CrewState crew, Trait trait)
        => crew.Items.Sum(id => pack.Card(id).Item?.Passive.OfType<ExtraDice>().Where(e => e.Trait == trait).Sum(e => e.Dice) ?? 0);
}
