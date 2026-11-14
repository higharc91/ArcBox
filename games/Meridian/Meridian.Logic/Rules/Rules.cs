namespace Meridian.Logic;

public sealed record StepResult(GameState State, EqArray<GameEvent> Events);

/// <summary>
/// The only way the game changes (spec §5.2). Pure: the same pack, state and command always give the same result,
/// because all randomness comes from the <see cref="Rng"/> inside the state.
/// </summary>
public static class Rules
{
    /// <summary>
    /// A new game: the start layout, everyone in the Airlock on their starting steps, shuffled piles and a random first
    /// seat. Seats take turns in the order given. Throws on a bad seat list: that is a bug in the adapter, not input.
    /// </summary>
    public static GameState NewGame(RulesPack pack, IReadOnlyList<(SeatId Seat, CrewId Crew)> seats, ulong seed)
    {
        var rules = pack.Rules;
        if (seats.Count < rules.MinPlayers || seats.Count > rules.MaxPlayers)
        {
            throw new ArgumentException($"Meridian seats {rules.MinPlayers}-{rules.MaxPlayers} players, not {seats.Count}.", nameof(seats));
        }
        if (seats.Select(s => s.Seat).Distinct().Count() != seats.Count || seats.Select(s => s.Crew).Distinct().Count() != seats.Count)
        {
            throw new ArgumentException("Every seat and every crew member must be different.", nameof(seats));
        }
        if (seats.FirstOrDefault(s => !pack.HasCrew(s.Crew)) is { Crew.Value: not null } unknown)
        {
            throw new ArgumentException($"Unknown crew member {unknown.Crew}.", nameof(seats));
        }

        var rng = Rng.Seeded(seed);
        var ship = ShipBuilder.Start(pack);
        var entry = ShipBuilder.Entry(pack, ship);
        var crew = seats.Select(s => new CrewState(s.Seat, s.Crew, entry, TraitSteps.Starting(pack.CrewMember(s.Crew).Tracks)))
                        .ToEqArray();

        (var tiles, rng) = rng.Shuffle(pack.Tiles.SelectMany(t => Enumerable.Repeat(t.Id, t.Copies)).ToEqArray());
        (var incidents, rng) = rng.Shuffle(pack.CardsOf(CardKind.Incident).Select(c => c.Id).ToEqArray());
        (var salvage, rng) = rng.Shuffle(pack.CardsOf(CardKind.Salvage).Select(c => c.Id).ToEqArray());
        (var anomalies, rng) = rng.Shuffle(pack.CardsOf(CardKind.Anomaly).Select(c => c.Id).ToEqArray());
        (var first, rng) = rng.Below(crew.Count);

        return new GameState(rng, Phase.Explore, ship, crew, first, first, Round: 1,
                             MovesLeft: Value(pack, crew[first], Trait.Agility), new TurnFlags(), tiles,
                             new CardPile(incidents), new CardPile(salvage), new CardPile(anomalies),
                             AnomaliesDrawn: 0, Waiting: null, Breach: null);
    }

    /// <summary>A trait's current value: the number under its marker.</summary>
    public static int Value(RulesPack pack, CrewState crew, Trait trait)
        => pack.CrewMember(crew.Crew).Tracks[trait].ValueAt(crew.Steps[trait]);

    /// <summary>
    /// Every command <paramref name="seat"/> may send now, except trade offers, whose combinations aren't listed (see
    /// Rules.TradeOffers). The phone's buttons and the bots both come from this, and <see cref="Step"/>
    /// accepts exactly these, so the three can never disagree.
    /// </summary>
    public static EqArray<Command> Legal(RulesPack pack, GameState state, SeatId seat)
    {
        if (state.IndexOf(seat) < 0)
        {
            return [];
        }
        if (state.Phase == Phase.Breach)
        {
            // Briefings: the only command is the adapter's Begin, once the scenario is set up.
            return state.Board is not null && seat == state.ActiveSeat ? [new Begin(seat)] : [];
        }
        if (state.Phase is not (Phase.Explore or Phase.Endgame))
        {
            return [];
        }

        var legal = new List<Command>();
        if (state.Waiting is { } waiting)
        {
            if (waiting.From == seat)
            {
                Resolver.Answers(pack, state, waiting, legal);
                legal.Add(new Timeout(seat));
            }
            return [.. legal];
        }

        if (seat != state.ActiveSeat)
        {
            return [];
        }

        Resolver.Actions(pack, state, seat, legal);
        legal.Add(new EndTurn(seat));
        legal.Add(new Timeout(seat));
        return [.. legal];
    }

    /// <summary>A sample of trades the active seat could offer now: give one, ask for one, or swap one for one.</summary>
    public static IEnumerable<OfferTrade> TradeOffers(GameState state, SeatId seat) => Resolver.SampleTrades(state, seat);

    public static StepResult Step(RulesPack pack, GameState state, Command command)
    {
        if (Rejection(pack, state, command) is { } reason)
        {
            return new StepResult(state, [new Rejected(command, reason)]);
        }

        var resolver = new Resolver(pack, state);
        resolver.Apply(command);
        return new StepResult(resolver.State, [.. resolver.Events]);
    }

    private static string? Rejection(RulesPack pack, GameState state, Command command)
    {
        if (state.Phase is not (Phase.Explore or Phase.Endgame) && command is not Begin)
        {
            return "Nobody can act now.";
        }
        if (state.IndexOf(command.From) < 0)
        {
            return "That seat isn't in this game.";
        }
        if (state.Waiting is { } waiting && waiting.From != command.From)
        {
            return $"Waiting on {waiting.From}.";
        }
        if (state.Waiting is null && command.From != state.ActiveSeat)
        {
            return "It isn't your turn.";
        }
        if (command is OfferTrade offer)
        {
            return state.Waiting is null ? Resolver.TradeRejection(state, offer) : "Not now.";
        }
        return Legal(pack, state, command.From).Contains(command) ? null : "That isn't allowed now.";
    }
}
