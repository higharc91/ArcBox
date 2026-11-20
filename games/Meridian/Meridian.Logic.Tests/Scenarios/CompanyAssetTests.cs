namespace Meridian.Logic.Tests.Scenarios;

public sealed class CompanyAssetTests
{
    private const string Id = CompanyAsset.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static SeatId Synthetic(GameState s) => s.Board!.Roles.Single(r => r.Role == CompanyAsset.Synthetic).Seat;

    [Fact]
    public void The_highest_tech_is_the_synthetic_commanding_drones_scaled_to_the_crew()
    {
        foreach (var players in new[] { 3, 4, 5, 6 })
        {
            var state = ScenarioRun.Begun(Id, players);
            var synthetic = Synthetic(state);
            var best = state.Crew.Max(c => Rules.Value(Pack, c, Trait.Tech));
            Assert.Equal(best, Rules.Value(Pack, state.CrewOf(synthetic), Trait.Tech));
            var drones = state.Board!.Tokens.Where(t => t.Kind == CompanyAsset.Drone).ToList();
            Assert.Equal(Math.Max(1, players - 2), drones.Count);
            Assert.All(drones, d => Assert.Equal(synthetic, d.Controller));
            Assert.All(state.Board.Roles, r => Assert.Equal(Audience.Everyone, r.Audience));   // a revealed traitor
            Assert.Equal(players >= 5 ? 3 : 2, CompanyAsset.Needed(Pack, state));
        }
    }

    [Fact]
    public void A_tie_for_highest_tech_goes_to_the_drawer()
    {
        var state = ScenarioRun.Begun(Id);
        var drawer = state.Breach!.Seat;
        // Everyone at the drawer's Tech: the drawer is chosen.
        var even = state with { Board = null, Crew = state.Crew.Select(c => c with { Steps = state.CrewOf(drawer).Steps, Crew = state.CrewOf(drawer).Crew }).ToEqArray() };
        var dealt = new CompanyAsset().Setup(Pack, even, state.Breach!);
        Assert.Equal(drawer, dealt.Board!.Roles.Single(r => r.Role == CompanyAsset.Synthetic).Seat);
    }

    [Fact]
    public void Transmitting_from_the_bridge_enough_times_wins_for_the_crew()
    {
        var game = Begin();
        var crew = game.State.Crew.First(c => c.Seat != Synthetic(game.State) && c.Aboard).Seat;
        var bridge = Transmitter(game);
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null };
        game.Put(crew, bridge);
        Assert.Contains(new ScenarioAct(crew, "transmit"), game.Legal(crew));
        Assert.DoesNotContain(new ScenarioAct(Synthetic(game.State), "transmit"), game.Legal(Synthetic(game.State)));
        for (var i = 0; i < CompanyAsset.Needed(Pack, game.State); i++)
        {
            game.State = game.State with { Turn = game.State.Turn with { Acted = false } };   // one action a turn: a new turn
            game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Tech), t => t >= 5).Do(new ScenarioAct(crew, "transmit"));
        }
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.DoesNotContain(Synthetic(game.State), game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void A_failed_transmission_draws_every_drone_a_step_closer()
    {
        var game = Begin();
        var crew = game.State.Crew.First(c => c.Seat != Synthetic(game.State) && c.Aboard).Seat;
        var bridge = Transmitter(game);
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null };
        game.Put(crew, bridge);
        var before = Primitives.Steps(game.State, bridge);
        var drones = game.State.Board!.Tokens.Where(t => t.Kind == CompanyAsset.Drone).ToList();
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Tech), t => t < 5).Do(new ScenarioAct(crew, "transmit"));
        var after = Primitives.Steps(game.State, bridge);
        foreach (var drone in drones.Where(d => before.TryGetValue(d.At!.Value, out var s) && s > 0))
        {
            var now = game.State.Board!.Tokens.First(t => t.Id == drone.Id).At!.Value;
            Assert.Equal(before[drone.At!.Value] - 1, after[now]);
        }
        Assert.Equal(0, game.State.Board!.Value("transmitted"));
    }

    [Fact]
    public void A_new_drone_deploys_each_round_up_to_one_less_than_the_players()
    {
        var game = Begin();
        var before = game.State.Board!.Tokens.Count(t => t.Kind == CompanyAsset.Drone);
        var round = game.State.Round;
        while (game.State.Round == round)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Equal(Math.Min(before + 1, game.State.Crew.Count - 1), game.State.Board!.Tokens.Count(t => t.Kind == CompanyAsset.Drone));
    }

    [Fact]
    public void No_witnesses_or_the_round_limit_wins_for_the_synthetic()
    {
        var game = Begin();
        foreach (var crew in game.State.Crew.Where(c => c.Seat != Synthetic(game.State)).ToList())
        {
            game.Resolve(r => r.Kill(crew.Seat));
        }
        game.Do(new EndTurn(game.State.ActiveSeat));
        Assert.Equal([Synthetic(game.State)], game.State.Board!.Outcome!.Winners);

        var timed = Begin();
        timed.State = timed.State.WithBoard(timed.State.Board! with { RoundLimit = 1 });
        while (timed.State.Phase == Phase.Endgame)
        {
            timed.Do(new EndTurn(timed.State.ActiveSeat));
        }
        Assert.Equal([Synthetic(timed.State)], timed.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void It_needs_a_transmitter_placed_or_still_in_the_pile()
    {
        var state = ScenarioRun.Begun(Id);
        var scenario = new CompanyAsset();
        var breach = state.Breach!;
        Assert.True(scenario.CanRun(Pack, state, breach));
        var without = state with
        {
            TilePile = state.TilePile.Where(t => t.Value is not ("bridge" or "comms_array")).ToEqArray(),
            Ship = state.Ship with { Compartments = state.Ship.Compartments.Where(c => c.Tile.Value is not ("bridge" or "comms_array")).ToEqArray() },
        };
        Assert.False(scenario.CanRun(Pack, without, breach));
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    private static Play Begin() => Play.From(Pack, ScenarioRun.Begun(Id));

    /// <summary>The Bridge, placed on a free Command cell if the ship hasn't found it yet.</summary>
    private static CompartmentId Transmitter(Play game)
    {
        if (Primitives.FirstOf(game.State, "bridge", "comms_array") is { } found)
        {
            return found;
        }
        foreach (var h in game.State.Ship.Frontier(Pack.Rules.DeckRadius))
        {
            var from = game.State.Ship.Get(h.Compartment);
            if (from.At.Deck == DeckId.Command)
            {
                var placed = Placement.Place(game.State.Ship, Pack.Tile(new TileId("bridge")), from.At.Step(h.Side), 0, Pack.Rules.DeckRadius);
                game.State = game.State with { Ship = placed.Ship, TilePile = game.State.TilePile.Remove(new TileId("bridge")) };
                return placed.Placed.Id;
            }
        }
        throw new InvalidOperationException("No free command cell.");
    }
}
