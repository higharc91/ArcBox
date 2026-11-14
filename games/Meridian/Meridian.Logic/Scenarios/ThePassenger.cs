namespace Meridian.Logic;

/// <summary>
/// The Passenger (revealed traitor; M3 plan's scenario table). The Anomaly's finder hosts a parasite and commands its
/// brood. The crew synthesize a counter-agent in a lab and inject the host with it.
/// </summary>
public sealed class ThePassenger : IScenario
{
    public const string Id = "the_passenger";
    public const string Host = "host";
    public const string Crew = "crew";
    private static readonly string[] Labs = ["med_bay", "hydroponics", "containment_lab"];

    public ScenarioInfo Info { get; } = new(Id, "revealed-traitor", 3, 6);

    /// <summary>A lab must exist somewhere, placed or still to be found, or the crew could never win.</summary>
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach)
        => Labs.Any(t => state.Ship.Compartments.Any(c => c.Tile.Value == t) || state.TilePile.Any(p => p.Value == t));

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var host = breach.Seat;
        var roles = state.Crew.Select(c => c.Seat == host
            ? new RoleCard(c.Seat, Host, "passenger", Audience.Everyone)
            : new RoleCard(c.Seat, Crew, Crew, Audience.Everyone)).ToEqArray();
        var at = state.CrewOf(host).At;
        var brood = Enumerable.Range(1, def.Number("startBrood")).Select(i => Brood(def, i, at, host)).ToEqArray();
        return state.WithBoard(new ScenarioBoard(Id, roles, brood,
            [new Counter("doses", 0, Audience.Everyone), new Counter("injected", 0, Audience.Everyone)],
            [new Objective(Host, def.Role(Host).Objectives[0], ObjectiveState.Open, Audience.OfSeat(host)),
             new Objective(Crew, def.Role(Crew).Objectives[0], ObjectiveState.Open, Audience.OfSide(Crew))],
            def.RoundLimit, state.Round));
    }

    private static Token Brood(ScenarioDef def, int n, CompartmentId at, SeatId host)
        => new($"brood-{n}", "brood", at, Audience.Everyone, Controller: host,
               Stats: new MonsterStats(def.Number("broodBrawn"), def.Number("broodAgility"), def.Number("broodNerve")));

    private static SeatId HostOf(GameState state) => state.Board!.Roles.First(r => r.Role == Host).Seat;

    /// <summary>At each round's end, a new brood joins the host while there are fewer brood than players.</summary>
    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is not RoundClosed)
        {
            return state;
        }
        var board = state.Board!;
        var count = board.Tokens.Count(t => t.Kind == "brood");
        if (count >= state.Crew.Count)
        {
            return state;
        }
        var host = HostOf(state);
        var next = board.Tokens.Where(t => t.Kind == "brood").Select(t => int.Parse(t.Id[6..])).DefaultIfEmpty(0).Max() + 1;
        events.Add(new ScenarioActed(host, "spawn", null, "Another of the brood crawls free."));
        return state.WithBoard(board.WithToken(Brood(pack.ScenarioDef(Id), next, state.CrewOf(host).At, host)));
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        if (!me.Aboard || state.RoleOf(seat)?.Role != Crew)
        {
            yield break;
        }
        if (Labs.Contains(state.Ship.Get(me.At).Tile.Value))
        {
            yield return new ScenarioAction("synthesize", "Synthesize a dose");
        }
        var host = state.CrewOf(HostOf(state));
        if (state.Board!.Value("doses") > 0 && host.Aboard && host.At == me.At)
        {
            yield return new ScenarioAction("inject", "Inject the host");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var def = pack.ScenarioDef(Id);
        var doses = state.Board!.Value("doses");
        switch (act.Action)
        {
            case "synthesize":
            {
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
                var made = total >= def.Number("synthesize");
                events.Add(new ScenarioActed(act.From, act.Action, null, made ? "A dose of counter-agent, ready." : "The mixture curdles."));
                return made ? next.WithBoard(next.Board!.WithCounter("doses", doses + 1)) : next;
            }
            case "inject":
            {
                var (afterCrew, mine) = Primitives.Check(pack, state, act.From, Trait.Brawn, events);
                var (after, theirs) = Primitives.Check(pack, afterCrew, HostOf(state), Trait.Brawn, events);
                var hit = mine > theirs;
                events.Add(new ScenarioActed(act.From, act.Action, null, hit ? "The needle goes in." : "The host twists away; the dose is lost."));
                var board = after.Board!.WithCounter("doses", doses - 1);
                return after.WithBoard(hit ? board.WithCounter("injected", 1) : board);
            }
            default:
                return state;
        }
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var board = state.Board!;
        var crew = board.Roles.Where(r => r.Role == Crew).Select(r => r.Seat).ToEqArray();
        if (board.Value("injected") > 0)
        {
            return new Outcome(crew, "The counter-agent took. The Passenger is gone.");
        }
        return crew.All(s => !state.CrewOf(s).Aboard)
            ? new Outcome([HostOf(state)], "Nobody is left to stop it. The Passenger flies the Meridian home.")
            : null;
    }

    public Outcome AtLimit(RulesPack pack, GameState state)
        => new([HostOf(state)], "Time ran out. The Passenger has settled in for the long voyage.");

    public IBotPolicy Bot(string role) => role switch
    {
        Host => new PolicyBot(ctx =>
            BotMoves.Attack(ctx, t => ctx.State.Crew.Any(c => c.Seat.Value == t))
            ?? BotMoves.Monsters(ctx, c => c.Seat != ctx.Seat)
            ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, ctx.State.Crew.Where(c => c.Aboard && c.Seat != ctx.Seat).Select(c => c.At)))),
        Crew => new PolicyBot(ctx =>
        {
            if (ctx.Board.Value("doses") == 0)
            {
                var labs = ctx.State.Ship.Compartments.Where(c => Labs.Contains(c.Tile.Value)).Select(c => c.Id);
                return BotMoves.Act(ctx, "synthesize") ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, labs));
            }
            return BotMoves.Act(ctx, "inject") ?? BotMoves.Toward(ctx, ctx.State.CrewOf(HostOf(ctx.State)).At);
        }),
        _ => throw new ArgumentException($"The Passenger deals no role {role}.", nameof(role)),
    };
}
