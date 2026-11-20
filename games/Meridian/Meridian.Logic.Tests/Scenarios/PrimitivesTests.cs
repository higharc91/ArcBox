namespace Meridian.Logic.Tests.Scenarios;

/// <summary>The primitives M7's scenarios share (M7 plan, Task 2), on a real endgame's ship.</summary>
public sealed class PrimitivesTests
{
    private static readonly RulesPack Pack = ScenarioRun.Pack(ThePassenger.Id);

    private static GameState Endgame(ulong seed = 1) => ScenarioRun.Begun(ThePassenger.Id, players: 4, seed: seed);

    /// <summary>A hatch between two placed compartments, and the compartment on its other side.</summary>
    private static (CompartmentId At, Side Side, CompartmentId Next) AnOpenHatch(GameState state)
        => state.Ship.Compartments.SelectMany(c => Sides.All.Select(side => (c.Id, side, Next: Primitives.Through(state, c.Id, side))))
               .Where(x => x.Next is not null && !state.Ship.IsSealed(x.Id, x.side))
               .Select(x => (x.Id, x.side, x.Next!.Id)).First();

    [Fact]
    public void A_sealed_hatch_stops_crew_and_monsters_both_ways_until_unsealed()
    {
        var state = Endgame();
        var (at, side, next) = AnOpenHatch(state);
        var events = new List<GameEvent>();
        var sealedState = Primitives.Seal(state, at, side, events);

        Assert.Equal(new HatchSealed(at, side, Locked: true), Assert.Single(events));
        Assert.DoesNotContain(sealedState.Ship.Adjacent(sealedState.Ship.Get(at)), c => c.Id == next);
        Assert.DoesNotContain(sealedState.Ship.Adjacent(sealedState.Ship.Get(next)), c => c.Id == at);
        // Monsters move by Reach, as crew do: one step through the seal is out of reach, from either side.
        Assert.False(Reach.From(Pack, sealedState.Ship, at, 1).ContainsKey(next) && !state.Ship.Linked(at).Contains(next));
        Assert.Contains(side, Primitives.SealedExits(sealedState, at));
        Assert.Contains(side.Opposite(), Primitives.SealedExits(sealedState, next));

        events.Clear();
        var open = Primitives.Unseal(sealedState, next, side.Opposite(), events);
        Assert.Equal(new HatchUnsealed(next, side.Opposite()), Assert.Single(events));
        Assert.Equal(state.Ship, open.Ship);   // exactly as it was
    }

    [Fact]
    public void Only_a_hatch_between_two_compartments_can_be_sealed_and_walls_never_open()
    {
        var state = Endgame();
        // A hatch sealed when its tile was placed faces a wall or the deck's edge: never a scenario's to seal or open.
        var wall = state.Ship.Sealed.First();
        Assert.Null(Primitives.Through(state, wall.Compartment, wall.Side));
        Assert.Throws<InvalidOperationException>(() => Primitives.Seal(state, wall.Compartment, wall.Side, []));
        var events = new List<GameEvent>();
        Assert.Equal(state.Ship, Primitives.Unseal(state, wall.Compartment, wall.Side, events).Ship);
        Assert.Empty(events);

        // Sealing what's already sealed changes nothing and says nothing.
        var (at, side, _) = AnOpenHatch(state);
        var once = Primitives.Seal(state, at, side, []);
        Assert.Equal(once.Ship, Primitives.Seal(once, at, side, events).Ship);
        Assert.Empty(events);
        Assert.Equal(state.Ship.Sealed.Count + 2, once.Ship.Sealed.Count);
    }

    [Fact]
    public void Steps_count_compartments_through_open_hatches_and_seals_lengthen_them()
    {
        var state = Endgame();
        var (at, side, next) = AnOpenHatch(state);
        Assert.Equal(1, Primitives.Steps(state, at)[next]);
        var sealedState = Primitives.Seal(state, at, side, []);
        var around = Primitives.Steps(sealedState, at);
        Assert.True(!around.TryGetValue(next, out var steps) || steps > 1);
    }

    [Fact]
    public void Spread_takes_one_new_neighbour_per_source_and_never_crosses_a_seal()
    {
        var state = Endgame();
        var (at, side, next) = AnOpenHatch(state);
        var (spread, after) = Primitives.Spread(state, [at]);
        Assert.Single(spread);
        Assert.Contains(spread[0], state.Ship.Adjacent(state.Ship.Get(at)).Select(c => c.Id));
        Assert.NotEqual(state.Rng, after.Rng);

        // Sealed in on every side: it goes nowhere.
        var shut = Sides.All.Aggregate(state, (s, d) => Primitives.Through(s, at, d) is not null ? Primitives.Seal(s, at, d, []) : s);
        Assert.Empty(Primitives.Spread(shut, [at]).Spread);
        // Two sources never pick the same compartment.
        var two = Primitives.Spread(state, [at, next]).Spread;
        Assert.Equal(two.Count, two.Distinct().Count());
        Assert.DoesNotContain(at, two);
        Assert.DoesNotContain(next, two);
    }

