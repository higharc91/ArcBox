namespace Meridian.Logic;

/// <summary>
/// The Last Clean Sample (free-for-all; M7 plan's scenario table). Everyone is infected, and there is one clean sample,
/// in a lab: placed, or the first one found. Synthesize it into the single dose, then inject yourself; the dose takes at
/// the start of your next turn if it's still yours. A hit takes the dose and cancels the injection. Infection rises each
/// round, and kills at its limit. If everyone dies, nobody wins.
/// </summary>
public sealed class LastCleanSample : IScenario
{
    public const string Id = "last_clean_sample";
    public const string Infected = "infected";
    public const string Sample = "sample";
    public const string Dose = "dose";
    private static readonly string[] Labs = ["med_bay", "hydroponics", "containment_lab", "isolation_ward"];

    public ScenarioInfo Info { get; } = new(Id, "free-for-all", 3, 6);

    /// <summary>A lab must exist somewhere, placed or still to be found.</summary>
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach)
        => Labs.Any(t => state.Ship.Compartments.Any(c => c.Tile.Value == t) || state.TilePile.Any(p => p.Value == t));

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var lab = Primitives.FirstOf(state, Labs);
        return state.WithBoard(new ScenarioBoard(Id,
            state.Crew.Select(c => new RoleCard(c.Seat, Infected, c.Seat.Value, Audience.Everyone)).ToEqArray(),
            lab is { } at ? [new Token(Sample, Sample, at, Audience.Everyone)] : [],
            [.. state.Crew.Select(c => new Counter($"infection-{c.Seat}", 0, Audience.Everyone)), new Counter("cured", 0, Audience.Everyone)],
            [new Objective(Infected, def.Role(Infected).Objectives[0], ObjectiveState.Open, Audience.Everyone)],
            def.RoundLimit, state.Round));
    }

    private static Token? Find(GameState state, string id) => state.Board!.Tokens.FirstOrDefault(t => t.Id == id);

    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        var def = pack.ScenarioDef(Id);
        if (Find(state, Dose) is { } dose)
        {
            // The dose slows its carrier, passes on a hit, and drops where its carrier dies.
            state = Primitives.Carried(state, Dose, def.Number("slow"), "The dose changes hands.", e, events);
            // Taken from an injector, or dropped: the injection is off.
            if (Find(state, Dose) is { State: { } injector } now && now.Carrier?.Value != injector)
            {
                state = state.WithBoard(state.Board!.WithToken(now with { State = null }));
            }
        }
        switch (e)
        {
            // The first lab found holds the sample, if none was aboard when the scenario began.
            case CompartmentRevealed r when Labs.Contains(r.Compartment.Tile.Value) && Find(state, Sample) is null && Find(state, Dose) is null
                                            && state.Board!.Value("cured") == 0:
                events.Add(new ScenarioNotice($"The last clean sample is here, in the {pack.Tile(r.Compartment.Tile).Name}.", r.Compartment.Id));
                return state.WithBoard(state.Board!.WithToken(new Token(Sample, Sample, r.Compartment.Id, Audience.Everyone)));
            // An injection takes at the start of its carrier's next turn, if the dose is still theirs.
            // A seat the infection has just finished (a trait at 0: dead by the rules, spec §2.8) is not cured.
            case TurnStarted t when Find(state, Dose) is { } injected && injected.State == t.Seat.Value && injected.Carrier == t.Seat
                                    && Traits.All.All(trait => state.CrewOf(t.Seat).Steps[trait] >= 1):
                events.Add(new ScenarioActed(t.Seat, "cured", null, "The fever breaks. They're clean."));
                return state.WithBoard(state.Board!.WithCounter("cured", state.IndexOf(t.Seat) + 1));
            case RoundClosed:
                return Infect(pack, state, events);
            default:
                return state;
        }
    }

    /// <summary>Each round's end, every infection rises; at the limit, the rules deal the death.</summary>
    private static GameState Infect(RulesPack pack, GameState state, List<GameEvent> events)
    {
        var limit = pack.ScenarioDef(Id).Number("infectionDeath");
        foreach (var crew in Primitives.Aboard(state).ToList())
        {
            var key = $"infection-{crew.Seat}";
            var level = state.Board!.Value(key) + 1;
            state = state.WithBoard(state.Board!.WithCounter(key, level));
            if (level >= limit)
            {
                state = state.WithCrew(crew with { Steps = crew.Steps.With(Trait.Brawn, 0) });
            }
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
        if (Find(state, Sample) is { } sample && sample.At == me.At)
        {
            yield return new ScenarioAction("synthesize", "Synthesize the dose");
        }
        if (Find(state, Dose) is { } dose && dose.Carrier == seat && dose.State != seat.Value)
        {
            yield return new ScenarioAction("inject", "Inject yourself");
        }
        if (Find(state, Dose) is { At: { } at } && at == me.At)
        {
            yield return new ScenarioAction("takeDose", "Take the dose");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        switch (act.Action)
        {
            case "synthesize":
            {
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
                if (total < pack.ScenarioDef(Id).Number("synthesize"))
                {
                    events.Add(new ScenarioActed(act.From, act.Action, null, "The culture clouds. Not yet."));
                    return next;
                }
                events.Add(new ScenarioActed(act.From, act.Action, null, "One dose, clean. Everyone saw who has it."));
                return next.WithBoard(next.Board!.WithoutToken(Sample).WithToken(new Token(Dose, Dose, null, Audience.Everyone, Carrier: act.From)));
            }
            case "inject":
                events.Add(new ScenarioActed(act.From, act.Action, null, "The needle goes in. It takes a little while."));
                return state.WithBoard(state.Board!.WithToken(Find(state, Dose)! with { State = act.From.Value }));
            case "takeDose":
                events.Add(new ScenarioActed(act.From, act.Action, null, "Picked up. Everyone saw."));
                return state.WithBoard(state.Board!.WithToken(Find(state, Dose)! with { At = null, Carrier = act.From }));
            default:
                return state;
        }
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        if (state.Board!.Value("cured") is > 0 and var n)
        {
            return new Outcome([state.Crew[n - 1].Seat], "One of them is clean. The rest are not.");
        }
        return Primitives.Aboard(state).Any() ? null : new Outcome([], "The infection took everyone. The sample was never used.");
    }

    public Outcome AtLimit(RulesPack pack, GameState state) => new([], "Time ran out. Nobody got clean.");

    public IBotPolicy Bot(string role) => role == Infected
        ? new PolicyBot(ctx =>
        {
            var state = ctx.State;
            if (Find(state, Dose) is { } dose && dose.Carrier == ctx.Seat)
            {
                // Inject when nobody is close enough to take it; else get away from them.
                var others = state.Crew.Where(c => c.Aboard && c.Seat != ctx.Seat).Select(c => c.At).ToList();
                if (Primitives.Alone(state, ctx.Seat))
                {
                    var inject = BotMoves.Act(ctx, "inject");
                    if (inject is not null)
                    {
                        return inject;
                    }
                }
                var away = ctx.Legal.OfType<MoveTo>()
                    .OrderByDescending(m => others.Select(o => Primitives.Steps(state, m.To).GetValueOrDefault(o, 99)).DefaultIfEmpty(99).Min())
                    .ThenBy(m => m.To).FirstOrDefault();
                return (Command?)away ?? new EndTurn(ctx.Seat);
            }
            if (Find(state, Sample) is { At: { } sample })
            {
                return BotMoves.Act(ctx, "synthesize") ?? BotMoves.Toward(ctx, sample);
            }
            if (Find(state, Dose) is { } held)
            {
                return BotMoves.Act(ctx, "takeDose")
                       ?? BotMoves.Attack(ctx, t => held.Carrier?.Value == t)
                       ?? BotMoves.Toward(ctx, held.At ?? state.CrewOf(held.Carrier!.Value).At);
            }
            return null;   // no sample yet: explore for a lab
        })
        : throw new ArgumentException($"The Last Clean Sample deals no role {role}.", nameof(role));
}
