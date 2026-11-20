namespace Meridian.Logic;

/// <summary>
/// Vent Cycle (timed escape; M7 plan's scenario table). The decks lose pressure one at a time, Engineering, then Habitat,
/// then Command, every few rounds. There are suits for all but one. Suit up and cycle out through the Airlock to escape;
/// every seat wins or loses for itself. The suits lie scattered, far from the Airlock. A hit in combat takes the loser's suit. When Command vents, whoever is left
/// aboard is lost.
/// </summary>
public sealed class VentCycle : IScenario
{
    public const string Id = "vent_cycle";
    public const string Survivor = "survivor";
    public const string Suit = "suit";
    private static readonly DeckId[] Order = [DeckId.Engineering, DeckId.Habitat, DeckId.Command];

    public ScenarioInfo Info { get; } = new(Id, "timed-escape", 3, 6);

    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => true;

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        // Scattered, each at least a few steps from the Airlock (owner's decision on the M7 balance report): fetching a suit
        // costs turns while the decks vent. On a small ship, the farthest compartments, in turn.
        var count = state.Crew.Count - 1;
        var away = Primitives.Steps(state, Airlock(state));
        var far = away.Where(x => x.Value >= def.Number("suitDistance")).Select(x => x.Key).Order().ToList();
        (var picked, state) = Primitives.Pick(state, far, count);
        var farthest = away.OrderByDescending(x => x.Value).ThenBy(x => x.Key).Select(x => x.Key).ToList();
        var places = picked.Concat(Enumerable.Range(0, Math.Max(0, count - picked.Count)).Select(i => farthest[i % farthest.Count])).ToList();
        var suits = places.Select((at, i) => new Token($"{Suit}-{i + 1}", Suit, at, Audience.Everyone)).ToEqArray();
        return state.WithBoard(new ScenarioBoard(Id,
            state.Crew.Select(c => new RoleCard(c.Seat, Survivor, c.Seat.Value, Audience.Everyone)).ToEqArray(), suits,
            [new Counter("vented", 0, Audience.Everyone), new Counter("nextVent", def.Number("cycle"), Audience.Everyone),
             new Counter("escaped", 0, Audience.Everyone)],
            [new Objective(Survivor, def.Role(Survivor).Objectives[0], ObjectiveState.Open, Audience.Everyone)],
            def.RoundLimit, state.Round));
    }

    private static IEnumerable<Token> Suits(GameState state) => state.Board!.Tokens.Where(t => t.Kind == Suit);
    private static bool Suited(GameState state, SeatId seat) => Suits(state).Any(s => s.Carrier == seat);
    private static CompartmentId Airlock(GameState state) => Primitives.FirstOf(state, "airlock")!.Value;

    /// <summary>The decks vented so far, in venting order.</summary>
    private static IEnumerable<DeckId> Vented(GameState state) => Order.Take(state.Board!.Value("vented"));

    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        // Suits: a hit takes one, a dead wearer drops theirs, an escaped wearer keeps theirs.
        foreach (var suit in Suits(state).Where(s => s.Carrier is not null).ToList())
        {
            state = Primitives.Carried(state, suit.Id, 0, "A suit is torn away and taken.", e, events);
        }
        switch (e)
        {
            // Vacuum: ending a turn unsuited on a vented deck costs a physical step.
            case TurnEnded t when state.CrewOf(t.Seat).Aboard && !Suited(state, t.Seat)
                                  && Vented(state).Contains(state.Ship.Get(state.CrewOf(t.Seat).At).At.Deck):
                return Primitives.LoseStep(state, t.Seat, physical: true, events);
            case RoundClosed:
                return Cycle(pack, state, events);
            default:
                return state;
        }
    }

    /// <summary>Every few rounds the next deck vents. When the last one goes, everyone still aboard is lost.</summary>
    private static GameState Cycle(RulesPack pack, GameState state, List<GameEvent> events)
    {
        var board = state.Board!;
        var next = board.Value("nextVent") - 1;
        if (next > 0)
        {
            return state.WithBoard(board.WithCounter("nextVent", next));
        }
        var vented = board.Value("vented");
        var deck = Order[vented];
        state = state.WithBoard(board.WithCounter("vented", vented + 1).WithCounter("nextVent", pack.ScenarioDef(Id).Number("cycle")));
        var where = state.Ship.Compartments.First(c => c.At.Deck == deck).Id;
        events.Add(new ScenarioNotice($"The {deck} deck decompresses.", where, Fx: "vent"));
        if (vented + 1 < Order.Length)
        {
            return state;
        }
        foreach (var crew in Primitives.Aboard(state).ToList())
        {
            state = state.WithCrew(crew with { Steps = crew.Steps.With(Trait.Brawn, 0) });   // the rules deal the deaths
        }
        return state;
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        if (!me.Aboard)
        {
            yield break;
        }
        if (!Suited(state, seat) && Suits(state).Any(s => s.At == me.At))
        {
            yield return new ScenarioAction("suitUp", "Put on a suit");
        }
        if (Suited(state, seat) && me.At == Airlock(state))
        {
            yield return new ScenarioAction("cycleOut", "Cycle out through the Airlock");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var me = state.CrewOf(act.From);
        if (act.Action == "suitUp")
        {
            var suit = Suits(state).First(s => s.At == me.At);
            events.Add(new ScenarioActed(act.From, act.Action, null, "Seals check. Air flows."));
            return state.WithBoard(state.Board!.WithToken(suit with { At = null, Carrier = act.From }));
        }
        events.Add(new ScenarioActed(act.From, act.Action, null, "The Airlock cycles, and they're gone."));
        var board = state.Board!.WithCounter("escaped", state.Board.Value("escaped") + 1);
        return state.WithCrew(me with { Escaped = true }).WithBoard(board) with
        {
            MovesLeft = state.ActiveSeat == act.From ? 0 : state.MovesLeft,
        };
    }

    private static EqArray<SeatId> Escaped(GameState state) => state.Crew.Where(c => c.Escaped).Select(c => c.Seat).ToEqArray();

    public Outcome? Evaluate(RulesPack pack, GameState state)
        => state.Board!.Value("vented") >= Order.Length || !Primitives.Aboard(state).Any()
            ? new Outcome(Escaped(state), "The last deck goes. Only those who cycled out are breathing.")
            : null;

    public Outcome AtLimit(RulesPack pack, GameState state) => new(Escaped(state), "Time ran out. Only those who cycled out are breathing.");

    public IBotPolicy Bot(string role) => role == Survivor
        ? new PolicyBot(ctx =>
        {
            if (Suited(ctx.State, ctx.Seat))
            {
                return BotMoves.Act(ctx, "cycleOut") ?? BotMoves.Toward(ctx, Airlock(ctx.State));
            }
            var lying = Suits(ctx.State).Where(s => s.At is not null).Select(s => s.At!.Value).ToList();
            if (lying.Count > 0)
            {
                return BotMoves.Act(ctx, "suitUp") ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, lying));
            }
            // No suit to be had: take one from whoever wears it.
            var wearers = ctx.State.Crew.Where(c => c.Aboard && Suited(ctx.State, c.Seat)).ToList();
            return BotMoves.Attack(ctx, t => wearers.Any(w => w.Seat.Value == t))
                   ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, wearers.Select(w => w.At)));
        })
        : throw new ArgumentException($"Vent Cycle deals no role {role}.", nameof(role));
}
