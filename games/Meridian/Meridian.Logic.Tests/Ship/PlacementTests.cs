namespace Meridian.Logic.Tests.Ship;

// The test pack's start layout on Habitat: lift c3 at (0,0) [e,s,w], ring c2 at (0,1) [n,e,s,w], airlock c1 at (0,2) [n].
public sealed class PlacementTests
{
    private static readonly RulesPack Pack = TestPack.Load();
    private static readonly int Radius = Pack.Rules.DeckRadius;
    private static TileDef T(string id) => Pack.Tile(new TileId(id));
    private static Cell H(int x, int y) => new(DeckId.Habitat, x, y);
    private static readonly ShipState Start = ShipBuilder.Start(Pack);

    [Fact]
    public void A_one_hatch_tile_has_one_rotation_facing_back()
        => Assert.Equal([2], Placement.ValidRotations(T("storage"), Side.N));

    [Fact]
    public void A_straight_tile_offers_its_two_identical_rotations_once()
        => Assert.Equal([0], Placement.ValidRotations(T("corridor"), Side.N));

    [Fact]
    public void A_corner_tile_offers_both_ways_round()
        => Assert.Equal([2, 3], Placement.ValidRotations(T("lab"), Side.N));

    [Fact]
    public void A_four_way_tile_offers_one_rotation()
        => Assert.Equal([0], Placement.ValidRotations(T("junction"), Side.W));

    [Fact]
    public void Place_seals_a_neighbours_hatch_that_faces_the_new_wall()
    {
        // Storage [s] east of the ring: the ring's east hatch now faces storage's west wall.
        var (ship, placed) = Placement.Place(Start, T("storage"), H(1, 1), 0, Radius);
        Assert.Equal(CompartmentId.Numbered(6), placed.Id);
        Assert.True(ship.IsSealed(CompartmentId.Numbered(2), Side.E));
        Assert.False(ship.IsSealed(placed.Id, Side.S));
    }

    [Fact]
    public void Place_seals_the_new_tiles_hatch_that_faces_a_wall()
    {
        var (ship, storage) = Placement.Place(Start, T("storage"), H(1, 1), 0, Radius);
        // Corridor [n,s] east of the lift: its south hatch faces storage's north wall, and the lift's east hatch its west wall.
        var (after, corridor) = Placement.Place(ship, T("corridor"), H(1, 0), 0, Radius);
        Assert.True(after.IsSealed(corridor.Id, Side.S));
        Assert.True(after.IsSealed(CompartmentId.Numbered(3), Side.E));
        Assert.False(after.IsSealed(storage.Id, Side.S));
    }

    [Fact]
    public void Place_seals_a_hatch_that_faces_the_deck_edge()
    {
        var (ship, corridor) = Placement.Place(Start, T("corridor"), H(3, -Radius), 0, Radius);
        Assert.True(ship.IsSealed(corridor.Id, Side.N));
        Assert.False(ship.IsSealed(corridor.Id, Side.S));
    }

    [Fact]
    public void Matching_hatches_join_and_are_not_sealed()
    {
        var (ship, corridor) = Placement.Place(Start, T("corridor"), H(1, 1), 1, Radius);   // [e,w], west meets the ring
        Assert.Empty(ship.Sealed);
        Assert.Contains(ship.Adjacent(ship.Get(CompartmentId.Numbered(2))), c => c.Id == corridor.Id);
    }

    [Fact]
    public void DefaultRotation_takes_the_lowest_when_usable_hatches_tie()
        => Assert.Equal(1, Placement.DefaultRotation(Start, T("lab"), H(1, 1), Side.W, Radius));

    [Fact]
    public void DefaultRotation_prefers_more_usable_hatches()
    {
        // A wall south of (1,1): rotation 1 [s,w] would point a hatch into it; rotation 2 [w,n] wouldn't.
        var (ship, _) = Placement.Place(Start, T("storage"), H(1, 2), 0, Radius);
        Assert.Equal(2, Placement.DefaultRotation(ship, T("lab"), H(1, 1), Side.W, Radius));
    }

    [Fact]
    public void A_drop_shaft_links_one_way_to_the_target_decks_first_compartment()
    {
        var shaft = T("corridor") with { Rule = new DropToRule(DeckId.Engineering) };
        var (ship, placed) = Placement.Place(Start, shaft, H(1, 1), 1, Radius);
        Assert.Contains(new Link(placed.Id, CompartmentId.Numbered(5), OneWay: true), ship.Links);
        Assert.Contains(CompartmentId.Numbered(5), ship.Linked(placed.Id));
        Assert.DoesNotContain(placed.Id, ship.Linked(CompartmentId.Numbered(5)));
    }

    [Fact]
    public void Placing_on_an_occupied_cell_is_a_bug_not_a_rule()
        => Assert.Throws<InvalidOperationException>(() => Placement.Place(Start, T("corridor"), H(0, 1), 0, Radius));
}
