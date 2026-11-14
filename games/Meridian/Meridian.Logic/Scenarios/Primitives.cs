namespace Meridian.Logic;

/// <summary>
/// The shared building blocks scenarios use (spec §4.3). Pure helpers over the state: each returns the new state and,
/// where something visible happened, adds events.
/// </summary>
public static class Primitives
{
    /// <summary>
    /// Rolls a scenario action's check at once, with the trait's value plus carried items' and lent dice (the lent dice are
    /// used up). Scenario checks resolve inside the action rather than as a separate roll wait: the roll is ceremony
    /// (spec §2.2) and one command per action keeps scenarios simple.
    /// </summary>
    public static (GameState State, int Total) Check(RulesPack pack, GameState state, SeatId seat, Trait trait, List<GameEvent> events)
    {
        var crew = state.CrewOf(seat);
        var extra = crew.Items.Sum(id => pack.Card(id).Item?.Passive.OfType<ExtraDice>().Where(e => e.Trait == trait).Sum(e => e.Dice) ?? 0);
        var dice = Rules.Value(pack, crew, trait) + extra + crew.Lent;
        var (roll, rng) = SignalDice.Roll(state.Rng, dice, pack.Rules);
        state = state.WithCrew(crew with { Lent = 0 }) with { Rng = rng };
        state = state.CountFor(seat, s => s with { Dice = s.Dice + dice });
        events.Add(new Rolled(seat, RollReason.Check, trait, roll));
        return (state, roll.Total);
    }

    /// <summary>Opens a secret vote among <paramref name="voters"/>, choosing among <paramref name="options"/>.</summary>
    public static GameState CallVote(GameState state, SeatId by, string purpose, EqArray<SeatId> voters, EqArray<string> options,
                                     List<GameEvent> events)
    {
        events.Add(new VoteCalled(by, purpose, voters));
        return state with { Waiting = new VotePending(voters[0], purpose, voters, options) };
    }

    /// <summary>Living crew still aboard: the usual voters, targets and "everyone dead" check.</summary>
    public static IEnumerable<CrewState> Aboard(GameState state) => state.Crew.Where(c => c.Aboard);

    /// <summary>The first placed compartment of any of these tiles, in the order given; null if none is placed.</summary>
    public static CompartmentId? FirstOf(GameState state, params string[] tiles)
    {
        foreach (var tile in tiles)
        {
            if (state.Ship.Compartments.FirstOrDefault(c => c.Tile.Value == tile) is { } found)
            {
                return found.Id;
            }
        }
        return null;
    }

    /// <summary>Picks <paramref name="count"/> distinct items with the state's generator.</summary>
    public static (EqArray<T> Picked, GameState State) Pick<T>(GameState state, IReadOnlyList<T> from, int count)
    {
        var (shuffled, rng) = state.Rng.Shuffle(from.ToEqArray());
        return (shuffled.Items.Take(count).ToEqArray(), state with { Rng = rng });
    }
}
