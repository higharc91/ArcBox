using ArcBox.Server.Games;
using Counter.Logic;
using Microsoft.Extensions.DependencyInjection;
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

    [Fact]
    public void Factory_overload_resolves_dependencies_per_creation()
    {
        var services = new ServiceCollection();
        var calls = 0;
        services.AddSingleton(new object());
        services.AddArcGame(sp => { Assert.NotNull(sp.GetRequiredService<object>()); calls++; return new CounterGame(); });
        services.AddSingleton<IGameRegistry, GameRegistry>();
        var registry = services.BuildServiceProvider().GetRequiredService<IGameRegistry>();
        Assert.Equal("counter", registry.Manifests.Single().Id);
        registry.Create("counter");
        Assert.Equal(2, calls);      // one manifest probe + one Create
    }
}
