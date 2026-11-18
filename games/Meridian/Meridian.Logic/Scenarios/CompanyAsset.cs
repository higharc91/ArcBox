namespace Meridian.Logic;

/// <summary>
/// Company Asset (revealed traitor; M7 plan's scenario table). The crew member with the highest Tech was never human: a
/// corporate synthetic, commanding the ship's security drones. The crew transmit the ship's logs from the Bridge or the
/// Comms Array; a failed transmission alerts every drone toward the transmitter. The synthetic leaves no witnesses.
/// </summary>
public sealed class CompanyAsset : IScenario
{
    public const string Id = "company_asset";
    public const string Synthetic = "synthetic";
    public const string Crew = "crew";
    public const string Drone = "drone";
    private static readonly string[] Transmitters = ["bridge", "comms_array"];

    public ScenarioInfo Info { get; } = new(Id, "revealed-traitor", 3, 6);

    /// <summary>A transmitter must exist somewhere, placed or still to be found, or the crew could never win.</summary>
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach)
        => Transmitters.Any(t => state.Ship.Compartments.Any(c => c.Tile.Value == t) || state.TilePile.Any(p => p.Value == t));

    /// <summary>The highest Tech; ties go to the drawer, then to whoever comes first in turn order after them.</summary>
    private static SeatId Chosen(RulesPack pack, GameState state, SeatId drawer)
    {
        var from = state.IndexOf(drawer);
        return Enumerable.Range(0, state.Crew.Count).Select(i => state.Crew[(from + i) % state.Crew.Count])
            .OrderByDescending(c => Rules.Value(pack, c, Trait.Tech)).First().Seat;
    }

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var synthetic = Chosen(pack, state, breach.Seat);
        var roles = state.Crew.Select(c => c.Seat == synthetic
            ? new RoleCard(c.Seat, Synthetic, Synthetic, Audience.Everyone)
            : new RoleCard(c.Seat, Crew, Crew, Audience.Everyone)).ToEqArray();
        state = state.WithBoard(new ScenarioBoard(Id, roles, [],
            [new Counter("transmitted", 0, Audience.Everyone)],
            [new Objective(Synthetic, def.Role(Synthetic).Objectives[0], ObjectiveState.Open, Audience.OfSeat(synthetic)),
             new Objective(Crew, def.Role(Crew).Objectives[0], ObjectiveState.Open, Audience.OfSide(Crew))],
            def.RoundLimit, state.Round));
        var start = Math.Max(1, state.Crew.Count - def.Number("dronesFewer"));
        for (var i = 0; i < start; i++)
        {
            state = Primitives.Spawn(state, Drone, Bay(state, synthetic), synthetic, Stats(def));
        }
        return state;
    }

    private static MonsterStats Stats(ScenarioDef def) => new(def.Number("droneBrawn"), def.Number("droneAgility"), def.Number("droneNerve"));

    private static SeatId SyntheticOf(GameState state) => state.Board!.Roles.First(r => r.Role == Synthetic).Seat;

    /// <summary>Drones come out of the Drone Bay if it's placed, else wherever the synthetic stands.</summary>
    private static CompartmentId Bay(GameState state, SeatId synthetic)
        => Primitives.FirstOf(state, "drone_bay") ?? state.CrewOf(synthetic).At;

    private static IEnumerable<Token> Drones(GameState state) => state.Board!.Tokens.Where(t => t.Kind == Drone);

    /// <summary>Transmissions needed: more hands, more to send.</summary>
    public static int Needed(RulesPack pack, GameState state)
        => pack.ScenarioDef(Id).Number("transmissions") + (state.Crew.Count >= 5 ? pack.ScenarioDef(Id).Number("extraAtFive") : 0);

    /// <summary>At each round's end a new drone joins while there are fewer than one less than the players.</summary>
    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is not RoundClosed || Drones(state).Count() >= state.Crew.Count - 1)
        {
            return state;
        }
        var synthetic = SyntheticOf(state);
        events.Add(new ScenarioActed(synthetic, "deploy", null, "Another drone unfolds from its cradle."));
        return Primitives.Spawn(state, Drone, Bay(state, synthetic), synthetic, Stats(pack.ScenarioDef(Id)));
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        if (me.Aboard && state.RoleOf(seat)?.Role == Crew && Transmitters.Contains(state.Ship.Get(me.At).Tile.Value))
        {
            yield return new ScenarioAction("transmit", "Transmit the logs");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
        // A thin crew of three gets an easier line out (tuned in the M7 balance report).
        var target = pack.ScenarioDef(Id).Number("transmit") - (state.Crew.Count == 3 ? pack.ScenarioDef(Id).Number("easierAtThree") : 0);
        if (total >= target)
        {
            events.Add(new ScenarioActed(act.From, act.Action, null, "The logs go out. Somebody, somewhere, is listening."));
            return next.WithBoard(next.Board!.WithCounter("transmitted", next.Board.Value("transmitted") + 1));
        }
        events.Add(new ScenarioActed(act.From, act.Action, null, "The signal stalls. Every drone aboard turns toward it."));
        return Alert(next, next.CrewOf(act.From).At, events);
    }

    /// <summary>A failed transmission: every drone that can moves one compartment closer to the transmitter.</summary>
    private static GameState Alert(GameState state, CompartmentId at, List<GameEvent> events)
    {
        var steps = Primitives.Steps(state, at);
        foreach (var drone in Drones(state).Where(d => d.At is not null && !d.Stunned).ToList())
        {
            var from = drone.At!.Value;
            if (!steps.TryGetValue(from, out var away) || away == 0)
            {
                continue;
            }
            var closer = state.Ship.Adjacent(state.Ship.Get(from)).Select(c => c.Id).Concat(state.Ship.Linked(from))
                .Where(c => steps.TryGetValue(c, out var s) && s < away).Order().FirstOrDefault();
            if (closer.Value is not null)
            {
                events.Add(new MonsterMoved(drone.Id, from, closer));
                state = state.WithBoard(state.Board!.WithToken(drone with { At = closer }));
            }
        }
        return state;
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var board = state.Board!;
        var crew = board.Roles.Where(r => r.Role == Crew).Select(r => r.Seat).ToEqArray();
        if (board.Value("transmitted") >= Needed(pack, state))
        {
            return new Outcome(crew, "The logs are out. The company can't bury this now.");
        }
        return crew.All(s => !state.CrewOf(s).Aboard)
            ? new Outcome([SyntheticOf(state)], "No witnesses. The asset files its report.")
            : null;
    }

    public Outcome AtLimit(RulesPack pack, GameState state)
        => new([SyntheticOf(state)], "Time ran out. The logs were never sent, and the asset files its report.");

    public IBotPolicy Bot(string role) => role switch
    {
        // Drones go for whoever is nearest a transmitter; the synthetic itself fights when it can.
        Synthetic => new PolicyBot(ctx =>
            BotMoves.Attack(ctx, t => ctx.State.Crew.Any(c => c.Seat.Value == t))
            ?? BotMoves.Monsters(ctx, c => c.Seat != ctx.Seat)
            ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, ctx.State.Crew.Where(c => c.Aboard && c.Seat != ctx.Seat).Select(c => c.At)))),
        Crew => new PolicyBot(ctx =>
        {
            var transmitters = ctx.State.Ship.Compartments.Where(c => Transmitters.Contains(c.Tile.Value)).Select(c => c.Id);
            return BotMoves.Act(ctx, "transmit") ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, transmitters));
        }),
        _ => throw new ArgumentException($"Company Asset deals no role {role}.", nameof(role)),
    };
}
