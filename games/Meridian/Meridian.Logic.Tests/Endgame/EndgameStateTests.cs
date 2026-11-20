namespace Meridian.Logic.Tests.Endgame;

public sealed class EndgameStateTests
{
    /// <summary>A game with a board: s2 is the secret mimic, a brood monster sits in the airlock, s1 drops a crowbar.</summary>
    internal static GameState WithBoard(Play game)
    {
        var mimic = new RoleCard(Play.S2, "mimic", "mimic", Audience.OfSeat(Play.S2));
        var board = new ScenarioBoard("stub", [new RoleCard(Play.S1, "crew", "crew", Audience.Everyone), mimic],
            [new Token("brood1", "brood", Play.C(1), Audience.Everyone, Controller: Play.S2, Stats: new MonsterStats(4, 3, 2)),
             new Token("dropped1", "item", Play.C(2), Audience.Everyone, Card: new CardId("pry_bar"))],
            [new Counter("sabotage", 1, Audience.OfSeat(Play.S2)), new Counter("repairs", 2, Audience.Everyone)],
            [new Objective("repair", "Repair four points.", ObjectiveState.Open, Audience.OfSide("crew"))],
            RoundLimit: 10, StartRound: 3);
        var state = game.State with
        {
            Phase = Phase.Endgame,
            Breach = new BreachInfo(Play.S1, new CardId("humming_shard"), Play.C(1), false, 6),
            Salvage = game.State.Salvage with { Draw = game.State.Salvage.Draw.Remove(new CardId("pry_bar")) },
        };
        return state.WithBoard(board);
    }

    [Fact]
    public void Endgame_records_round_trip_with_value_equality()
    {
        var state = WithBoard(Play.Game());
        var back = Json.RoundTrip(state);
        Assert.Equal(state, back);
        Assert.Equal(state.GetHashCode(), back.GetHashCode());
    }

    [Fact]
    public void Audiences_include_exactly_who_they_name()
    {
        var state = WithBoard(Play.Game());
        Assert.True(Audience.Everyone.Includes(state, null));
        Assert.False(Audience.OfSeat(Play.S2).Includes(state, null));     // never the TV
        Assert.True(Audience.OfSeat(Play.S2).Includes(state, Play.S2));
        Assert.False(Audience.OfSeat(Play.S2).Includes(state, Play.S1));
        Assert.True(Audience.OfSide("crew").Includes(state, Play.S1));
        Assert.False(Audience.OfSide("crew").Includes(state, Play.S2));
        Assert.True(Audience.OfRole("mimic").Includes(state, Play.S2));
        Assert.False(Audience.OfRole("mimic").Includes(state, Play.S3));   // no role dealt to s3
    }

    [Fact]
    public void A_valid_endgame_state_has_no_violations()
    {
        var game = Play.Game();
        Assert.Empty(Invariants.Check(game.Pack, WithBoard(game)));
    }

    [Fact]
    public void A_dropped_item_still_counts_its_card()
    {
        var game = Play.Game();
        var state = WithBoard(game);
        var lost = state.WithBoard(state.Board! with { Tokens = state.Board.Tokens.Where(t => t.Id != "dropped1").ToEqArray() });
        Assert.Contains("a card is missing or exists twice", Invariants.Check(game.Pack, lost));
    }

    [Fact]
    public void Broken_endgame_states_are_caught()
    {
        var game = Play.Game();
        var state = WithBoard(game);
        Assert.Contains(Invariants.Check(game.Pack, state.WithBoard(state.Board!.WithToken(
            new Token("ghost", "brood", new CompartmentId("c99"), Audience.Everyone)))), e => e.Contains("doesn't exist"));
        Assert.Contains(Invariants.Check(game.Pack, state.WithBoard(state.Board! with
        {
            Roles = state.Board.Roles.Add(new RoleCard(new SeatId("nobody"), "crew", "crew", Audience.Everyone)),
        })), e => e.Contains("role"));
        Assert.Contains("an endgame with no scenario board", Invariants.Check(game.Pack, state with { Board = null }));
        Assert.Contains("the game is over with no outcome", Invariants.Check(game.Pack, state with { Phase = Phase.Over }));
    }

    [Fact]
    public void Only_the_dead_may_sit_at_step_zero()
    {
        var game = Play.Game();
        var state = WithBoard(game);
        var crew = state.CrewOf(Play.S1) with { Steps = new TraitSteps(0, 4, 4, 4) };
        Assert.Contains(Invariants.Check(game.Pack, state.WithCrew(crew)), e => e.Contains("marker"));
        Assert.Empty(Invariants.Check(game.Pack, state.WithCrew(crew with { Alive = false })));
    }

    [Fact]
    public void Stats_count_reveals_cards_and_dice()
    {
        var game = Play.Game().Put(Play.S1, Play.C(2), moves: 4).Pile(CardKind.Salvage, ["pry_bar"]);
        game.State = game.State with { TilePile = [new TileId("storage")] };
        game.Do(new Open(Play.S1, Play.C(2), Side.E));
        Assert.Equal(new CrewStats(Revealed: 1, Cards: 1), game.Crew(Play.S1).Counted);
    }
}
