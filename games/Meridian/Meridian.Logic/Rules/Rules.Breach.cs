namespace Meridian.Logic;

// The Breach (spec §2.6; plan Task 8). Choosing a scenario and everything after it is stage M3.
internal sealed partial class Resolver
{
    /// <summary>After an Anomaly is held: roll as many dice as Anomalies drawn, or fire at once on the forced one.</summary>
    partial void AnomalyDrawn(SeatId seat, CardId card)
    {
        if (State.Phase != Phase.Explore)
        {
            return;   // the Breach has already fired: an Anomaly found later is just an item
        }
        if (State.AnomaliesDrawn >= R.ForcedBreachAnomaly)
        {
            Fire(new BreachInfo(seat, card, State.CrewOf(seat).At, Forced: true, Roll: null));
            return;
        }
        State = State with { Waiting = new RollPending(seat, RollReason.Breach, null, State.AnomaliesDrawn, card) };
    }

    partial void BreachRolled(RollPending roll, int total)
    {
        if (total >= R.BreachThreshold)
        {
            Fire(new BreachInfo(roll.From, roll.Card, State.CrewOf(roll.From).At, Forced: false, Roll: total));
            return;
        }
        Events.Add(new BreachHeld(roll.From, total, R.BreachThreshold));
    }

    /// <summary>
    /// Checked after every command: a ship with nowhere left to grow fires the Breach, because no more Anomalies can be
    /// drawn and Explore would otherwise never end (spec §2.6). No Anomaly and no compartment are recorded, so the
    /// scenario is chosen from every runnable one.
    /// </summary>
    private void AfterStep()
    {
        if (State.Phase == Phase.Explore && State.Waiting is null && !CanGrow(Pack, State))
        {
            Fire(new BreachInfo(State.ActiveSeat, Anomaly: null, Compartment: null, Forced: true, Roll: null));
        }
    }

    /// <summary>Some frontier hatch faces a deck that still has a tile in the pile.</summary>
    internal static bool CanGrow(RulesPack pack, GameState state)
    {
        var decks = state.TilePile.SelectMany(t => pack.Tile(t).Decks).ToHashSet();
        return state.Ship.Frontier(pack.Rules.DeckRadius).Any(h => decks.Contains(state.Ship.Get(h.Compartment).At.Deck));
    }

    private void Fire(BreachInfo breach)
    {
        State = State with { Phase = Phase.Breach, Breach = breach, Waiting = null };
        Events.Add(new BreachFired(breach));
    }
}
