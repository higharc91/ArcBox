namespace Meridian.Logic;

/// <summary>
/// Decaying Orbit (timed escape, and the fallback; M3 plan's scenario table). The orbit drops a step each round. There are
/// escape pods for all but one; or restart the engines and everyone aboard lives. Every seat wins or loses for itself.
/// It can always run: its places fall back to start tiles the ship always has.
/// </summary>
public sealed class DecayingOrbit : IScenario
{
    public const string Id = ScenarioRegistry.Fallback;
    public const string Survivor = "survivor";

    public ScenarioInfo Info { get; } = new(Id, "timed-escape", 3, 6);

    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => true;

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var pods = Primitives.FirstOf(state, "escape_pod_bay", "airlock")!.Value;
        var engine = Primitives.FirstOf(state, "engine_room", "engineering_landing")!.Value;
        return state.WithBoard(new ScenarioBoard(Id,
            state.Crew.Select(c => new RoleCard(c.Seat, Survivor, c.Seat.Value, Audience.Everyone)).ToEqArray(),
            [new Token("pods", "pods", pods, Audience.Everyone), new Token("engine", "engine", engine, Audience.Everyone)],
            [new Counter("orbit", def.Number("orbit"), Audience.Everyone), new Counter("pods", state.Crew.Count - 1, Audience.Everyone),
             new Counter("engines", 0, Audience.Everyone)],
            [new Objective(Survivor, def.Role(Survivor).Objectives[0], ObjectiveState.Open, Audience.Everyone)],
            def.RoundLimit, state.Round));
    }

    private static CompartmentId Place(GameState state, string id) => state.Board!.Tokens.First(t => t.Id == id).At!.Value;

    /// <summary>The orbit drops each round. At zero, everyone still aboard is lost; the rules deal the deaths.</summary>
    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is not RoundClosed)
        {
            return state;
        }
        var orbit = state.Board!.Value("orbit") - 1;
        state = state.WithBoard(state.Board.WithCounter("orbit", orbit));
        if (orbit > 0)
        {
            events.Add(new ScenarioNotice($"The hull groans. {orbit} {(orbit == 1 ? "round" : "rounds")} of orbit left."));
            return state;
        }
        events.Add(new ScenarioNotice("The Meridian meets the atmosphere."));
        foreach (var crew in Primitives.Aboard(state).ToList())
        {
            state = state.WithCrew(crew with { Steps = crew.Steps.With(Trait.Brawn, 0) });
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
        if (me.At == Place(state, "pods") && state.Board!.Value("pods") > 0)
        {
            yield return new ScenarioAction("board", "Take an escape pod");
        }
        if (me.At == Place(state, "engine"))
        {
            yield return new ScenarioAction("restart", "Restart the engines");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var board = state.Board!;
        if (act.Action == "board")
        {
            events.Add(new ScenarioActed(act.From, act.Action, null, "A pod seals and drops away."));
            var me = state.CrewOf(act.From);
            return state.WithCrew(me with { Escaped = true }).WithBoard(board.WithCounter("pods", board.Value("pods") - 1)) with
            {
                MovesLeft = state.ActiveSeat == act.From ? 0 : state.MovesLeft,
            };
        }
        var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
        var lit = total >= pack.ScenarioDef(Id).Number("restartTarget");
        events.Add(new ScenarioActed(act.From, act.Action, null, lit ? "An engine catches." : "The engine coughs and dies."));
        return lit ? next.WithBoard(next.Board!.WithCounter("engines", next.Board.Value("engines") + 1)) : next;
    }

    private static EqArray<SeatId> Escaped(GameState state) => state.Crew.Where(c => c.Escaped).Select(c => c.Seat).ToEqArray();

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var board = state.Board!;
        if (board.Value("engines") >= pack.ScenarioDef(Id).Number("enginesNeeded"))
        {
            return new Outcome(state.Crew.Where(c => c.Escaped || c.Aboard).Select(c => c.Seat).ToEqArray(),
                               "The engines roar. The Meridian climbs.");
        }
        if (board.Value("orbit") <= 0 || !Primitives.Aboard(state).Any())
        {
            return new Outcome(Escaped(state), "The Meridian burns. Only the pods got away.");
        }
        return null;
    }

    public Outcome AtLimit(RulesPack pack, GameState state) => new(Escaped(state), "The Meridian burns. Only the pods got away.");

    public IBotPolicy Bot(string role) => role == Survivor
        ? new PolicyBot(ctx =>
        {
            var board = ctx.Board;
            var hopeful = board.Value("orbit") >= 3 || board.Value("pods") == 0;
            return hopeful
                ? BotMoves.Act(ctx, "restart") ?? BotMoves.Toward(ctx, Place(ctx.State, "engine"))
                : BotMoves.Act(ctx, "board") ?? BotMoves.Toward(ctx, Place(ctx.State, "pods"));
        })
        : throw new ArgumentException($"Decaying Orbit deals no role {role}.", nameof(role));
}
