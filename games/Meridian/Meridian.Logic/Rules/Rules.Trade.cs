namespace Meridian.Logic;

// Trading (spec §2.4; plan Task 9).
internal sealed partial class Resolver
{
    /// <summary>
    /// Why an offer isn't allowed, or null. Offers aren't enumerated in <see cref="Rules.Legal"/> (their combinations
    /// multiply), so they get this predicate instead. The caller has already checked it's the offerer's free turn.
    /// </summary>
    public static string? TradeRejection(GameState state, OfferTrade offer)
    {
        if (state.Turn.Traded)
        {
            return "You've already traded this turn.";
        }
        if (offer.To == offer.From || state.IndexOf(offer.To) < 0)
        {
            return "Trade with another crew member.";
        }
        if (state.CrewOf(offer.To).At != state.CrewOf(offer.From).At)
        {
            return "You can only trade with someone in the same compartment.";
        }
        if (offer.Offer.Give.IsEmpty && offer.Offer.Take.IsEmpty)
        {
            return "Offer or ask for at least one item.";
        }
        return Carries(state, offer.From, offer.Offer.Give) && Carries(state, offer.To, offer.Offer.Take)
            ? null
            : "Only items each of you is carrying can be traded.";
    }

    private static bool Carries(GameState state, SeatId seat, EqArray<CardId> items)
        => items.Distinct().Count() == items.Count && items.All(state.CrewOf(seat).Items.Contains);

    /// <summary>A sample of legal offers: give one, ask for one, or swap one for one, with each seat alongside.</summary>
    public static IEnumerable<OfferTrade> SampleTrades(GameState state, SeatId seat)
    {
        if (state.Phase != Phase.Explore || state.Waiting is not null || seat != state.ActiveSeat || state.Turn.Traded)
        {
            yield break;
        }
        var me = state.CrewOf(seat);
        foreach (var other in state.Crew.Where(c => c.Seat != seat && c.At == me.At))
        {
            foreach (var give in me.Items)
            {
                yield return new OfferTrade(seat, other.Seat, new TradeOffer([give], []));
                foreach (var take in other.Items)
                {
                    yield return new OfferTrade(seat, other.Seat, new TradeOffer([give], [take]));
                }
            }
            foreach (var take in other.Items)
            {
                yield return new OfferTrade(seat, other.Seat, new TradeOffer([], [take]));
            }
        }
    }

    private void Offer(OfferTrade offer)
    {
        State = State with
        {
            Turn = State.Turn with { Traded = true },
            Waiting = new TradePending(offer.To, offer.From, offer.Offer),
        };
        Events.Add(new TradeOffered(offer.From, offer.To, offer.Offer));
    }

    /// <summary>Accepting swaps the items if both still hold them; declining, or a timeout, leaves things as they were.</summary>
    private void Answer(bool accept)
    {
        var trade = (TradePending)State.Waiting!;
        State = State with { Waiting = null };
        var swapped = accept && Carries(State, trade.Offerer, trade.Offer.Give) && Carries(State, trade.From, trade.Offer.Take);
        if (swapped)
        {
            var offerer = State.CrewOf(trade.Offerer);
            var answerer = State.CrewOf(trade.From);
            State = State
                .WithCrew(offerer with { Items = offerer.Items.Where(i => !trade.Offer.Give.Contains(i)).Concat(trade.Offer.Take).ToEqArray() })
                .WithCrew(answerer with { Items = answerer.Items.Where(i => !trade.Offer.Take.Contains(i)).Concat(trade.Offer.Give).ToEqArray() });
        }
        Events.Add(new TradeAnswered(trade.Offerer, trade.From, swapped));
    }
}
