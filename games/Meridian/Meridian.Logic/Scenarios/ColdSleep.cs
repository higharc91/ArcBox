namespace Meridian.Logic;

/// <summary>
/// Cold Sleep (revealed traitor; M7 plan's scenario table). The Anomaly's finder wakes the cryo-crew. One sleeper wakes
/// each round for every power relay still feeding the Cryo Bay, and the waker sends them for the Airlock. The crew reroute
/// every relay; then any sleeper still stunned at a round's end falls back into cold sleep. With the power cut and nobody
/// left awake, the crew win. Enough sleepers in the Airlock at a round's end, and the ship is overrun.
/// </summary>
public sealed class ColdSleep : IScenario
{
    public const string Id = "cold_sleep";
    public const string Waker = "waker";
    public const string Crew = "crew";
    public const string Sleeper = "sleeper";
    public const string Relay = "relay";
    private static readonly string[] PreferredRelays = ["power_relay", "reactor_core", "engine_room"];

    public ScenarioInfo Info { get; } = new(Id, "revealed-traitor", 3, 6);

    /// <summary>The Cryo Bay must be placed or still in the pile; any Engineering compartment can hold a relay.</summary>
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach)
        => (state.Ship.Compartments.Any(c => c.Tile.Value == "cryo_bay") || state.TilePile.Any(t => t.Value == "cryo_bay"))
           && state.Ship.Compartments.Any(c => c.At.Deck == DeckId.Engineering);

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var waker = breach.Seat;
        // Relays: the power compartments first, then any other Engineering compartment, at random.
        var engineering = state.Ship.Compartments.Where(c => c.At.Deck == DeckId.Engineering).ToList();
        var preferred = PreferredRelays.SelectMany(t => engineering.Where(c => c.Tile.Value == t)).Select(c => c.Id).ToList();
        (var others, state) = Primitives.Pick(state, engineering.Select(c => c.Id).Except(preferred).ToList(), def.Number("relays"));
        var relays = preferred.Concat(others).Take(def.Number("relays"))
            .Select((at, i) => new Token($"{Relay}-{i + 1}", Relay, at, Audience.Everyone, State: "powered")).ToEqArray();
        var roles = state.Crew.Select(c => c.Seat == waker
            ? new RoleCard(c.Seat, Waker, Waker, Audience.Everyone)
            : new RoleCard(c.Seat, Crew, Crew, Audience.Everyone)).ToEqArray();
        state = state.WithBoard(new ScenarioBoard(Id, roles, relays,
            [new Counter("overrun", 0, Audience.Everyone), new Counter("inAirlock", 0, Audience.Everyone), new Counter("cleared", 0, Audience.Everyone)],
            [new Objective(Waker, def.Role(Waker).Objectives[0], ObjectiveState.Open, Audience.OfSeat(waker)),
             new Objective(Crew, def.Role(Crew).Objectives[0], ObjectiveState.Open, Audience.OfSide(Crew))],
            def.RoundLimit, state.Round));
        for (var i = 0; i < def.Number("startSleepers"); i++)
        {
            state = Primitives.Spawn(state, Sleeper, Cryo(state, waker), waker, Stats(def));
        }
        return state;
    }

    private static MonsterStats Stats(ScenarioDef def) => new(def.Number("sleeperBrawn"), def.Number("sleeperAgility"), def.Number("sleeperNerve"));
    private static SeatId WakerOf(GameState state) => state.Board!.Roles.First(r => r.Role == Waker).Seat;
    private static CompartmentId Cryo(GameState state, SeatId waker) => Primitives.FirstOf(state, "cryo_bay") ?? state.CrewOf(waker).At;
    private static CompartmentId Airlock(GameState state) => Primitives.FirstOf(state, "airlock")!.Value;
    private static IEnumerable<Token> Sleepers(GameState state) => state.Board!.Tokens.Where(t => t.Kind == Sleeper);
    private static IEnumerable<Token> Relays(GameState state) => state.Board!.Tokens.Where(t => t.Kind == Relay);
    private static bool PowerCut(GameState state) => Relays(state).All(r => r.State == "rerouted");

    /// <summary>Sleepers it takes to overrun the Airlock: one less than the players, at least two.</summary>
    public static int Overrun(GameState state) => Math.Max(2, state.Crew.Count - 1);

    /// <summary>
    /// At each round's end, in order: with the power cut, stunned sleepers fall back asleep; the Airlock is counted; then
    /// one sleeper wakes per powered relay, up to the cap.
    /// </summary>
    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is not RoundClosed)
        {
            return state;
        }
        var def = pack.ScenarioDef(Id);
        if (PowerCut(state))
        {
            foreach (var asleep in Sleepers(state).Where(s => s.Stunned).ToList())
            {
                state = state.WithBoard(state.Board!.WithoutToken(asleep.Id));
                events.Add(new ScenarioNotice("Without power, a sleeper sinks back into the cold.", asleep.At));
            }
        }
        var inAirlock = Sleepers(state).Count(s => s.At == Airlock(state));
        var board = state.Board!.WithCounter("inAirlock", inAirlock);
        // The crew's win is judged here, at the round's end: the power cut and nobody left awake.
        if (PowerCut(state) && !Sleepers(state).Any())
        {
            board = board.WithCounter("cleared", 1);
        }
        state = state.WithBoard(inAirlock >= Overrun(state) ? board.WithCounter("overrun", 1) : board);
        var waker = WakerOf(state);
        var waking = Math.Min(Relays(state).Count(r => r.State == "powered"), def.Number("maxAwake") - Sleepers(state).Count());
        for (var i = 0; i < waking; i++)
        {
            state = Primitives.Spawn(state, Sleeper, Cryo(state, waker), waker, Stats(def));
        }
        if (waking > 0)
        {
            events.Add(new ScenarioActed(waker, "wake", null, waking == 1 ? "Another pod hisses open." : $"{waking} more pods hiss open."));
        }
        return state;
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        if (me.Aboard && state.RoleOf(seat)?.Role == Crew && Relays(state).Any(r => r.At == me.At && r.State == "powered"))
        {
            yield return new ScenarioAction("reroute", "Reroute the relay");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var relay = Relays(state).First(r => r.At == state.CrewOf(act.From).At && r.State == "powered");
        var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
        if (total < pack.ScenarioDef(Id).Number("reroute"))
        {
            events.Add(new ScenarioActed(act.From, act.Action, null, "The relay arcs and holds its route."));
            return next;
        }
        events.Add(new ScenarioActed(act.From, act.Action, null, "Power swings away from the Cryo Bay."));
        return next.WithBoard(next.Board!.WithToken(relay with { State = "rerouted" }));
    }

    /// <summary>A completed objective beats a loss at the same moment (M7 review focus 6): the crew are checked first.</summary>
    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var board = state.Board!;
        var crew = board.Roles.Where(r => r.Role == Crew).Select(r => r.Seat).ToEqArray();
        if (board.Value("cleared") > 0)
        {
            return new Outcome(crew, "The Cryo Bay goes dark, and everyone in it sleeps again.");
        }
        if (board.Value("overrun") > 0)
        {
            return new Outcome([WakerOf(state)], "The sleepers fill the Airlock. The Meridian is theirs now.");
        }
        return crew.All(s => !state.CrewOf(s).Aboard)
            ? new Outcome([WakerOf(state)], "Nobody is left awake but the sleepers.")
            : null;
    }

    public Outcome AtLimit(RulesPack pack, GameState state)
        => new([WakerOf(state)], "Time ran out. The cryo-crew are awake for good.");

    public IBotPolicy Bot(string role) => role switch
    {
        // Sleepers fight whoever stands in their way, else march on the Airlock.
        Waker => new PolicyBot(ctx =>
            ctx.Legal.OfType<MonsterAttack>().FirstOrDefault()
            ?? BotMoves.MonstersTo(ctx, Airlock(ctx.State))
            ?? BotMoves.Toward(ctx, Airlock(ctx.State))),
        // Half the crew reroute relays and half hold the Airlock; with the power cut, everyone puts the sleepers down.
        Crew => new PolicyBot(ctx =>
        {
            var sleepers = Sleepers(ctx.State).Where(s => !s.Stunned).ToList();
            var attack = BotMoves.Attack(ctx, t => sleepers.Any(s => s.Id == t));
            var powered = Relays(ctx.State).Where(r => r.State == "powered").Select(r => r.At!.Value).ToList();
            var rerouter = ctx.State.IndexOf(ctx.Seat) % 2 == 0;
            if (powered.Count == 0 || !rerouter)
            {
                return attack ?? BotMoves.Toward(ctx, sleepers.Count > 0 ? BotMoves.Nearest(ctx, sleepers.Select(s => s.At!.Value)) : Airlock(ctx.State))
                       ?? (powered.Count > 0 ? BotMoves.Act(ctx, "reroute") ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, powered)) : null);
            }
            return BotMoves.Act(ctx, "reroute") ?? attack ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, powered));
        }),
        _ => throw new ArgumentException($"Cold Sleep deals no role {role}.", nameof(role)),
    };
}
