using System.Numerics;
using Tactics.Presentation.Board;
using Tactics.Presentation.World;
using Tactics.Presentation.Tests.Board;

namespace Tactics.Presentation.Tests.World;

public sealed class WorldTextureBudgetTests
{
    [Fact]
    public void The_gate_rejects_over_budget_missing_measurements_and_small_parties()
    {
        Assert.True(WorldTextureBudget.Passes(512L * 1024 * 1024, 4));
        Assert.False(WorldTextureBudget.Passes(512L * 1024 * 1024 + 1, 4));
        foreach (var bytes in new[] { 0, -1, double.NaN, double.PositiveInfinity })
            Assert.False(WorldTextureBudget.Passes(bytes, 4));
        foreach (var members in new[] { 0, 1, 3, 5 })
            Assert.False(WorldTextureBudget.Passes(200L * 1024 * 1024, members));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Perf_party_draws_four_distinct_real_hero_atlases_with_bars(bool changed)
    {
        var art = UnitArt.Load(Pack.Root);
        var ground = DemoRegion.FromPack(Pack.Root, "ashen_ford");
        var terrain = new BoardTerrain(ground.Map, null);
        var party = DemoRegion.PerfParty(terrain, art, new UnitPlace(new Vector2(8, 8), 0), Vector2.UnitX, 1, changed ? WorldTextureBudget.ChangedParty : WorldTextureBudget.Party);
        Assert.Equal(4, party.Length);
        Assert.Equal(4, party.Select(u => u.Id).Distinct().Count());
        Assert.Equal(4, party.Select(u => u.Sprite.Path).Distinct().Count());
        Assert.All(party, unit =>
        {
            Assert.StartsWith("tactical/", unit.Sprite.Path);
            Assert.NotNull(unit.Bar);
        });
    }
}
