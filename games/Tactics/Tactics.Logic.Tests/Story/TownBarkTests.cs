using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class TownBarkTests
{
    [Fact]
    public void Rotation_filters_before_counting_and_has_a_stable_first_match()
    {
        var set = new BarkSet("townsfolk_farmer", [
            new BarkLine("a", "Before", "idle", "level", [], ["won"]),
            new BarkLine("b", "Always", "idle", "level", [], []),
            new BarkLine("c", "After", "happy", "warm", ["won"], [])]);
        Assert.Equal("a", set.Pick(ImmutableHashSet<string>.Empty, 0)!.Id);
        Assert.Equal("b", set.Pick(ImmutableHashSet<string>.Empty, 1)!.Id);
        Assert.Equal("a", set.Pick(ImmutableHashSet<string>.Empty, 2)!.Id);
        Assert.Equal("b", set.Pick(ImmutableHashSet.Create("won"), 0)!.Id);
        Assert.Equal("c", set.Pick(ImmutableHashSet.Create("won"), 1)!.Id);
    }
}
