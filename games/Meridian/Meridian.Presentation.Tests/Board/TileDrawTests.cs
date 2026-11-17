using Meridian.Presentation.Board;

namespace Meridian.Presentation.Tests.Board;

public sealed class TileDrawTests
{
    private static CompartmentCard Room(string id, int x, int y, int rotation, EqArray<Side> hatches, EqArray<Side>? sealedSides = null)
        => new(id, "storage_bay", null, new Cell(DeckId.Habitat, x, y), rotation, hatches, sealedSides, null);

    [Fact]
    public void Each_side_is_open_closed_sealed_or_a_wall()
    {
        var a = Room("a", 0, 0, 0, [Side.N, Side.E, Side.S]);
        var b = Room("b", 1, 0, 0, [Side.W]);            // east of a, facing back: open
        var c = Room("c", 0, 1, 0, [Side.E], [Side.N]);   // south of a, sealed on both sides by placement
        var draw = TileDraw.Of([a, b, c], DeckId.Habitat).Single(t => t.Id == "a");
        var sealedA = Room("a", 0, 0, 0, [Side.N, Side.E, Side.S], [Side.S]);
        var drawSealed = TileDraw.Of([sealedA, b, c], DeckId.Habitat).Single(t => t.Id == "a");
        Assert.Equal(HatchState.Closed, draw.Hatch(Side.N));   // nothing placed beyond yet
        Assert.Equal(HatchState.Open, draw.Hatch(Side.E));
        Assert.Equal(HatchState.None, draw.Hatch(Side.W));
        Assert.Equal(HatchState.Sealed, drawSealed.Hatch(Side.S));
    }

    [Fact]
    public void Every_tile_at_every_rotation_draws_exactly_the_hatches_the_rules_give_it()
    {
        // Review focus 6: what the TV draws as a doorway is what the rules let you walk through.
        foreach (var tile in BasePack.Pack.Tiles.Concat(BasePack.Pack.StartTiles))
        {
            for (var r = 0; r < 4; r++)
            {
                var room = new CompartmentCard("x", tile.Id.Value, null, new Cell(DeckId.Habitat, 0, 0), r, tile.HatchesAt(r), null, null);
                var draw = TileDraw.Of([room], DeckId.Habitat).Single();
                var drawn = Sides.All.Where(s => draw.Hatch(s) != HatchState.None).ToHashSet();
                Assert.True(drawn.SetEquals(tile.HatchesAt(r)), $"{tile.Id} at {r}: drew {string.Join(",", drawn)}");
                Assert.Equal(r * 90, draw.Degrees);
            }
        }
    }

    [Theory]
    [InlineData(Side.N, 0)]
    [InlineData(Side.E, 90)]
    [InlineData(Side.S, 180)]
    [InlineData(Side.W, 270)]
    public void A_hatch_overlay_drawn_for_the_north_edge_is_turned_to_its_side(Side side, int degrees)
        => Assert.Equal(degrees, TileDraw.OverlayDegrees(side));

    [Fact]
    public void Only_the_asked_deck_is_drawn_and_the_presented_room_is_lit()
    {
        var a = Room("a", 0, 0, 0, [Side.N]);
        var b = new CompartmentCard("b", "bridge", null, new Cell(DeckId.Command, 0, 0), 0, [Side.S], null, CardKind.Anomaly);
        var draws = TileDraw.Of([a, b], DeckId.Command, lit: "b");
        var only = Assert.Single(draws);
        Assert.True(only.Lit);
        Assert.Equal(CardKind.Anomaly, only.Symbol);
    }
}
