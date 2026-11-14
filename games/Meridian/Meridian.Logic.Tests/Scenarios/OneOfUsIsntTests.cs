namespace Meridian.Logic.Tests.Scenarios;

public sealed class OneOfUsIsntTests
{
    private const string Id = OneOfUsIsnt.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static Play Begin(int players = 4, ulong seed = 1) => Play.From(Pack, ScenarioRun.Begun(Id, players, seed));
    private static SeatId Mimic(GameState s) => s.Board!.Roles.Single(r => r.Role == OneOfUsIsnt.Mimic).Seat;
    private static IEnumerable<Token> Points(GameState s) => s.Board!.Tokens.Where(t => t.Kind == "repair");

    /// <summary>Makes it <paramref name="seat"/>'s free turn, standing alone at a point in <paramref name="state"/>.</summary>
    private static Play AtPoint(Play game, SeatId seat, string pointState)
    {
        var point = Points(game.State).First();
        var board = game.State.Board!.WithToken(point with { State = pointState });
        game.State = game.State.WithBoard(board) with { Active = game.State.IndexOf(seat), Waiting = null, Turn = new TurnFlags() };
        foreach (var other in game.State.Crew.Where(c => c.Seat != seat))
        {
            game.Put(other.Seat, Play.C(1));
        }
        return game.Put(seat, point.At!.Value);
    }

    [Fact]
    public void One_secret_mimic_and_four_broken_points()
    {
        var state = ScenarioRun.Begun(Id);
        Assert.Single(state.Board!.Roles, r => r.Role == OneOfUsIsnt.Mimic);
        Assert.All(state.Board.Roles, r => Assert.Equal(Audience.OfSeat(r.Seat), r.Audience));   // every card secret alike
        Assert.Equal(4, Points(state).Count());
        Assert.All(Points(state), p => Assert.Equal("broken", p.State));
    }

    [Fact]
    public void Nobody_but_the_mimic_can_see_who_it_is()
    {
        var state = ScenarioRun.Begun(Id);
        var mimic = Mimic(state);
        Assert.DoesNotContain("\"mimic\"", Json.Write(Visibility.For(state, null)));
        foreach (var crew in state.Crew.Where(c => c.Seat != mimic))
        {
            var seen = Visibility.For(state, crew.Seat);
            Assert.DoesNotContain("\"mimic\"", Json.Write(seen));
            Assert.Equal(Visibility.UnknownRole, seen.RoleOf(mimic)!.Role);
            Assert.Equal(OneOfUsIsnt.Crew, seen.RoleOf(crew.Seat)!.Role);
        }
        Assert.Equal(OneOfUsIsnt.Mimic, Visibility.For(state, mimic).RoleOf(mimic)!.Role);
    }

    [Fact]
    public void A_repair_fixes_the_point()
    {
        var game = Begin();
        var crew = game.State.Crew.First(c => c.Seat != Mimic(game.State)).Seat;
        AtPoint(game, crew, "broken");
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Tech), t => t >= 4).Do(new ScenarioAct(crew, "repair"));
        Assert.Equal("fixed", Points(game.State).First().State);
    }

    [Fact]
    public void Only_the_mimic_alone_can_sabotage_and_it_shows_only_at_the_round_end()
    {
        var game = Begin();
        var mimic = Mimic(game.State);
        var crew = game.State.Crew.First(c => c.Seat != mimic).Seat;
        AtPoint(game, crew, "fixed");
        Assert.DoesNotContain(new ScenarioAct(crew, "sabotage"), game.Legal(crew));

        AtPoint(game, mimic, "fixed");
        game.Put(crew, Points(game.State).First().At!.Value);
        Assert.DoesNotContain(new ScenarioAct(mimic, "sabotage"), game.Legal(mimic));   // not alone
        game.Put(crew, Play.C(1));
        game.Do(new ScenarioAct(mimic, "sabotage"));

        Assert.Equal("fixed", Points(Visibility.For(game.State, crew)).First().State);   // still looks repaired
        Assert.DoesNotContain(game.Events, e => e is ScenarioNotice);
        var round = game.State.Round;
        while (game.State.Round == round && game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal("broken", Points(game.State).First().State);
        Assert.Contains(game.Events, e => e is ScenarioNotice n && n.Text.StartsWith("A repair failed"));
        Assert.DoesNotContain(game.State.Board!.Tokens, t => t.Kind == "sabotage");
    }

    [Fact]
    public void Naming_the_mimic_wins_and_two_votes_is_the_limit()
    {
        var game = Begin();
        var mimic = Mimic(game.State);
        var scanner = game.State.Ship.Compartments.First(c => c.Tile.Value == "command_landing").Id;
        var caller = game.State.Crew.First(c => c.Seat != mimic).Seat;

        void Vote(string choice)
        {
            game.State = game.State with { Active = game.State.IndexOf(caller), Waiting = null, Turn = new TurnFlags() };
            game.Put(caller, scanner).Do(new ScenarioAct(caller, "callVote"));
            foreach (var voter in ((VotePending)game.State.Waiting!).Owed)
            {
                game.Do(new CastVote(voter, choice));
            }
        }

        Vote(caller.Value);   // wrong
        Assert.Equal(Phase.Endgame, game.State.Phase);
        Assert.Equal(1, game.State.Board!.Value("votes"));
        Assert.DoesNotContain(new ScenarioAct(caller, "callVote"), game.Legal(caller));   // once per round

        game.State = game.State with { Round = game.State.Round + 1 };
        Vote(mimic.Value);
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.DoesNotContain(mimic, game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void Ballots_stay_hidden_until_the_vote_resolves()
    {
        // M3 review focus 5, at the rules level.
        var game = Begin();
        var caller = game.State.Crew.First(c => c.Seat != Mimic(game.State)).Seat;
        var scanner = game.State.Ship.Compartments.First(c => c.Tile.Value == "command_landing").Id;
        game.State = game.State with { Active = game.State.IndexOf(caller), Waiting = null, Turn = new TurnFlags() };
        game.Put(caller, scanner).Do(new ScenarioAct(caller, "callVote"));
        var first = ((VotePending)game.State.Waiting!).Owed[0];
        game.Do(new CastVote(first, caller.Value));
        foreach (var other in game.State.Crew.Where(c => c.Seat != first))
        {
            Assert.Empty(((VotePending)Visibility.For(game.State, other.Seat).Waiting!).Cast);
        }
    }

    [Fact]
    public void The_round_limit_goes_to_the_mimic()
    {
        var game = Begin();
        game.State = game.State.WithBoard(game.State.Board! with { RoundLimit = 1 });
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal([Mimic(game.State)], game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);
}
