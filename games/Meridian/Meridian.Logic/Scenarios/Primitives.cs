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

    // ---- Extracted from the S1 scenarios (M7 plan, Task 2) ----

    /// <summary>Whether a seat aboard shares its compartment with no other living crew member.</summary>
    public static bool Alone(GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        return !state.Crew.Any(c => c.Seat != seat && c.Aboard && c.At == me.At);
    }

    /// <summary>
    /// Vents a compartment (Tiller Says No's hazard, and others'): each crew member aboard there makes an Agility check
    /// against <paramref name="save"/> or loses a step from their higher physical trait. A death is dealt by the rules after.
    /// The caller says why, with a notice.
    /// </summary>
    public static GameState Vent(RulesPack pack, GameState state, CompartmentId at, int save, List<GameEvent> events)
    {
        foreach (var crew in Aboard(state).Where(c => c.At == at).ToList())
        {
            (state, var total) = Check(pack, state, crew.Seat, Trait.Agility, events);
            if (total < save)
            {
                state = LoseStep(state, crew.Seat, physical: true, events);
            }
        }
        return state;
    }

    /// <summary>A seat loses a step from its higher physical (or mental) trait, as a hazard deals damage.</summary>
    public static GameState LoseStep(GameState state, SeatId seat, bool physical, List<GameEvent> events)
    {
        var me = state.CrewOf(seat);
        var (a, b) = physical ? (Trait.Brawn, Trait.Agility) : (Trait.Tech, Trait.Nerve);
        var trait = me.Steps[a] >= me.Steps[b] ? a : b;
        events.Add(new TraitChanged(seat, trait, me.Steps[trait], me.Steps[trait] - 1));
        return state.WithCrew(me with { Steps = me.Steps.With(trait, me.Steps[trait] - 1) });
    }

    /// <summary>The next free id for a numbered token kind: <c>brood-3</c> after <c>brood-1</c> and <c>brood-2</c>.</summary>
    public static string NextId(ScenarioBoard board, string prefix)
        => $"{prefix}-{board.Tokens.Where(t => t.Id.StartsWith($"{prefix}-", StringComparison.Ordinal))
                                     .Select(t => int.TryParse(t.Id[(prefix.Length + 1)..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1}";

    /// <summary>
    /// Spawns a monster of <paramref name="kind"/> at <paramref name="at"/>, commanded by <paramref name="controller"/>,
    /// numbered after the ones already on the board.
    /// </summary>
    public static GameState Spawn(GameState state, string kind, CompartmentId at, SeatId controller, MonsterStats stats)
        => state.WithBoard(state.Board!.WithToken(new Token(NextId(state.Board!, kind), kind, at, Audience.Everyone,
                                                            Controller: controller, Stats: stats)));

    /// <summary>
    /// The rules for a carried token (Salvage Rights' Prize, and others'), for a scenario's <c>On</c>: the carrier's
    /// movement is cut by <paramref name="slow"/> (never below 1); a dead carrier drops it where they fell; and, when
    /// <paramref name="takenText"/> is given, a seat that hits the carrier in combat takes it, with that text. An escaped
    /// carrier keeps it: it left the ship with them. Never lost (M7 review focus 5).
    /// </summary>
    public static GameState Carried(GameState state, string tokenId, int slow, string? takenText, GameEvent e, List<GameEvent> events)
    {
        var token = state.Board!.Tokens.First(t => t.Id == tokenId);
        switch (e)
        {
            case TurnStarted t when token.Carrier == t.Seat && state.CrewOf(t.Seat).Aboard && slow > 0:
                return state with { MovesLeft = Math.Max(1, state.MovesLeft - slow) };
            case Attacked { Result: "hit" } a when takenText is not null && token.Carrier?.Value == a.Defender
                                                   && state.IndexOf(new SeatId(a.Attacker)) >= 0:
                events.Add(new ScenarioActed(new SeatId(a.Attacker), "steal", null, takenText));
                return state.WithBoard(state.Board!.WithToken(token with { Carrier = new SeatId(a.Attacker) }));
            case Died d when token.Carrier == d.Seat:
                return state.WithBoard(state.Board!.WithToken(token with { Carrier = null, At = state.CrewOf(d.Seat).At }));
            default:
                return state;
        }
    }

    // ---- New for M7 ----

    /// <summary>
    /// The open hatch from <paramref name="at"/> on <paramref name="side"/> to a placed neighbour, if there is one: the
    /// only kind a scenario may seal. A hatch facing a wall or an unexplored cell is not a scenario's to close.
    /// </summary>
    public static Compartment? Through(GameState state, CompartmentId at, Side side)
    {
        var here = state.Ship.Get(at);
        return here.Hatches.Contains(side) && state.Ship.At(here.At.Step(side)) is { } next && next.Hatches.Contains(side.Opposite())
            ? next : null;
    }

    /// <summary>
    /// Seals the hatch between <paramref name="at"/> and its neighbour on <paramref name="side"/>: nobody, crew or
    /// monster, passes it either way until it's unsealed. Both sides are sealed, so it reads the same from either room.
    /// </summary>
    public static GameState Seal(GameState state, CompartmentId at, Side side, List<GameEvent> events)
    {
        var next = Through(state, at, side) ?? throw new InvalidOperationException($"{at} has no open hatch on {side}.");
        if (state.Ship.IsSealed(at, side))
        {
            return state;
        }
        var sealedHatches = state.Ship.Sealed.Items.Add(new HatchRef(at, side)).Add(new HatchRef(next.Id, side.Opposite())).Sort();
        events.Add(new HatchSealed(at, side, Locked: true));
        return state with { Ship = state.Ship with { Sealed = new EqArray<HatchRef>(sealedHatches) } };
    }

    /// <summary>Opens a hatch a scenario sealed, on both sides. Hatches facing walls are never unsealed.</summary>
    public static GameState Unseal(GameState state, CompartmentId at, Side side, List<GameEvent> events)
    {
        if (Through(state, at, side) is not { } next || !state.Ship.IsSealed(at, side))
        {
            return state;
        }
        var sealedHatches = state.Ship.Sealed.Items.Remove(new HatchRef(at, side)).Remove(new HatchRef(next.Id, side.Opposite()));
        events.Add(new HatchUnsealed(at, side));
        return state with { Ship = state.Ship with { Sealed = new EqArray<HatchRef>(sealedHatches) } };
    }

    /// <summary>The sealed hatches out of a compartment that lead to a placed neighbour: those a scenario sealed.</summary>
    public static IEnumerable<Side> SealedExits(GameState state, CompartmentId at)
        => Sides.All.Where(side => Through(state, at, side) is not null && state.Ship.IsSealed(at, side));

    /// <summary>
    /// How many compartments away everything is from <paramref name="from"/>, through open hatches and links, counting
    /// steps rather than movement cost.
    /// </summary>
    public static IReadOnlyDictionary<CompartmentId, int> Steps(GameState state, CompartmentId from)
    {
        var steps = new Dictionary<CompartmentId, int> { [from] = 0 };
        var queue = new Queue<CompartmentId>([from]);
        while (queue.TryDequeue(out var at))
        {
            foreach (var next in state.Ship.Adjacent(state.Ship.Get(at)).Select(c => c.Id).Concat(state.Ship.Linked(at)))
            {
                if (steps.TryAdd(next, steps[at] + 1))
                {
                    queue.Enqueue(next);
                }
            }
        }
        return steps;
    }

    /// <summary>
    /// Spreads a hazard from each compartment of <paramref name="from"/>, in id order, to one random neighbour through an
    /// open hatch that isn't already covered. Returns where it spread (never the same compartment twice) and the state
    /// with its generator advanced.
    /// </summary>
    public static (EqArray<CompartmentId> Spread, GameState State) Spread(GameState state, IReadOnlyCollection<CompartmentId> from)
    {
        var covered = from.ToHashSet();
        var spread = new List<CompartmentId>();
        foreach (var at in from.Order())
        {
            var options = state.Ship.Adjacent(state.Ship.Get(at)).Select(c => c.Id).Where(c => !covered.Contains(c)).Order().ToList();
            if (options.Count == 0)
            {
                continue;
            }
            (var pick, var rng) = state.Rng.Below(options.Count);
            state = state with { Rng = rng };
            covered.Add(options[pick]);
            spread.Add(options[pick]);
        }
        return (spread.ToEqArray(), state);
    }

    /// <summary>Makes a hidden token public: everyone sees it from now on. The caller says how it was found, with a notice.</summary>
    public static GameState Reveal(GameState state, string tokenId)
    {
        var token = state.Board!.Tokens.First(t => t.Id == tokenId);
        return state.WithBoard(state.Board.WithToken(token with { Audience = Audience.Everyone }));
    }
}
