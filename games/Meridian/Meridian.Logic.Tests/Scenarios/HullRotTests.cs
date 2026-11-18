namespace Meridian.Logic.Tests.Scenarios;

public sealed class HullRotTests
{
    private const string Id = HullRot.Id;
    private static RulesPack Pack => ScenarioRun.Pack(Id);

    private static CompartmentId Airlock(GameState s) => Primitives.FirstOf(s, "airlock")!.Value;
    private static Token Source(GameState s) => s.Board!.Tokens.First(t => t.Id == HullRot.Source);
    private static HashSet<CompartmentId> Rotted(GameState s) => s.Board!.Tokens.Where(t => t.Kind == HullRot.Rot).Select(t => t.At!.Value).ToHashSet();

    [Fact]
    public void The_source_starts_far_from_the_airlock_in_its_own_rot()
    {
        foreach (var players in new[] { 3, 6 })
        {
            var state = ScenarioRun.Begun(Id, players);
            var at = Source(state).At!.Value;
            Assert.Equal([at], Rotted(state));
            Assert.True(Primitives.Steps(state, Airlock(state))[at] >= 2);
            Assert.All(state.Board!.Roles, r => Assert.Equal(HullRot.Crew, r.Role));
        }
    }

    [Fact]
    public void The_rot_spreads_along_open_hatches_at_each_rounds_end_and_reaching_the_airlock_loses()
    {
        var game = Begin();
        var before = Rotted(game.State);
        EndRound(game);
        var after = Rotted(game.State);
        Assert.True(after.Count > before.Count);
        Assert.True(after.Count - before.Count <= Pack.ScenarioDef(Id).Number("maxSpread"));
        Assert.All(after.Except(before), r => Assert.Contains(before, b => game.State.Ship.Adjacent(game.State.Ship.Get(b)).Any(c => c.Id == r)));
        Assert.Contains(game.Events, e => e is ScenarioNotice { Fx: "rot" });

        // Rot in the Airlock at a round's end: the ship is lost.
        var near = Begin();
        near.State = near.State.WithBoard(near.State.Board!.WithToken(new Token("rot-9", HullRot.Rot, Airlock(near.State), Audience.Everyone)));
        EndRound(near);
        Assert.Equal(Phase.Over, near.State.Phase);
        Assert.Empty(near.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void Burning_clears_rot_but_never_the_sources_own_while_the_source_is_there()
    {
        var game = Begin();
        var crew = game.State.Crew[0].Seat;
        var source = Source(game.State).At!.Value;
        game.Put(crew, source);
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null, Turn = game.State.Turn with { Acted = false } };
        Assert.DoesNotContain(new ScenarioAct(crew, "burn"), game.Legal(crew));

        var other = game.State.Ship.Adjacent(game.State.Ship.Get(source)).First().Id;
        game.State = game.State.WithBoard(game.State.Board!.WithToken(new Token("rot-9", HullRot.Rot, other, Audience.Everyone)));
        game.Put(crew, other);
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Tech), t => t >= 4).Do(new ScenarioAct(crew, "burn"));
        Assert.DoesNotContain(other, Rotted(game.State));
    }

    [Fact]
    public void A_clean_source_is_cut_free_carried_slowly_and_jettisoned_for_the_win()
    {
        var game = Begin();
        var crew = game.State.Crew[0].Seat;
        var source = Source(game.State).At!.Value;
        game.Put(crew, source);
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null, Turn = game.State.Turn with { Acted = false } };
        Assert.Contains(new ScenarioAct(crew, "cut"), game.Legal(crew));   // only its own compartment is rotted
        game.Rig(Rules.Value(Pack, game.Crew(crew), Trait.Brawn), t => t >= 5).Do(new ScenarioAct(crew, "cut"));
        Assert.Equal(crew, Source(game.State).Carrier);

        game.Put(crew, Airlock(game.State));
        game.State = game.State with { Turn = game.State.Turn with { Acted = false } };
        game.Do(new ScenarioAct(crew, "jettison"));
        Assert.Equal(Phase.Over, game.State.Phase);
        Assert.Equal(game.State.Crew.Count, game.State.Board!.Outcome!.Winners.Count);
    }

    [Fact]
    public void Rot_nearby_keeps_the_source_rooted()
    {
        var game = Begin();
        var crew = game.State.Crew[0].Seat;
        var source = Source(game.State).At!.Value;
        var near = game.State.Ship.Adjacent(game.State.Ship.Get(source)).First().Id;
        game.State = game.State.WithBoard(game.State.Board!.WithToken(new Token("rot-9", HullRot.Rot, near, Audience.Everyone)));
        game.Put(crew, source);
        game.State = game.State with { Active = game.State.IndexOf(crew), Waiting = null, Turn = game.State.Turn with { Acted = false } };
        Assert.DoesNotContain(new ScenarioAct(crew, "cut"), game.Legal(crew));
    }

    /// <summary>Review focus 5: a carrier who dies drops the source where they fell.</summary>
    [Fact]
    public void A_dead_carrier_drops_the_source()
    {
        var game = Begin();
        var crew = game.State.Crew[0].Seat;
        game.State = game.State.WithBoard(game.State.Board!.WithToken(Source(game.State) with { At = null, Carrier = crew }));
        game.Resolve(r => r.Kill(crew));
        var after = new HullRot().On(Pack, game.State, new Died(crew), []);   // the scenario hears of the death
        Assert.Equal(game.Crew(crew).At, Source(after).At);
        Assert.Null(Source(after).Carrier);
    }

    [Fact]
    public void The_round_limit_goes_to_the_rot()
    {
        var game = Begin();
        game.State = game.State.WithBoard(game.State.Board! with { RoundLimit = 1 });
        while (game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
        Assert.Empty(game.State.Board!.Outcome!.Winners);
    }

    [Fact]
    public void Bots_play_it_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 25);

    [Fact]
    [Trait("Category", "Integration")]
    public void Bots_play_two_hundred_games_to_an_outcome() => ScenarioRun.Sweep(Id, seeds: 200);

    private static Play Begin() => Play.From(Pack, ScenarioRun.Begun(Id));

    private static void EndRound(Play game)
    {
        var round = game.State.Round;
        while (game.State.Round == round && game.State.Phase == Phase.Endgame)
        {
            game.Do(new EndTurn(game.State.ActiveSeat));
        }
    }
}
