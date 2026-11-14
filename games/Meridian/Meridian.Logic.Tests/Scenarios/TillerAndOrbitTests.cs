namespace Meridian.Logic.Tests.Scenarios;

public sealed class TillerSaysNoTests
{
    private const string Id = TillerSaysNo.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);
    private static Play Begin() => Play.From(Pack, ScenarioRun.Begun(Id));
    private static CompartmentId Core(GameState s) => s.Board!.Tokens.Single(t => t.Id == "core").At!.Value;

    [Fact]
    public void The_core_is_on_the_bridge_or_the_command_landing_and_nobody_is_a_traitor()
    {
        var state = ScenarioRun.Begun(Id);
        Assert.Contains(state.Ship.Get(Core(state)).Tile.Value, new[] { "bridge", "command_landing" });
        Assert.All(state.Board!.Roles, r => Assert.Equal(TillerSaysNo.Crew, r.Role));
    }

    [Fact]
    public void Pulling_the_last_bank_wins_for_everyone()
    {
        var game = Begin();
        var seat = game.State.ActiveSeat;
        game.State = game.State.WithBoard(game.State.Board!.WithCounter("banks", TillerSaysNo.BanksNeeded(Pack, game.State) - 1));
        game.Put(seat, Core(game.State));
        var target = Pack.ScenarioDef(Id).Number("bankTarget");
        game.Rig(Rules.Value(Pack, game.Crew(seat), Trait.Tech), t => t >= target).Do(new ScenarioAct(seat, "pullBank"));
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.Equal(game.State.Crew.Count, game.State.Board!.Outcome!.Winners.Count);
    }

    [Fact]
    public void Tiller_vents_the_most_crowded_compartment_each_round()
    {
        var game = Begin();
        var round = game.State.Round;
        while (game.State.Round == round)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        var vented = Assert.Single(game.Events.OfType<ScenarioNotice>(), n => n.Text.StartsWith("Tiller vents"));
        var checks = game.Events.OfType<Rolled>().Count(r => r.Trait == Trait.Agility);
        Assert.Equal(game.State.Crew.Count(c => c.At == vented.At || !c.Alive), checks);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);
}

public sealed class DecayingOrbitTests
{
    private const string Id = DecayingOrbit.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);
    private static Play Begin(int players = 4) => Play.From(Pack, ScenarioRun.Begun(Id, players));
    private static CompartmentId Place(GameState s, string id) => s.Board!.Tokens.Single(t => t.Id == id).At!.Value;

    [Fact]
    public void It_can_run_from_the_start_layout_alone_at_every_player_count()
    {
        // M3 review focus 6: the fallback needs nothing that might not be placed.
        foreach (var players in new[] { 3, 4, 5, 6 })
        {
            var start = Rules.NewGame(Pack, BotRunner.Seats(Pack, players), 1);
            var breach = new BreachInfo(start.ActiveSeat, null, null, true, null);
            Assert.True(new DecayingOrbit().CanRun(Pack, start, breach));
            var set = new DecayingOrbit().Setup(Pack, start, breach);
            Assert.Equal(players - 1, set.Board!.Value("pods"));
            Assert.Equal("airlock", set.Ship.Get(Place(set, "pods")).Tile.Value);
            Assert.Equal("engineering_landing", set.Ship.Get(Place(set, "engine")).Tile.Value);
        }
    }

    [Fact]
    public void Boarding_a_pod_escapes_and_leaves_the_turn_order()
    {
        var game = Begin();
        var seat = game.State.ActiveSeat;
        game.Put(seat, Place(game.State, "pods"));
        game.Do(new ScenarioAct(seat, "board"));
        Assert.True(game.Crew(seat).Escaped);
        Assert.Equal(2, game.State.Board!.Value("pods"));
        game.Do(new EndTurn(seat));
        for (var i = 0; i < 6 && game.State.Phase == Phase.Endgame; i++)
        {
            Assert.NotEqual(seat, game.State.ActiveSeat);
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
    }

    [Fact]
    public void Restarting_the_engines_saves_everyone_aboard_and_the_escaped()
    {
        var game = Begin();
        var seat = game.State.ActiveSeat;
        var other = game.State.Crew.First(c => c.Seat != seat).Seat;
        game.State = game.State.WithCrew(game.Crew(other) with { Escaped = true }).WithBoard(game.State.Board!.WithCounter("engines", 2));
        game.Put(seat, Place(game.State, "engine"));
        var target = Pack.ScenarioDef(Id).Number("restartTarget");
        game.Rig(Rules.Value(Pack, game.Crew(seat), Trait.Tech), t => t >= target).Do(new ScenarioAct(seat, "restart"));
        Assert.Equal(game.State.Crew.Count, game.State.Board!.Outcome!.Winners.Count);
    }

    [Fact]
    public void At_orbit_zero_everyone_aboard_is_lost_and_only_the_escaped_win()
    {
        var game = Begin();
        var escaped = game.State.Crew[0].Seat;
        game.State = game.State.WithCrew(game.Crew(escaped) with { Escaped = true }).WithBoard(game.State.Board!.WithCounter("orbit", 1));
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal([escaped], game.State.Board!.Outcome!.Winners);
        Assert.All(game.State.Crew.Where(c => !c.Escaped), c => Assert.False(c.Alive));
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);
}
