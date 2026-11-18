namespace Meridian.Logic;

/// <summary>
/// Captain on Deck (revealed traitor; M7 plan's scenario table). The crew member with the lowest Nerve speaks for the dead
/// captain, and spends Authority to seal hatches and vent compartments. The crew pull three command cores from scattered
/// compartments, cutting through seals where they must; the captain holds the ship until the round limit.
/// </summary>
public sealed class CaptainOnDeck : IScenario
{
    public const string Id = "captain_on_deck";
    public const string Captain = "captain";
    public const string Crew = "crew";
    public const string Core = "command_core";

    public ScenarioInfo Info { get; } = new(Id, "revealed-traitor", 3, 6);

    /// <summary>The cores need three compartments off the start layout, at least two steps apart.</summary>
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => Places(pack, state, state.Rng, 3).Places.Count == 3;

    /// <summary>
    /// <paramref name="count"/> compartments off the start layout, each at least two steps from the others, picked in a
    /// random order (fewer if the ship can't hold them).
    /// </summary>
    private static (List<CompartmentId> Places, Rng Rng) Places(RulesPack pack, GameState state, Rng rng, int count)
    {
        var (shuffled, next) = rng.Shuffle(state.Ship.Compartments.Skip(pack.Start.Place.Count).Select(c => c.Id).ToEqArray());
        var picked = new List<CompartmentId>();
        foreach (var at in shuffled)
        {
            var steps = Primitives.Steps(state, at);
            if (picked.All(p => !steps.TryGetValue(p, out var s) || s >= 2))
            {
                picked.Add(at);
                if (picked.Count == count)
                {
                    break;
                }
            }
        }
        return (picked, next);
    }

