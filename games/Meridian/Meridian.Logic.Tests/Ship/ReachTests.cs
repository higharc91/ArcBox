namespace Meridian.Logic.Tests.Ship;

public sealed class ReachTests
{
    private static readonly RulesPack Pack = TestPack.Load();

    [Fact]
    public void Reach_follows_hatches_and_the_lift()
    {
        var ship = ShipBuilder.Start(Pack);
        var reach = Reach.From(Pack, ship, Play.C(1), 4);
        Assert.Equal(0, reach[Play.C(1)]);
        Assert.Equal(1, reach[Play.C(2)]);
        Assert.Equal(2, reach[Play.C(3)]);
        Assert.Equal(3, reach[Play.C(4)]);
        Assert.Equal(3, reach[Play.C(5)]);
    }

    [Fact]
    public void Reach_stops_at_the_budget()
    {
        var reach = Reach.From(Pack, ShipBuilder.Start(Pack), Play.C(1), 2);
        Assert.False(reach.ContainsKey(Play.C(4)));
        Assert.True(reach.ContainsKey(Play.C(3)));
    }

    [Fact]
    public void Leaving_a_compartment_with_an_exit_cost_costs_more()
    {
        var files = TestPack.Files();
        files[PackLoader.TilesFile]["startTiles"]![1]!["exitCost"] = 2;   // the docking ring
        var pack = files.Load();
        var reach = Reach.From(pack, ShipBuilder.Start(pack), Play.C(1), 10);
        Assert.Equal(1, reach[Play.C(2)]);
        Assert.Equal(4, reach[Play.C(3)]);   // 1 into the ring, then 1 + 2 to leave it
    }
}
