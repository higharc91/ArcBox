namespace Meridian.Logic.Tests.Ship;

public sealed class ShipBuilderTests
{
    private static readonly RulesPack Pack = TestPack.Load();

    [Fact]
    public void Start_has_three_decks_joined_by_the_lift()
    {
        var ship = ShipBuilder.Start(Pack);
        Assert.Equal(5, ship.Compartments.Count);
        Assert.Equal(Enum.GetValues<DeckId>().Order(), ship.Compartments.Select(c => c.At.Deck).Distinct().Order());
        var lift = ship.Compartments.Single(c => c.Tile == new TileId("central_lift"));
        Assert.Equal([CompartmentId.Numbered(4), CompartmentId.Numbered(5)], ship.Linked(lift.Id).Order().ToArray());
        Assert.Contains(lift.Id, ship.Linked(CompartmentId.Numbered(4)));
        Assert.Empty(ship.Sealed);
    }

    [Fact]
    public void The_entry_is_the_airlock()
        => Assert.Equal(new TileId("airlock"), ShipBuilder.Start(Pack).Get(ShipBuilder.Entry(Pack, ShipBuilder.Start(Pack))).Tile);

    [Fact]
    public void The_frontier_is_every_open_hatch_onto_empty_space()
    {
        // Ring east and west, lift east and west, and four hatches on each landing.
        Assert.Equal(12, ShipBuilder.Start(Pack).Frontier(Pack.Rules.DeckRadius).Count());
    }

    [Fact]
    public void Compartment_ids_are_stable_across_a_json_round_trip()
    {
        var (ship, _) = Placement.Place(ShipBuilder.Start(Pack), Pack.Tile(new TileId("storage")),
                                        new Cell(DeckId.Habitat, 1, 1), 0, Pack.Rules.DeckRadius);
        var back = Json.RoundTrip(ship);
        Assert.Equal(ship, back);
        Assert.Contains("[\"habitat\",1,1]", Json.Write(ship));
    }
}