    [Fact]
    public void A_carried_token_slows_its_carrier_changes_hands_on_a_hit_and_is_never_lost()
    {
        var state = Endgame();
        var (carrier, thief) = (state.Crew[0].Seat, state.Crew[1].Seat);
        state = state.WithBoard(state.Board!.WithToken(new Token("dose", "dose", null, Audience.Everyone, Carrier: carrier)));
        Token Dose(GameState s) => s.Board!.Tokens.First(t => t.Id == "dose");

        var slowed = Primitives.Carried(state with { MovesLeft = 4 }, "dose", 1, "Taken.", new TurnStarted(carrier, 1, 1), []);
        Assert.Equal(3, slowed.MovesLeft);
        Assert.Equal(1, Primitives.Carried(state with { MovesLeft = 1 }, "dose", 2, "Taken.", new TurnStarted(carrier, 1, 1), []).MovesLeft);

        var events = new List<GameEvent>();
        var hit = new Attacked(thief.Value, carrier.Value, new DiceRoll([2]), new DiceRoll([0]), "hit");
        var taken = Primitives.Carried(state, "dose", 1, "Taken.", hit, events);
        Assert.Equal(thief, Dose(taken).Carrier);
        Assert.Equal("Taken.", Assert.IsType<ScenarioActed>(Assert.Single(events)).Text);
        Assert.Equal(carrier, Dose(Primitives.Carried(state, "dose", 1, null, hit, [])).Carrier);   // not takeable

        var dropped = Primitives.Carried(state, "dose", 1, "Taken.", new Died(carrier), []);
        Assert.Null(Dose(dropped).Carrier);
        Assert.Equal(state.CrewOf(carrier).At, Dose(dropped).At);
        // An escaped carrier keeps it: it left the ship with them.
        Assert.Equal(carrier, Dose(Primitives.Carried(state.WithCrew(state.CrewOf(carrier) with { Escaped = true }), "dose", 1, "Taken.",
                                                      new TurnStarted(thief, 1, 1), [])).Carrier);
    }

    [Fact]
    public void Monsters_spawn_numbered_after_the_ones_on_the_board()
    {
        var state = Endgame();
        var host = state.Board!.Roles.First(r => r.Role == ThePassenger.Host).Seat;
        var count = state.Board.Tokens.Count(t => t.Kind == "brood");
        var spawned = Primitives.Spawn(state, "brood", state.CrewOf(host).At, host, new MonsterStats(4, 3, 2));
        var newest = spawned.Board!.Tokens.Single(t => !state.Board.Tokens.Contains(t));
        Assert.Equal($"brood-{count + 1}", newest.Id);
        Assert.Equal(host, newest.Controller);
        Assert.Equal("drone-1", Primitives.NextId(state.Board, "drone"));
    }

    [Fact]
    public void A_vent_costs_a_physical_step_on_a_failed_save_and_alone_means_nobody_else_aboard_there()
    {
        var state = Endgame();
        var crew = state.Crew[0];
        var events = new List<GameEvent>();
        var vented = Primitives.Vent(Pack, state, crew.At, save: 99, events);   // nobody makes 99
        Assert.All(state.Crew.Where(c => c.Aboard && c.At == crew.At),
                   c => Assert.Equal(c.Steps.Brawn + c.Steps.Agility - 1, vented.CrewOf(c.Seat).Steps.Brawn + vented.CrewOf(c.Seat).Steps.Agility));
        Assert.Contains(events, e => e is TraitChanged);
        Assert.Equal(state.Crew.Count(c => c.Aboard && c.At == crew.At) == 1, Primitives.Alone(state, crew.Seat));
    }

    [Fact]
    public void A_revealed_token_is_seen_by_everyone()
    {
        var state = Endgame();
        var seat = state.Crew[0].Seat;
        state = state.WithBoard(state.Board!.WithToken(new Token("charge-1", "charge", state.Crew[0].At, Audience.OfSeat(seat))));
        Assert.DoesNotContain(Visibility.For(state, null).Board!.Tokens, t => t.Id == "charge-1");
        Assert.Contains(Visibility.For(Primitives.Reveal(state, "charge-1"), null).Board!.Tokens, t => t.Id == "charge-1");
    }
}
