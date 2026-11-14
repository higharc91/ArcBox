namespace Meridian.Logic;

/// <summary>
/// One of Us Isn't (hidden traitor; M3 plan's scenario table). One seat is secretly the mimic. The crew repair four
/// points, or expose the mimic in one of two votes; the mimic undoes repairs while alone, and wins at the round limit.
/// Every role card is secret to its own seat, so nobody's card tells them anything about anyone else's.
/// A sabotage only shows at the end of the round: the mimic acts on its own turn, so an immediate notice would name it.
/// Until then the point looks repaired, and only a token the mimic can see says otherwise.
/// </summary>
public sealed class OneOfUsIsnt : IScenario
{
    public const string Id = "one_of_us_isnt";
    public const string Crew = "crew";
    public const string Mimic = "mimic";
    private static readonly string[] Scanners = ["sensor_suite", "command_landing"];

    public ScenarioInfo Info { get; } = new(Id, "hidden-traitor", 3, 6);

    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach)
        => state.Ship.Compartments.Count > pack.Start.Place.Count + Points(pack, state);

    /// <summary>
    /// One repair point per player, and two more for each player above four: more hands, more to repair (M3 balance
    /// report). Plus the content's extra.
    /// </summary>
    private static int Points(RulesPack pack, GameState state)
        => state.Crew.Count + 2 * Math.Max(0, state.Crew.Count - 4) + pack.ScenarioDef(Id).Number("extraPoints");

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
    {
        var def = pack.ScenarioDef(Id);
        (var mimicIndex, var rng) = state.Rng.Below(state.Crew.Count);
        state = state with { Rng = rng };
        var mimic = state.Crew[mimicIndex].Seat;
        var roles = state.Crew.Select(c => c.Seat == mimic
            ? new RoleCard(c.Seat, Mimic, Mimic, Audience.OfSeat(c.Seat))
            : new RoleCard(c.Seat, Crew, Crew, Audience.OfSeat(c.Seat))).ToEqArray();
        var places = state.Ship.Compartments.Skip(pack.Start.Place.Count).Select(c => c.Id).ToList();
        (var picked, state) = Primitives.Pick(state, places, Points(pack, state));
        var points = picked.Select((at, i) => new Token($"point-{i + 1}", "repair", at, Audience.Everyone, State: "broken")).ToEqArray();
        var objectives = state.Crew.Select(c => new Objective($"goal-{c.Seat}",
            def.Role(c.Seat == mimic ? Mimic : Crew).Objectives[0], ObjectiveState.Open, Audience.OfSeat(c.Seat))).ToEqArray();
        return state.WithBoard(new ScenarioBoard(Id, roles, points,
            [new Counter("votes", 0, Audience.Everyone), new Counter("voteRound", -1, Audience.Everyone),
             new Counter("exposed", 0, Audience.Everyone)],
            objectives, def.RoundLimit, state.Round));
    }

    private static SeatId MimicOf(GameState state) => state.Board!.Roles.First(r => r.Role == Mimic).Seat;

    private static IEnumerable<Token> Points(GameState state) => state.Board!.Tokens.Where(t => t.Kind == "repair");

    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events)
    {
        if (e is RoundClosed)
        {
            return RevealSabotage(pack, state, events);
        }
        if (e is not VoteResolved { Purpose: "mimic" } vote)
        {
            return state;
        }
        var board = state.Board!.WithCounter("votes", state.Board.Value("votes") + 1);
        var caught = vote.Result == MimicOf(state).Value;
        events.Add(new ScenarioNotice(caught ? "The scanner screams. They found it." : "The scanner shows nothing. Wrong one."));
        return state.WithBoard(caught ? board.WithCounter("exposed", 1) : board);
    }

    /// <summary>
    /// At a round's end every sabotage shows: the point is broken again, everyone is told where, and whoever is in or next
    /// to that compartment now falls under suspicion (public counters a person could keep in their head; the bots use them).
    /// </summary>
    private static GameState RevealSabotage(RulesPack pack, GameState state, List<GameEvent> events)
    {
        foreach (var sabotage in state.Board!.Tokens.Where(t => t.Kind == "sabotage").ToList())
        {
            var at = sabotage.At!.Value;
            var point = Points(state).First(p => p.Id == sabotage.State);
            var board = state.Board!.WithoutToken(sabotage.Id).WithToken(point with { State = "broken" });
            var near = state.Ship.Adjacent(state.Ship.Get(at)).Select(c => c.Id).Append(at).ToHashSet();
            foreach (var crew in state.Crew.Where(c => c.Aboard && near.Contains(c.At)))
            {
                var key = $"suspect-{crew.Seat}";
                board = board.CounterOf(key) is null
                    ? board with { Counters = board.Counters.Add(new Counter(key, 1, Audience.Everyone)) }
                    : board.WithCounter(key, board.Value(key) + 1);
            }
            state = state.WithBoard(board);
            events.Add(new ScenarioNotice($"A repair failed in the {pack.Tile(state.Ship.Get(at).Tile).Name}.", at));
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
        var def = pack.ScenarioDef(Id);
        var here = Points(state).Where(p => p.At == me.At).ToList();
        if (here.Any(p => p.State == "broken"))
        {
            yield return new ScenarioAction("repair", "Repair the point");
        }
        var alone = !state.Crew.Any(c => c.Seat != seat && c.Aboard && c.At == me.At);
        var sabotaged = state.Board!.Tokens.Where(t => t.Kind == "sabotage").Select(t => t.State).ToHashSet();
        if (state.RoleOf(seat)?.Role == Mimic && alone && here.Any(p => p.State == "fixed" && !sabotaged.Contains(p.Id)))
        {
            yield return new ScenarioAction("sabotage", "Undo the repair, quietly");
        }
        if (Scanners.Contains(state.Ship.Get(me.At).Tile.Value) && state.Board!.Value("votes") < def.Number("votes")
            && state.Board.Value("voteRound") != state.Round)
        {
            yield return new ScenarioAction("callVote", "Call a vote at the scanner");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        var def = pack.ScenarioDef(Id);
        var at = state.CrewOf(act.From).At;
        switch (act.Action)
        {
            case "repair":
            {
                var point = Points(state).First(p => p.At == at && p.State == "broken");
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
                var fixedIt = total >= def.Number("repair");
                events.Add(new ScenarioActed(act.From, act.Action, point.Id, fixedIt ? "The point hums back to life." : "Sparks, then nothing."));
                return fixedIt ? next.WithBoard(next.Board!.WithToken(point with { State = "fixed" })) : next;
            }
            case "sabotage":
            {
                var sabotaged = state.Board!.Tokens.Where(t => t.Kind == "sabotage").Select(t => t.State).ToHashSet();
                var point = Points(state).First(p => p.At == at && p.State == "fixed" && !sabotaged.Contains(p.Id));
                // Nothing public happens yet: the point still looks repaired until the round ends (see RevealSabotage).
                return state.WithBoard(state.Board.WithToken(
                    new Token($"sabotage-{point.Id}", "sabotage", at, Audience.OfSeat(act.From), State: point.Id)));
            }
            case "callVote":
            {
                var voters = Primitives.Aboard(state).Select(c => c.Seat).ToEqArray();
                state = state.WithBoard(state.Board!.WithCounter("voteRound", state.Round));
                events.Add(new ScenarioActed(act.From, act.Action, null, "Everyone to the scanner. Who is it?"));
                return Primitives.CallVote(state, act.From, "mimic", voters, voters.Select(v => v.Value).ToEqArray(), events);
            }
            default:
                return state;
        }
    }

    public Outcome? Evaluate(RulesPack pack, GameState state)
    {
        var mimic = MimicOf(state);
        var crew = state.Board!.Roles.Where(r => r.Role == Crew).Select(r => r.Seat).ToEqArray();
        if (state.Board.Value("exposed") > 0)
        {
            return new Outcome(crew, "The crew named the mimic, and the scanner agreed.");
        }
        if (Points(state).All(p => p.State == "fixed") && !state.Board.Tokens.Any(t => t.Kind == "sabotage"))
        {
            return new Outcome(crew, "Every point repaired at once. The Meridian answers to the crew again.");
        }
        return crew.All(s => !state.CrewOf(s).Aboard)
            ? new Outcome([mimic], "Nobody is left who could tell the difference.")
            : null;
    }

    public Outcome AtLimit(RulesPack pack, GameState state)
        => new([MimicOf(state)], "Time ran out. One of them was never one of them.");

    /// <summary>The seat most suspected by the public suspicion counters (ties broken by chance), never yourself.</summary>
    private static string? Suspect(BotContext ctx, VotePending vote)
        => vote.Options.Where(o => o != ctx.Seat.Value)
            .OrderByDescending(o => ctx.Board.Value($"suspect-{o}")).ThenBy(_ => ctx.Dice.Below(1000)).FirstOrDefault();

    public IBotPolicy Bot(string role) => role switch
    {
        Crew => new PolicyBot(ctx =>
        {
            var broken = Points(ctx.State).Where(p => p.State == "broken").Select(p => p.At!.Value);
            var suspicious = ctx.State.Crew.Any(c => ctx.Board.Value($"suspect-{c.Seat}") > 0);
            return BotMoves.Act(ctx, "repair")
                   ?? (suspicious ? BotMoves.Act(ctx, "callVote") : null)
                   ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, broken));
        }, Suspect),
        Mimic => new PolicyBot(ctx =>
        {
            var fixedPoints = Points(ctx.State).Where(p => p.State == "fixed").Select(p => p.At!.Value).ToList();
            return BotMoves.Act(ctx, "sabotage")
                   ?? BotMoves.Toward(ctx, BotMoves.Nearest(ctx, fixedPoints.Count > 0
                       ? fixedPoints : Points(ctx.State).Select(p => p.At!.Value)));
        }, Suspect),
        _ => throw new ArgumentException($"One of Us Isn't deals no role {role}.", nameof(role)),
    };
}
