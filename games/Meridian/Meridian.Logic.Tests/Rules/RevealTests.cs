namespace Meridian.Logic.Tests.Turns;

// s1 stands in the docking ring (c2) and opens its east hatch onto habitat (1,1).
public sealed class RevealTests
{
    private static Play AtRing(params string[] pile)
    {
        var game = Play.Game().Put(Play.S1, Play.C(2), moves: 4);
        game.State = game.State with { TilePile = pile.Select(t => new TileId(t)).ToEqArray() };
        return game;
    }

    private static readonly Open East = new(Play.S1, Play.C(2), Side.E);

    [Fact]
    public void Open_places_the_first_tile_that_fits_the_deck_and_keeps_the_order_of_skipped_tiles()
    {
        var game = AtRing("lab", "corridor", "junction");   // the lab only fits Command
        game.Do(East);
        var corridor = game.Placed("corridor");
        Assert.Equal(new Cell(DeckId.Habitat, 1, 1), corridor.At);
        Assert.Equal([new TileId("lab"), new TileId("junction")], game.State.TilePile);
        Assert.Equal(corridor.Id, game.Crew(Play.S1).At);
        Assert.Equal(3, game.State.MovesLeft);
    }

    [Fact]
    public void Open_with_no_fitting_tile_seals_the_hatch_and_spends_nothing()
    {
        // Review focus 1.
        var game = AtRing("lab");
        var events = game.Do(East);
        Assert.Equal(new HatchSealed(Play.C(2), Side.E), Assert.Single(events));
        Assert.True(game.State.Ship.IsSealed(Play.C(2), Side.E));
        Assert.Equal(Play.C(2), game.Crew(Play.S1).At);
        Assert.Equal(4, game.State.MovesLeft);
        Assert.Equal(Play.S1, game.State.ActiveSeat);
        Assert.DoesNotContain(East, game.Legal(Play.S1));
    }

    [Fact]
    public void A_tile_with_several_rotations_waits_for_a_choice()
    {
        var game = AtRing("quarters");
        game.Do(East);
        var wait = Assert.IsType<OrientPending>(game.State.Waiting);
        Assert.Equal([1, 2, 3], wait.Rotations);
        Assert.Equal([new Orient(Play.S1, 1), new Orient(Play.S1, 2), new Orient(Play.S1, 3), new Timeout(Play.S1)],
                     game.Legal(Play.S1));
        game.Do(new Orient(Play.S1, 2));
        Assert.Equal(2, game.Placed("quarters").Rotation);
        Assert.Null(game.State.Waiting);
    }

    [Fact]
    public void Orient_timeout_takes_the_default_rotation()
    {
        // Review focus 2: most usable hatches, then the lowest rotation; never chance.
        var game = AtRing("quarters");
        game.Do(East);
        game.Do(new Timeout(Play.S1));
        var expected = Placement.DefaultRotation(Play.Game().State.Ship, game.Pack.Tile(new TileId("quarters")),
                                                 new Cell(DeckId.Habitat, 1, 1), Side.W, game.Pack.Rules.DeckRadius);
        Assert.Equal(expected, game.Placed("quarters").Rotation);
    }

    [Fact]
    public void Nobody_else_can_act_while_a_rotation_is_chosen()
    {
        var game = AtRing("quarters");
        game.Do(East);
        Assert.Empty(game.Legal(Play.S2));
        Assert.DoesNotContain(game.Legal(Play.S1), c => c is EndTurn or MoveTo);
    }

    [Fact]
    public void A_symbol_tile_ends_movement()
    {
        var game = AtRing("storage");
        game.Do(East);
        Assert.Equal(0, game.State.MovesLeft);
        Assert.Equal(game.Placed("storage").Id, game.Crew(Play.S1).At);
    }

    [Fact]
    public void Saved_while_orienting_then_restored_accepts_the_same_orient()
    {
        // Review focus 6.
        var game = AtRing("quarters");
        game.Do(East);
        var restored = Json.RoundTrip(game.State);
        var a = Rules.Step(game.Pack, game.State, new Orient(Play.S1, 3));
        var b = Rules.Step(game.Pack, restored, new Orient(Play.S1, 3));
        Assert.Equal(a.State, b.State);
        Assert.Equal(a.Events, b.Events);
    }
}
