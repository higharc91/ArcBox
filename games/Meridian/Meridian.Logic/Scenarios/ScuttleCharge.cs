namespace Meridian.Logic;

/// <summary>
/// Scuttle Charge (hidden traitor; M7 plan's scenario table). One random seat was paid to destroy the ship. Alone in a
/// compartment, the saboteur plants a charge that only they can see. Nothing public happens then: the count of armed
/// charges, never their places, updates at the round's end, so turn order can't name the planter (M7 review focus 2).
/// The crew search compartments and disarm what they find. Enough charges armed at a round's end scuttle the ship; the
/// crew win by lasting to the round limit, or if the saboteur dies.
/// </summary>
public sealed class ScuttleCharge : IScenario
{
    public const string Id = "scuttle_charge";
    public const string Crew = "crew";
    public const string Saboteur = "saboteur";
    public const string Charge = "charge";

    public ScenarioInfo Info { get; } = new(Id, "hidden-traitor", 3, 6);

    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => true;

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        (var index, var rng) = state.Rng.Below(state.Crew.Count);
        state = state with { Rng = rng };
        var saboteur = state.Crew[index].Seat;
        var roles = state.Crew.Select(c => c.Seat == saboteur
            ? new RoleCard(c.Seat, Saboteur, Saboteur, Audience.OfSeat(c.Seat))
            : new RoleCard(c.Seat, Crew, Crew, Audience.OfSeat(c.Seat))).ToEqArray();
        var objectives = state.Crew.Select(c => new Objective($"goal-{c.Seat}",
            def.Role(c.Seat == saboteur ? Saboteur : Crew).Objectives[0], ObjectiveState.Open, Audience.OfSeat(c.Seat))).ToEqArray();
        return state.WithBoard(new ScenarioBoard(Id, roles, [],
            [new Counter("kit", Needed(pack, state) + def.Number("extraCharges"), Audience.OfSeat(saboteur)),
             new Counter("armed", 0, Audience.Everyone), new Counter("disarmed", 0, Audience.Everyone),
             new Counter("scuttled", 0, Audience.Everyone)],
            objectives, def.RoundLimit, state.Round));
    }

    private static SeatId SaboteurOf(GameState state) => state.Board!.Roles.First(r => r.Role == Saboteur).Seat;
    private static IEnumerable<Token> Charges(GameState state) => state.Board!.Tokens.Where(t => t.Kind == Charge);

    /// <summary>Armed charges it takes to scuttle the ship, and more with five or more aboard (content numbers).</summary>
    public static int Needed(RulesPack pack, GameState state)
        => pack.ScenarioDef(Id).Number("needed") + (state.Crew.Count >= 5 ? pack.ScenarioDef(Id).Number("extraAtFive") : 0);

    /// <summary>At each round's end the armed count goes public, enough charges scuttle the ship, and old search marks go.</summary>
    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is not RoundClosed)
        {
            return state;
        }
        var armed = Charges(state).Count();
        // Search marks only matter for three rounds; older ones go, which keeps the TV's view inside its budget.
        var board = state.Board! with
        {
            Counters = state.Board!.Counters.Where(c => !c.Id.StartsWith("searched-", StringComparison.Ordinal) || state.Round - c.Value < 3).ToEqArray(),
        };
        if (armed != board.Value("armed"))
        {
            events.Add(new ScenarioNotice(armed == 1 ? "One charge is armed somewhere aboard." : $"{armed} charges are armed somewhere aboard."));
        }
        board = board.WithCounter("armed", armed);
        return state.WithBoard(armed >= Needed(pack, state) ? board.WithCounter("scuttled", 1) : board);
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        if (!me.Aboard)
        {
            yield break;
        }
        // Everyone can search and disarm, the saboteur included: the actions on their phone give nothing away.
        var here = Charges(state).Where(c => c.At == me.At).ToList();
        yield return new ScenarioAction("search", "Search the compartment");
        if (here.Any(c => c.Audience == Audience.Everyone))
        {
            yield return new ScenarioAction("disarm", "Disarm the charge");
        }
        if (state.RoleOf(seat)?.Role == Saboteur && here.Count == 0 && state.Board!.Value("kit") > 0 && Primitives.Alone(state, seat))
        {
            yield return new ScenarioAction("plant", "Plant a charge, quietly");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var def = pack.ScenarioDef(Id);
        var at = state.CrewOf(act.From).At;
        var board = state.Board!;
        switch (act.Action)
        {
            case "plant":
                // No event at all: nothing public may change on the turn a charge is planted.
                return state.WithBoard(board.WithCounter("kit", board.Value("kit") - 1)
                    .WithToken(new Token(Primitives.NextId(board, Charge), Charge, at, Audience.OfSeat(act.From))));
            case "search":
            {
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Nerve, events);
                next = next.WithBoard(next.Board!.WithCounter($"searched-{at}", next.Round, Audience.Everyone));
                // A search sweeps the compartment and, with reach, its neighbours.
                var swept = Primitives.Steps(next, at).Where(x => x.Value <= def.Number("searchReach")).Select(x => x.Key).ToHashSet();
                var hidden = Charges(next).Where(c => swept.Contains(c.At!.Value) && c.Audience != Audience.Everyone).ToList();
                if (total < def.Number("search") || hidden.Count == 0)
                {
                    events.Add(new ScenarioActed(act.From, act.Action, null, "Nothing under the plates. Nothing you could see, anyway."));
                    return next;
                }
                foreach (var found in hidden)
                {
                    events.Add(new ScenarioNotice($"A charge, wired under the deck plates of the {pack.Tile(next.Ship.Get(found.At!.Value).Tile).Name}.",
                                                  found.At, Fx: "sparks"));
                }
                return hidden.Aggregate(next, (s, c) => Primitives.Reveal(s, c.Id));
            }
            case "disarm":
            {
                var charge = Charges(state).First(c => c.At == at && c.Audience == Audience.Everyone);
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
                if (total < def.Number("disarm"))
                {
                    events.Add(new ScenarioActed(act.From, act.Action, null, "The wires won't come apart cleanly. Not yet."));
                    return next;
                }
                events.Add(new ScenarioActed(act.From, act.Action, null, "The charge goes dead in your hands."));
                return next.WithBoard(next.Board!.WithoutToken(charge.Id).WithCounter("disarmed", next.Board.Value("disarmed") + 1));
            }
            default:
                return state;
        }
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var board = state.Board!;
        var saboteur = SaboteurOf(state);
        var crew = board.Roles.Where(r => r.Role == Crew).Select(r => r.Seat).ToEqArray();
        if (board.Value("scuttled") > 0)
        {
            return new Outcome([saboteur], "The charges go off together. The Meridian breaks in half, as someone was paid to see.");
        }
        if (!state.CrewOf(saboteur).Alive)
        {
            return new Outcome(crew, "The saboteur is dead. Whatever they planted, nobody is left to wire it.");
        }
        return crew.All(s => !state.CrewOf(s).Aboard)
            ? new Outcome([saboteur], "Nobody is left to stop the charges.")
            : null;
    }

    public Outcome AtLimit(RulesPack pack, GameState state)
        => new(state.Board!.Roles.Where(r => r.Role == Crew).Select(r => r.Seat).ToEqArray(),
               "Time ran out for the saboteur. The Meridian holds together.");

    /// <summary>The nearest compartment nobody has searched in the last few rounds, and with no charge already found.</summary>
    private static CompartmentId? Unsearched(BotContext ctx)
    {
        var found = Charges(ctx.State).Select(c => c.At).ToHashSet();
        return BotMoves.Nearest(ctx, ctx.State.Ship.Compartments.Select(c => c.Id)
            .Where(c => !found.Contains(c) && (ctx.Board.CounterOf($"searched-{c}") is not { } s || ctx.State.Round - s.Value >= 3)));
    }

    public IBotPolicy Bot(string role) => role switch
    {
        // Disarm what's found and search where nobody has looked lately, but never wander alone: whoever is alone is who
        // can plant, so a crew member on their own rejoins the nearest of the others, as people at a table would.
        Crew => new PolicyBot(ctx =>
        {
            var found = Charges(ctx.State).Where(c => c.Audience == Audience.Everyone).Select(c => c.At!.Value).ToList();
            var here = ctx.Board.CounterOf($"searched-{ctx.Me.At}") is not { } s || ctx.State.Round - s.Value >= 3;
            var others = ctx.State.Crew.Where(c => c.Aboard && c.Seat != ctx.Seat).Select(c => c.At).ToList();
            return BotMoves.Act(ctx, "disarm")
                   ?? (found.Count > 0 ? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, found)) : null)
                   ?? (here ? BotMoves.Act(ctx, "search") : null)
                   ?? (Primitives.Alone(ctx.State, ctx.Seat) && others.Count > 0 ? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, others)) : null)
                   ?? BotMoves.Toward(ctx, Unsearched(ctx));
        }),
        // Plays the crew, planting only when the rules say nobody saw it (spec §6), and never searching its own charges.
        Saboteur => new PolicyBot(ctx =>
        {
            var mine = Charges(ctx.State).Where(c => c.Audience != Audience.Everyone).Select(c => c.At).ToHashSet();
            var here = !mine.Contains(ctx.Me.At) && (ctx.Board.CounterOf($"searched-{ctx.Me.At}") is not { } s || ctx.State.Round - s.Value >= 3);
            return BotMoves.Act(ctx, "plant")
                   ?? (here ? BotMoves.Act(ctx, "search") : null)
                   ?? BotMoves.Toward(ctx, Unsearched(ctx));
        }),
        _ => throw new ArgumentException($"Scuttle Charge deals no role {role}.", nameof(role)),
    };
}