    /// <summary>The lowest Nerve; ties go to the drawer, then to whoever comes first in turn order after them.</summary>
    private static SeatId Chosen(RulesPack pack, GameState state, SeatId drawer)
    {
        var from = state.IndexOf(drawer);
        return Enumerable.Range(0, state.Crew.Count).Select(i => state.Crew[(from + i) % state.Crew.Count])
            .OrderBy(c => Rules.Value(pack, c, Trait.Nerve)).First().Seat;
    }

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        var captain = Chosen(pack, state, breach.Seat);
        var (places, rng) = Places(pack, state, state.Rng, def.Number("cores"));
        state = state with { Rng = rng };
        var roles = state.Crew.Select(c => c.Seat == captain
            ? new RoleCard(c.Seat, Captain, Captain, Audience.Everyone)
            : new RoleCard(c.Seat, Crew, Crew, Audience.Everyone)).ToEqArray();
        return state.WithBoard(new ScenarioBoard(Id, roles,
            places.Select((at, i) => new Token($"{Core}-{i + 1}", Core, at, Audience.Everyone)).ToEqArray(),
            [new Counter("authority", def.Number("authority"), Audience.Everyone), new Counter("pulled", 0, Audience.Everyone)],
            [new Objective(Captain, def.Role(Captain).Objectives[0], ObjectiveState.Open, Audience.OfSeat(captain)),
             new Objective(Crew, def.Role(Crew).Objectives[0], ObjectiveState.Open, Audience.OfSide(Crew))],
            def.RoundLimit, state.Round));
    }

    private static SeatId CaptainOf(GameState state) => state.Board!.Roles.First(r => r.Role == Captain).Seat;

    private static IEnumerable<Token> Cores(GameState state) => state.Board!.Tokens.Where(t => t.Kind == Core);

    /// <summary>Authority grows by one at each round's end, up to its cap.</summary>
    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is not RoundClosed)
        {
            return state;
        }
        var board = state.Board!;
        return state.WithBoard(board.WithCounter("authority", Math.Min(pack.ScenarioDef(Id).Number("maxAuthority"), board.Value("authority") + 1)));
    }

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        var me = state.CrewOf(seat);
        if (!me.Aboard)
        {
            yield break;
        }
        var def = pack.ScenarioDef(Id);
        if (state.RoleOf(seat)?.Role == Captain)
        {
            var authority = state.Board!.Value("authority");
            if (authority >= def.Number("sealCost"))
            {
                foreach (var side in Sides.All.Where(s => Primitives.Through(state, me.At, s) is not null && !state.Ship.IsSealed(me.At, s)))
                {
                    yield return new ScenarioAction("seal", $"Seal the {Name(side)} hatch", side.ToString());
                }
            }
            if (authority >= def.Number("ventCost"))
            {
                foreach (var room in state.Ship.Adjacent(state.Ship.Get(me.At)).Select(c => c.Id).Prepend(me.At))
                {
                    yield return new ScenarioAction("vent", $"Vent the {pack.Tile(state.Ship.Get(room).Tile).Name}", room.Value);
                }
            }
            yield break;
        }
        if (Cores(state).Any(c => c.At == me.At))
        {
            yield return new ScenarioAction("pull", "Pull the command core");
        }
        foreach (var side in Primitives.SealedExits(state, me.At))
        {
            yield return new ScenarioAction("cut", $"Cut through the {Name(side)} hatch", side.ToString());
        }
    }

    private static string Name(Side side) => side switch
    {
        Side.N => "north",
        Side.E => "east",
        Side.S => "south",
        _ => "west",
    };

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var def = pack.ScenarioDef(Id);
        var me = state.CrewOf(act.From);
        var board = state.Board!;
        switch (act.Action)
        {
            case "seal":
                state = state.WithBoard(board.WithCounter("authority", board.Value("authority") - def.Number("sealCost")));
                events.Add(new ScenarioActed(act.From, act.Action, act.Data, "By the captain's order, the hatch is sealed."));
                return Primitives.Seal(state, me.At, Enum.Parse<Side>(act.Data!), events);
            case "vent":
            {
                var room = new CompartmentId(act.Data!);
                state = state.WithBoard(board.WithCounter("authority", board.Value("authority") - def.Number("ventCost")));
                events.Add(new ScenarioNotice($"By the captain's order, the {pack.Tile(state.Ship.Get(room).Tile).Name} is vented.", room, Fx: "vent"));
                return Primitives.Vent(pack, state, room, def.Number("ventSave"), events);
            }
            case "pull":
            {
                var core = Cores(state).First(c => c.At == me.At);
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Brawn, events);
                if (total < def.Number("pull"))
                {
                    events.Add(new ScenarioActed(act.From, act.Action, null, "The core won't budge."));
                    return next;
                }
                events.Add(new ScenarioActed(act.From, act.Action, null, "A command core tears free of its housing."));
                return next.WithBoard(next.Board!.WithoutToken(core.Id).WithCounter("pulled", next.Board.Value("pulled") + 1));
            }
            case "cut":
            {
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Brawn, events);
                if (total < def.Number("cut"))
                {
                    events.Add(new ScenarioActed(act.From, act.Action, act.Data, "The seal holds."));
                    return next;
                }
                events.Add(new ScenarioActed(act.From, act.Action, act.Data, "The seal gives."));
                return Primitives.Unseal(next, me.At, Enum.Parse<Side>(act.Data!), events);
            }
            default:
                return state;
        }
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var board = state.Board!;
        var crew = board.Roles.Where(r => r.Role == Crew).Select(r => r.Seat).ToEqArray();
        if (board.Value("pulled") >= pack.ScenarioDef(Id).Number("cores"))
        {
            return new Outcome(crew, "The last core comes free. The captain's voice stops mid-order.");
        }
        return crew.All(s => !state.CrewOf(s).Aboard)
            ? new Outcome([CaptainOf(state)], "The captain stands alone on deck. The Meridian obeys.")
            : null;
    }

    public Outcome AtLimit(RulesPack pack, GameState state)
        => new([CaptainOf(state)], "Time ran out. The captain held the ship.");

    /// <summary>A scenario action this seat may take now, chosen by <paramref name="best"/> among those named <paramref name="action"/>.</summary>
    private static Command? Pick(BotContext ctx, string action, Func<ScenarioAct, int> best)
        => ctx.Legal.OfType<ScenarioAct>().Where(a => a.Action == action).OrderByDescending(best).ThenBy(a => a.Data).FirstOrDefault();

    public IBotPolicy Bot(string role) => role switch
    {
        Captain => new PolicyBot(ctx =>
        {
            var crew = ctx.State.Crew.Where(c => c.Aboard && c.Seat != ctx.Seat).ToList();
            // Vent where two or more of them stand; else seal the hatch toward the nearest of them; else go to them.
            var vent = ctx.Legal.OfType<ScenarioAct>().Where(a => a.Action == "vent")
                .Select(a => (Act: a, Count: crew.Count(c => c.At.Value == a.Data))).Where(x => x.Count >= 2)
                .OrderByDescending(x => x.Count).ThenBy(x => x.Act.Data).Select(x => (Command?)x.Act).FirstOrDefault();
            if (vent is not null)
            {
                return vent;
            }
            // Seal the hatch with crew closest beyond it, if they're within two steps of it.
            var at = crew.Select(c => c.At).ToHashSet();
            var seal = ctx.Legal.OfType<ScenarioAct>().Where(a => a.Action == "seal")
                .Select(a => (Act: a, Away: Primitives.Steps(ctx.State, Primitives.Through(ctx.State, ctx.Me.At, Enum.Parse<Side>(a.Data!))!.Id)
                                                .Where(x => at.Contains(x.Key)).Select(x => x.Value).DefaultIfEmpty(99).Min()))
                .Where(x => x.Away <= 2).OrderBy(x => x.Away).ThenBy(x => x.Act.Data).Select(x => (Command?)x.Act).FirstOrDefault();
            if (seal is not null)
            {
                return seal;
            }
            return BotMoves.Attack(ctx, t => crew.Any(c => c.Seat.Value == t))
                   ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, Cores(ctx.State).Select(c => c.At!.Value)));
        }),
        Crew => new PolicyBot(ctx =>
        {
            var cores = Cores(ctx.State).Select(c => c.At!.Value).ToList();
            // Pull here, else walk to the nearest core; sealed in or sealed off, cut through.
            return BotMoves.Act(ctx, "pull")
                   ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, cores))
                   ?? Pick(ctx, "cut", a => 0);
        }),
        _ => throw new ArgumentException($"Captain on Deck deals no role {role}.", nameof(role)),
    };
}
