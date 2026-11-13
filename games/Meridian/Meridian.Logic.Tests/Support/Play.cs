namespace Meridian.Logic.Tests;

/// <summary>
/// A game under test. <see cref="Do"/> applies a command that must be legal; <see cref="Try"/> one that may not be.
/// Tests set up exact positions with <c>game.State = game.State with { … }</c> rather than relying on a shuffle.
/// </summary>
internal sealed class Play
{
    public static readonly SeatId S1 = new("s1"), S2 = new("s2"), S3 = new("s3");

    public RulesPack Pack { get; }
    public GameState State { get; set; }
    public List<GameEvent> Events { get; } = [];

    private Play(RulesPack pack, GameState state) => (Pack, State) = (pack, state);

    /// <summary>Three seats (s1–s3, crew in pack order), with s1 to act first and nothing drawn yet.</summary>
    public static Play Game(RulesPack? pack = null, ulong seed = 1)
    {
        pack ??= TestPack.Load();
        var seats = new[] { S1, S2, S3 }.Zip(pack.Crew.Take(3), (s, c) => (s, c.Id)).ToList();
        var state = Rules.NewGame(pack, seats, seed);
        state = state with { First = 0, Active = 0, MovesLeft = Rules.Value(pack, state.Crew[0], Trait.Agility) };
        return new Play(pack, state);
    }

    public GameEvent[] Do(Command command)
    {
        var result = Rules.Step(Pack, State, command);
        Assert.False(result.Events.FirstOrDefault() is Rejected,
                     $"{command} was rejected: {(result.Events.FirstOrDefault() as Rejected)?.Reason}");
        State = result.State;
        Events.AddRange(result.Events);
        return [.. result.Events];
    }

    public StepResult Try(Command command)
    {
        var result = Rules.Step(Pack, State, command);
        State = result.State;
        Events.AddRange(result.Events);
        return result;
    }

    public EqArray<Command> Legal(SeatId seat) => Rules.Legal(Pack, State, seat);

    public CrewState Crew(SeatId seat) => State.CrewOf(seat);

    /// <summary>Puts a seat in a compartment, and gives the active seat <paramref name="moves"/> if set.</summary>
    public Play Put(SeatId seat, CompartmentId at, int? moves = null)
    {
        State = State.WithCrew(State.CrewOf(seat) with { At = at });
        if (moves is { } m)
        {
            State = State with { MovesLeft = m };
        }
        return this;
    }

    public Compartment Placed(string tile) => State.Ship.Compartments.Last(c => c.Tile == new TileId(tile));

    public static CompartmentId C(int n) => CompartmentId.Numbered(n);

    /// <summary>Opens a check on a card for a seat, as drawing it would.</summary>
    public Play Check(SeatId seat, string card, string? option = null)
    {
        var resolver = new Resolver(Pack, State);
        resolver.StartCheck(seat, new CardId(card), option);
        State = resolver.State;
        return this;
    }

    /// <summary>Sets the state's generator so the next roll of <paramref name="dice"/> dice totals as wanted.</summary>
    public Play Rig(int dice, Func<int, bool> total)
    {
        for (ulong seed = 1; seed < 10_000; seed++)
        {
            if (total(SignalDice.Roll(Rng.Seeded(seed), dice, Pack.Rules).Roll.Total))
            {
                State = State with { Rng = Rng.Seeded(seed) };
                return this;
            }
        }
        throw new InvalidOperationException("No seed gives that total.");
    }

    public Play Steps(SeatId seat, TraitSteps steps)
    {
        State = State.WithCrew(State.CrewOf(seat) with { Steps = steps });
        return this;
    }

    public Play Give(SeatId seat, params string[] cards)
    {
        var crew = State.CrewOf(seat);
        State = State.WithCrew(crew with { Items = crew.Items.AddRange(cards.Select(c => new CardId(c))) });
        return this;
    }

    public int Dice => ((RollPending)State.Waiting!).Dice;
}
