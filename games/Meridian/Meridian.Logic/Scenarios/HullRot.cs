namespace Meridian.Logic;

/// <summary>
/// Hull Rot (co-op against the ship; M7 plan's scenario table). A growth spreads compartment to compartment from its
/// source, along open hatches. The crew burn it back, cut the source free once its surroundings are clean, and carry it
/// out of the Airlock. If the rot reaches the Airlock first, or time runs out, the ship is lost.
/// </summary>
public sealed class HullRot : IScenario
{
    public const string Id = "hull_rot";
    public const string Crew = "crew";
    public const string Rot = "growth";
    public const string Source = "rot_source";

    public ScenarioInfo Info { get; } = new(Id, "co-op", 3, 6);

    /// <summary>The source needs a compartment at least two steps from the Airlock.</summary>
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach)
        => Primitives.Steps(state, Airlock(state)).Values.Any(s => s >= 2);

    private static CompartmentId Airlock(GameState state) => Primitives.FirstOf(state, "airlock")!.Value;

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        // The source: a random compartment far enough from the Airlock, or the farthest there is.
        var away = Primitives.Steps(state, Airlock(state));
        var far = away.Where(x => x.Value >= def.Number("sourceDistance")).Select(x => x.Key).Order().ToList();
        if (far.Count == 0)
        {
            far = [away.OrderByDescending(x => x.Value).ThenBy(x => x.Key).First().Key];
        }
        (var picked, state) = Primitives.Pick(state, far, 1);
        var at = picked[0];
        return state.WithBoard(new ScenarioBoard(Id,
            state.Crew.Select(c => new RoleCard(c.Seat, Crew, Crew, Audience.Everyone)).ToEqArray(),
            [new Token(Source, Source, at, Audience.Everyone), new Token($"{Rot}-1", Rot, at, Audience.Everyone)],
            [new Counter("rotted", 1, Audience.Everyone), new Counter("overgrown", 0, Audience.Everyone), new Counter("jettisoned", 0, Audience.Everyone)],
            [new Objective(Crew, def.Role(Crew).Objectives[0], ObjectiveState.Open, Audience.Everyone)],
            def.RoundLimit, state.Round));
    }

    private static Token SourceToken(GameState state) => state.Board!.Tokens.First(t => t.Id == Source);
    private static IEnumerable<Token> RotTokens(GameState state) => state.Board!.Tokens.Where(t => t.Kind == Rot);
    private static HashSet<CompartmentId> Rotted(GameState state) => RotTokens(state).Select(t => t.At!.Value).ToHashSet();

    /// <summary>The source may be cut free when no rot lies within two steps of it but its own compartment's.</summary>
    private static bool CanCut(GameState state, CompartmentId at)
    {
        var rotted = Rotted(state);
        return Primitives.Steps(state, at).Any(x => x.Value is > 0 and <= 2 && rotted.Contains(x.Key)) is false;
    }

    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        var def = pack.ScenarioDef(Id);
        // The source, once cut free, slows its carrier by one and drops where they fall.
        if (state.Board!.Tokens.Any(t => t.Id == Source))   // until it's jettisoned
        {
            state = Primitives.Carried(state, Source, def.Number("slow"), null, e, events);
        }
        switch (e)
        {
            // Spores: ending a turn in rot costs a Nerve save or a mental step.
            case TurnEnded t when state.CrewOf(t.Seat).Aboard && Rotted(state).Contains(state.CrewOf(t.Seat).At):
            {
                (state, var total) = Primitives.Check(pack, state, t.Seat, Trait.Nerve, events);
                return total < def.Number("sporeSave") ? Primitives.LoseStep(state, t.Seat, physical: false, events) : state;
            }
            case RoundClosed:
                return Spread(pack, state, events);
            default:
                return state;
        }
    }

    /// <summary>
    /// The rot spreads from each rotted compartment to one neighbour through an open hatch, the ones nearest the Airlock
    /// first, at most <c>maxSpread</c> new compartments a round. Reaching the Airlock loses the ship.
    /// </summary>
    private static GameState Spread(RulesPack pack, GameState state, List<GameEvent> events)
    {
        var away = Primitives.Steps(state, Airlock(state));
        var from = Rotted(state).OrderBy(c => away.GetValueOrDefault(c, int.MaxValue)).ThenBy(c => c).ToList();
        (var spread, state) = Primitives.Spread(state, from);
        var grown = spread.OrderBy(c => away.GetValueOrDefault(c, int.MaxValue)).ThenBy(c => c).Take(pack.ScenarioDef(Id).Number("maxSpread")).ToList();
        foreach (var at in grown)
        {
            state = state.WithBoard(state.Board!.WithToken(new Token(Primitives.NextId(state.Board!, Rot), Rot, at, Audience.Everyone)));
        }
        if (grown.Count > 0)
        {
            // One notice, on the compartment the rot reached nearest the Airlock.
            var front = grown[0];
            events.Add(new ScenarioNotice($"The rot spreads into the {pack.Tile(state.Ship.Get(front).Tile).Name}.", front, Fx: "rot"));
        }
        var board = state.Board!.WithCounter("rotted", Rotted(state).Count);
        return state.WithBoard(Rotted(state).Contains(Airlock(state)) ? board.WithCounter("overgrown", 1) : board);
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        if (!me.Aboard)
        {
            yield break;
        }
        var source = SourceToken(state);
        if (Rotted(state).Contains(me.At) && source.At != me.At)
        {
            yield return new ScenarioAction("burn", "Burn the rot");
        }
        if (source.At == me.At && CanCut(state, me.At))
        {
            yield return new ScenarioAction("cut", "Cut the source free");
        }
        if (source.Carrier == seat && me.At == Airlock(state))
        {
            yield return new ScenarioAction("jettison", "Jettison the source");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var def = pack.ScenarioDef(Id);
        var at = state.CrewOf(act.From).At;
        switch (act.Action)
        {
            case "burn":
            {
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
                if (total < def.Number("burn"))
                {
                    events.Add(new ScenarioActed(act.From, act.Action, null, "The growth blackens, then pushes back."));
                    return next;
                }
                events.Add(new ScenarioActed(act.From, act.Action, null, "The rot burns away to ash."));
                var rot = RotTokens(next).First(t => t.At == at);
                return next.WithBoard(next.Board!.WithoutToken(rot.Id).WithCounter("rotted", Rotted(next).Count - 1));
            }
            case "cut":
            {
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Brawn, events);
                if (total < def.Number("cut"))
                {
                    events.Add(new ScenarioActed(act.From, act.Action, null, "The roots hold."));
                    return next;
                }
                events.Add(new ScenarioActed(act.From, act.Action, null, "The source tears loose, pulsing."));
                return next.WithBoard(next.Board!.WithToken(SourceToken(next) with { At = null, Carrier = act.From }));
            }
            case "jettison":
                events.Add(new ScenarioActed(act.From, act.Action, null, "The Airlock cycles. The source tumbles into the dark."));
                return state.WithBoard(state.Board!.WithoutToken(Source).WithCounter("jettisoned", 1));
            default:
                return state;
        }
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var board = state.Board!;
        var crew = state.Crew.Select(c => c.Seat).ToEqArray();
        if (board.Value("jettisoned") > 0)
        {
            return new Outcome(crew, "The source is gone. What's left of the rot withers by morning.");
        }
        if (board.Value("overgrown") > 0)
        {
            return new Outcome([], "The rot reaches the Airlock. The Meridian belongs to it now.");
        }
        return Primitives.Aboard(state).Any() ? null : new Outcome([], "Nobody is left to fight it.");
    }

    public Outcome AtLimit(RulesPack pack, GameState state) => new([], "Time ran out. The rot has the ship.");

    public IBotPolicy Bot(string role) => role == Crew
        ? new PolicyBot(ctx =>
        {
            var state = ctx.State;
            var source = SourceToken(state);
            if (source.Carrier == ctx.Seat)
            {
                return BotMoves.Act(ctx, "jettison") ?? BotMoves.Toward(ctx, Airlock(state));
            }
            var away = Primitives.Steps(state, Airlock(state));
            var rotted = Rotted(state);
            // Burn here; hold back rot that's close to the Airlock; else clear around the source, cut it, carry it out.
            var threat = rotted.Where(r => away.GetValueOrDefault(r, 99) <= 3).ToList();
            var nearSource = source.At is { } s
                ? Primitives.Steps(state, s).Where(x => x.Value is > 0 and <= 2 && rotted.Contains(x.Key)).Select(x => x.Key).ToList()
                : [];
            return BotMoves.Act(ctx, "burn")
                   ?? BotMoves.Act(ctx, "cut")
                   ?? (threat.Count > 0 ? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, threat)) : null)
                   ?? (nearSource.Count > 0 ? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, nearSource)) : null)
                   ?? BotMoves.Toward(ctx, source.At ?? (source.Carrier is { } c ? state.CrewOf(c).At : null));
        })
        : throw new ArgumentException($"Hull Rot deals no role {role}.", nameof(role));
}
