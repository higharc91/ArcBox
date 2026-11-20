namespace Meridian.Logic.Tests.Scenarios;

public sealed class ThePassengerTests
{
    private const string Id = ThePassenger.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static SeatId Host(GameState s) => s.Board!.Roles.Single(r => r.Role == ThePassenger.Host).Seat;

    [Fact]
    public void The_finder_hosts_it_and_commands_brood_scaled_to_the_crew()
    {
        var state = ScenarioRun.Begun(Id);
        var host = Host(state);
        Assert.Equal(state.Breach!.Seat, host);
        var brood = state.Board!.Tokens.Where(t => t.Kind == "brood").ToList();
        Assert.Equal(Math.Max(1, state.Crew.Count - 2), brood.Count);
        Assert.All(brood, b => Assert.Equal(host, b.Controller));
        Assert.All(state.Board.Roles, r => Assert.Equal(Audience.Everyone, r.Audience));   // a revealed traitor
        Assert.Equal(12, state.Board.RoundLimit);
    }

    [Fact]
    public void Synthesizing_in_a_lab_makes_a_dose_and_injecting_the_host_wins()
    {
        var game = Begin();
        var crew = game.State.Crew.First(c => c.Seat != Host(game.State) && c.Aboard).Seat;
        var lab = Placement.Place(game.State.Ship, Pack.Tile(new TileId("med_bay")), FreeCell(game.State), 0, Pack.Rules.DeckRadius);
        game.State = game.State with { Ship = lab.Ship, TilePile = game.State.TilePile.Remove(new TileId("med_bay")), Active = game.State.IndexOf(crew), Waiting = null };
        game.Put(crew, lab.Placed.Id);
        Assert.Contains(new ScenarioAct(crew, "synthesize"), game.Legal(crew));
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Tech), t => t >= 5).Do(new ScenarioAct(crew, "synthesize"));
        Assert.Equal(1, game.State.Board!.Value("doses"));

        game.Do(new EndTurn(crew));
        while (game.State.ActiveSeat != crew)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        game.Put(Host(game.State), lab.Placed.Id);
        var a = Rules.Value(Pack, game.Crew(crew), Trait.Brawn);
        var d = Rules.Value(Pack, game.Crew(Host(game.State)), Trait.Brawn);
        game.RigFight(a, d, (x, y) => x > y).Do(new ScenarioAct(crew, "inject"));
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.DoesNotContain(Host(game.State), game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void A_new_brood_spawns_with_the_host_each_round_while_there_are_fewer_than_one_less_than_the_players()
    {
        var game = Begin();
        var before = game.State.Board!.Tokens.Count(t => t.Kind == "brood");
        var round = game.State.Round;
        while (game.State.Round == round)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal(before + 1, game.State.Board!.Tokens.Count(t => t.Kind == "brood"));
    }

    [Fact]
    public void Killing_every_crew_member_wins_for_the_host()
    {
        var game = Begin();
        foreach (var crew in game.State.Crew.Where(c => c.Seat != Host(game.State)).ToList())
        {
            game.Resolve(r => r.Kill(crew.Seat));
        }
        game.Do(new EndTurn(game.State.ActiveSeat));
        Assert.Equal([Host(game.State)], game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void The_round_limit_goes_to_the_host()
    {
        var game = Begin();
        game.State = game.State.WithBoard(game.State.Board! with { RoundLimit = 1 });
        var round = game.State.Round;
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal([Host(game.State)], game.State.Board!.Outcome!.Winners);
        Assert.Equal(round + 1, game.State.Round);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    private static Play Begin()
    {
        return Play.From(Pack, ScenarioRun.Begun(Id));
    }

    private static Cell FreeCell(GameState state)
    {
        foreach (var h in state.Ship.Frontier(Pack.Rules.DeckRadius))
        {
            var from = state.Ship.Get(h.Compartment);
            if (from.At.Deck == DeckId.Habitat)
            {
                return from.At.Step(h.Side);
            }
        }
        throw new InvalidOperationException("No free habitat cell.");
    }
}
