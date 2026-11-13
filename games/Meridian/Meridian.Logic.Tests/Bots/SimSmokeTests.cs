using Meridian.Sim;

namespace Meridian.Logic.Tests.Bots;

public sealed class SimSmokeTests
{
    [Fact]
    [Trait("Category", "Sim")]
    public void Explore_runs_twenty_games_and_reports_every_player_count()
    {
        var games = ExploreReport.Run(BasePack.Pack, new ExploreOptions(Games: 20, MinPlayers: 3, MaxPlayers: 6, Seed: 1));
        Assert.Equal(80, games.Count);
        Assert.All(games, g => Assert.True(g.Reached));
        var report = ExploreReport.Format(BasePack.Pack, games);
        foreach (var players in new[] { 3, 4, 5, 6 })
        {
            Assert.Contains($"| {players} | 20 |", report);
        }
    }
}
