namespace Meridian.Logic.Tests.Endgame;

/// <summary>
/// A scenario exercising the primitives: "tinker" (anywhere, a Tech check that adds its total to "work"), "here" (only in
/// the docking ring) and "accuse" (opens a vote among everyone aboard, naming a seat). Round ends add one "hazard".
/// </summary>
internal sealed class PrimitiveScenario : IScenario
{
    public ScenarioInfo Info { get; } = new("prim", "test", 3, 6);
    public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => true;

    public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
        => state.WithBoard(new ScenarioBoard("prim",
            state.Crew.Select(c => new RoleCard(c.Seat, "crew", "crew", Audience.Everyone)).ToEqArray(), [],
            [new Counter("work", 0, Audience.Everyone), new Counter("hazard", 0, Audience.Everyone)], [], 99, 0));

    public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events) => e switch
    {
        RoundClosed => state.WithBoard(state.Board!.WithCounter("hazard", state.Board.Value("hazard") + 1)),
        VoteResolved { Result: { } named } => state.WithBoard(state.Board! with
        {
            Outcome = null,
            Counters = state.Board.Counters.Add(new Counter($"named-{named}", 1, Audience.Everyone)),
        }),
        _ => state,
    };

    public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
    {
        yield return new ScenarioAction("tinker", "Tinker");
        yield return new ScenarioAction("accuse", "Call a vote");
        if (state.CrewOf(seat).At == CompartmentId.Numbered(2))
        {
            yield return new ScenarioAction("here", "Only here");
        }
    }

    public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
    {
        switch (act.Action)
        {
            case "tinker":
                var (next, total) = Primitives.Check(pack, state, act.From, Trait.Tech, events);
                return next.WithBoard(next.Board!.WithCounter("work", next.Board.Value("work") + total));
            case "accuse":
                var voters = Primitives.Aboard(state).Select(c => c.Seat).ToEqArray();
                return Primitives.CallVote(state, act.From, "accuse", voters, voters.Select(v => v.Value).ToEqArray(), events);
            default:
                return state;
        }
    }

    public Outcome? Evaluate(RulesPack pack, GameState state) => null;
    public Outcome AtLimit(RulesPack pack, GameState state) => new([], "limit");
    public IBotPolicy Bot(string role) => ExploreBot.Instance;
}

public sealed class PrimitiveTests
{
    private static Play Begun()
    {
        var pack = TestPack.Load().WithScenarios(new ScenarioSet([new PrimitiveScenario()]),
            new BreachTable(["prim"], []), [new ScenarioDef("prim", "Prim", 99, [new RoleDef("crew", "crew", "Crew", "x")])]);
        var game = Play.Game(pack).Pile(CardKind.Anomaly, ["humming_shard"]);
        game.State = game.State with { AnomaliesDrawn = 8 };
        game.Draw(CardKind.Anomaly);
        game.Do(new Begin(game.State.ActiveSeat));
        return game;
    }

    [Fact]
    public void Actions_appear_only_where_they_are_allowed()
    {
        var game = Begun();
        var seat = game.State.ActiveSeat;
        Assert.Contains(new ScenarioAct(seat, "tinker"), game.Legal(seat));
        Assert.DoesNotContain(new ScenarioAct(seat, "here"), game.Legal(seat));
        game.Put(seat, Play.C(2));
        Assert.Contains(new ScenarioAct(seat, "here"), game.Legal(seat));
    }

    [Fact]
    public void A_check_action_rolls_at_once_and_one_action_is_taken_per_turn()
    {
        var game = Begun();
        var seat = game.State.ActiveSeat;
        game.Do(new ScenarioAct(seat, "tinker"));
        var rolled = Assert.Single(game.Events.OfType<Rolled>());
        Assert.Equal(rolled.Dice.Total, game.State.Board!.Value("work"));
        Assert.DoesNotContain(game.Legal(seat), c => c is ScenarioAct);
    }

    [Fact]
    public void Hazards_run_once_per_round_end()
    {
        var game = Begun();
        for (var i = 0; i < 3; i++)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal(1, game.State.Board!.Value("hazard"));
        Assert.Single(game.Events.OfType<RoundClosed>());
    }

    [Fact]
    public void A_vote_takes_ballots_in_any_order_and_hides_them_until_resolved()
    {
        var game = Begun();
        game.Do(new ScenarioAct(game.State.ActiveSeat, "accuse"));
        var vote = Assert.IsType<VotePending>(game.State.Waiting);
        Assert.Equal(3, vote.Owed.Count);
        Assert.All([Play.S1, Play.S2, Play.S3], s => Assert.Contains(new CastVote(s, "s2"), game.Legal(s)));

        game.Do(new CastVote(Play.S3, "s2"));
        game.Do(new CastVote(Play.S1, "s2"));
        Assert.DoesNotContain(game.Events, e => e is VoteResolved);
        Assert.Equal([new BallotCast(Play.S3), new BallotCast(Play.S1)], game.Events.OfType<BallotCast>());
        Assert.Empty(game.Legal(Play.S1));   // already voted
        game.Do(new CastVote(Play.S2, "s3"));

        var resolved = Assert.Single(game.Events.OfType<VoteResolved>());
        Assert.Equal("s2", resolved.Result);
        Assert.Equal([new VoteCount("s2", 2), new VoteCount("s3", 1)], resolved.Tally);
        Assert.Null(game.State.Waiting);
        Assert.NotNull(game.State.Board!.CounterOf("named-s2"));
    }

    [Fact]
    public void A_tied_or_abstained_vote_names_nobody()
    {
        var game = Begun();
        game.Do(new ScenarioAct(game.State.ActiveSeat, "accuse"));
        game.Do(new CastVote(Play.S1, "s2"));
        game.Do(new CastVote(Play.S2, "s3"));
        game.Do(new Logic.Timeout(Play.S3));   // abstains
        Assert.Null(Assert.Single(game.Events.OfType<VoteResolved>()).Result);
    }

    [Fact]
    public void A_voter_who_dies_abstains()
    {
        var game = Begun();
        game.Do(new ScenarioAct(game.State.ActiveSeat, "accuse"));
        game.Resolve(r => r.Kill(Play.S3));
        Assert.DoesNotContain(Play.S3, ((VotePending)game.State.Waiting!).Owed);
    }

    [Fact]
    public void A_vote_round_trips_mid_way()
    {
        var game = Begun();
        game.Do(new ScenarioAct(game.State.ActiveSeat, "accuse"));
        game.Do(new CastVote(Play.S1, "s2"));
        Assert.Equal(game.State, Json.RoundTrip(game.State));
    }
}
