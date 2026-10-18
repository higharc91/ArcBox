using ArcBox.Server.Games;
using Counter.Logic;
using Xunit;

namespace ArcBox.Server.Tests;

public class GameRegistryTests
{
    [Fact]
    public void Registry_lists_manifests_and_creates_fresh_instances()
    {
        var r = new GameRegistry([new(() => new CounterGame())]);
        Assert.Equal("counter", r.Manifests.Single().Id);
        Assert.True(r.Contains("counter"));
        Assert.False(r.Contains("nope"));
        Assert.NotSame(r.Create("counter"), r.Create("counter"));
        Assert.Throws<KeyNotFoundException>(() => r.Create("nope"));
    }
}
