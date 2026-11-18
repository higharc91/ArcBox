namespace Meridian.Logic.Tests.Scenarios;

public sealed class CaptainOnDeckTests
{
    private const string Id = CaptainOnDeck.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static SeatId Captain(GameState s) => s.Board!.Roles.Single(r => r.Role == CaptainOnDeck.Captain).Seat;
    private static IEnumerable<Token> Cores(GameState s) => s.Board!.Tokens.Where(t => t.Kind == CaptainOnDeck.Core);

    [Fact]
    public void The_lowest_nerve_speaks_for_the_captain_and_three_cores_lie_apart()
    {
        foreach (var players in new[] { 3, 4, 5, 6 })
        {
            var state = ScenarioRun.Begun(Id, players);
            var lowest = state.Crew.Min(c => Rules.Value(Pack, c, Trait.Nerve));
            Assert.Equal(lowest, Rules.Value(Pack, state.CrewOf(Captain(state)), Trait.Nerve));
            var cores = Cores(state).ToList();
            Assert.Equal(3, cores.Count);
            Assert.All(cores, c => Assert.DoesNotContain(c.At!.Value, state.Ship.Compartments.Take(Pack.Start.Place.Count).Select(x => x.Id)));
            foreach (var core in cores)
            {
                var steps = Primitives.Steps(state, core.At!.Value);
                Assert.All(cores.Where(o => o != core), o => Assert.True(!steps.TryGetValue(o.At!.Value, out var s) || s >= 2));
            }
            Assert.Equal(2, state.Board!.Value("authority"));
            Assert.All(state.Board.Roles, r => Assert.Equal(Audience.Everyone, r.Audience));
        }
    }

    [Fact]
    public void Authority_grows_each_round_to_four()
    {
        var game = Begin();
        for (var round = 0; round < 4; round++)
        {
            var now = game.State.Round;
            while (game.State.Round == now && game.State.Phase == Phase.Endgame)
            {
                game.Do(new EndTurn(game.State.ActiveSeat));
            }
        }
        Assert.Equal(4, game.State.Board!.Value("authority"));
    }

    [Fact]
    public void The_captain_seals_a_hatch_for_one_authority_and_the_crew_cut_through_it()
    {
        var game = Begin();
        var captain = Captain(game.State);
        var (room, side) = OpenExit(game.State);
        game.Put(captain, room);
        game.State = game.State with { Active = game.State.IndexOf(captain), Waiting = null };
        game.Do(new ScenarioAct(captain, "seal", side.ToString()));
        Assert.True(game.State.Ship.IsSealed(room, side));
        Assert.Equal(1, game.State.Board!.Value("authority"));
        Assert.Contains(game.Events, e => e is HatchSealed { Locked: true });

        // A crew member on the far side cuts back through.
        var crew = game.State.Crew.First(c => c.Seat != captain && c.Aboard).Seat;
        var far = Primitives.Through(game.State, room, side)!.Id;
        game.Put(crew, far);
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null, Turn = game.State.Turn with { Acted = false } };
        Assert.Contains(new ScenarioAct(crew, "cut", side.Opposite().ToString()), game.Legal(crew));
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Brawn), t => t >= 5).Do(new ScenarioAct(crew, "cut", side.Opposite().ToString()));
        Assert.False(game.State.Ship.IsSealed(room, side));
        Assert.Contains(game.Events, e => e is HatchUnsealed);
    }

    [Fact]
    public void A_vent_costs_two_authority_and_hurts_everyone_who_fails_the_save()
    {
        var game = Begin();
        var captain = Captain(game.State);
        var crew = game.State.Crew.Where(c => c.Seat != captain && c.Aboard).Select(c => c.Seat).Take(2).ToList();
        var room = game.State.CrewOf(crew[0]).At;
        game.Put(captain, room);
        game.Put(crew[1], room);
        game.State = game.State with { Active = game.State.IndexOf(captain), Waiting = null, Rng = Rng.Seeded(1) };
        var before = crew.Sum(c => game.Crew(c).Steps.Brawn + game.Crew(c).Steps.Agility);
        game.Do(new ScenarioAct(captain, "vent", room.Value));
        Assert.Equal(0, game.State.Board!.Value("authority"));
        Assert.Contains(game.Events, e => e is ScenarioNotice { Fx: "vent" });
        Assert.True(crew.Sum(c => game.Crew(c).Steps.Brawn + game.Crew(c).Steps.Agility) <= before);
        // With no authority left there's nothing more to spend.
        game.State = game.State with { Turn = game.State.Turn with { Acted = false } };
        Assert.DoesNotContain(game.Legal(captain), c => c is ScenarioAct { Action: "vent" or "seal" });
    }

    [Fact]
    public void Pulling_three_cores_wins_for_the_crew()
    {
        var game = Begin();
        var crew = game.State.Crew.First(c => c.Seat != Captain(game.State) && c.Aboard).Seat;
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null };
        foreach (var core in Cores(game.State).ToList())
        {
            game.Put(crew, core.At!.Value);
            game.State = game.State with { Turn = game.State.Turn with { Acted = false } };
            game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Brawn), t => t >= 4).Do(new ScenarioAct(crew, "pull"));
        }
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.DoesNotContain(Captain(game.State), game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void The_round_limit_goes_to_the_captain()
    {
        var game = Begin();
        game.State = game.State.WithBoard(game.State.Board! with { RoundLimit = 1 });
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal([Captain(game.State)], game.State.Board!.Outcome!.Winners);
    }

    /// <summary>Review focus 3: sealed in on every side, a crew bot cuts its way out.</summary>
    [Fact]
    public void A_crew_bot_sealed_in_cuts_its_way_out()
    {
        var game = Begin();
        var crew = game.State.Crew.First(c => c.Seat != Captain(game.State) && c.Aboard && Cores(game.State).All(k => k.At != c.At)).Seat;
        var room = game.Crew(crew).At;
        game.State = Sides.All.Aggregate(game.State, (s, d) => Primitives.Through(s, room, d) is not null ? Primitives.Seal(s, room, d, []) : s);
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null, MovesLeft = 3 };
        Assume(Primitives.SealedExits(game.State, room).Any());
        var bot = new CaptainOnDeck().Bot(CaptainOnDeck.Crew);
        var rng = Rng.Seeded(1);
        var command = bot.Choose(Pack, game.State, crew, game.Legal(crew).ToEqArray(), ref rng);
        Assert.Equal("cut", Assert.IsType<ScenarioAct>(command).Action);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    private static void Assume(bool condition) => Assert.True(condition, "the setup didn't hold");

    private static Play Begin() => Play.From(Pack, ScenarioRun.Begun(Id));

    private static (CompartmentId Room, Side Side) OpenExit(GameState state)
        => state.Ship.Compartments.SelectMany(c => Sides.All.Select(s => (c.Id, s)))
            .First(x => Primitives.Through(state, x.Id, x.s) is not null && !state.Ship.IsSealed(x.Id, x.s));
}
