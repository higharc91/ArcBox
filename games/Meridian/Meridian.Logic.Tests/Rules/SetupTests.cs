namespace Meridian.Logic.Tests.Turns;

public sealed class SetupTests
{
    private static readonly RulesPack Pack = TestPack.Load();
    private static readonly (SeatId, CrewId)[] Seats =
        [(Play.S1, new("ivo")), (Play.S2, new("mara")), (Play.S3, new("dex"))];

    [Fact]
    public void NewGame_is_deterministic_for_a_seed()
        => Assert.Equal(Rules.NewGame(Pack, Seats, 11), Rules.NewGame(Pack, Seats, 11));

    [Fact]
    public void Different_seeds_give_different_tile_orders()
    {
        var orders = Enumerable.Range(1, 8).Select(s => Rules.NewGame(Pack, Seats, (ulong)s).TilePile).Distinct();
        Assert.True(orders.Count() > 1);
    }

    [Fact]
    public void Everyone_starts_in_the_airlock_on_their_starting_steps()
    {
        var state = Rules.NewGame(Pack, Seats, 1);
        var airlock = state.Ship.Compartments.Single(c => c.Tile == new TileId("airlock")).Id;
        Assert.All(state.Crew, c => Assert.Equal(airlock, c.At));
        Assert.All(state.Crew, c => Assert.Equal(new TraitSteps(4, 4, 4, 4), c.Steps));
        Assert.Equal(Phase.Explore, state.Phase);
        Assert.Null(state.Waiting);
        Assert.Equal(1, state.Round);
    }

    [Fact]
    public void Every_tile_copy_and_card_is_in_a_pile()
    {
        var state = Rules.NewGame(Pack, Seats, 1);
        Assert.Equal(Pack.Tiles.Sum(t => t.Copies), state.TilePile.Count);
        Assert.Single(state.Incidents.Draw);
        Assert.Single(state.Salvage.Draw);
        Assert.Single(state.Anomalies.Draw);
    }

    [Fact]
    public void The_first_seat_is_random_and_moves_by_agility()
    {
        var firsts = Enumerable.Range(1, 30).Select(s => Rules.NewGame(Pack, Seats, (ulong)s)).ToList();
        Assert.True(firsts.Select(s => s.Active).Distinct().Count() > 1);
        Assert.All(firsts, s => Assert.Equal(Rules.Value(Pack, s.ActiveCrew, Trait.Agility), s.MovesLeft));
        Assert.All(firsts, s => Assert.Equal(s.First, s.Active));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(7)]
    public void Player_counts_outside_the_rules_are_a_bug(int count)
    {
        var seats = Enumerable.Range(0, count).Select(i => (new SeatId($"s{i}"), new CrewId("ivo"))).ToList();
        Assert.Throws<ArgumentException>(() => Rules.NewGame(Pack, seats, 1));
    }

    [Fact]
    public void The_same_crew_member_twice_is_a_bug()
        => Assert.Throws<ArgumentException>(() => Rules.NewGame(Pack,
            [(Play.S1, new("ivo")), (Play.S2, new("ivo")), (Play.S3, new("dex"))], 1));
}
