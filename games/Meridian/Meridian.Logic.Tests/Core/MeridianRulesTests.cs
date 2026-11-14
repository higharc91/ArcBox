namespace Meridian.Logic.Tests.Core;

public sealed class MeridianRulesTests
{
    [Fact]
    public void Defaults_match_the_spec()
    {
        var rules = new MeridianRules();
        Assert.Equal([0, 0, 1, 1, 1, 2], rules.DieFaces);
        Assert.Equal(8, rules.TrackSteps);
        Assert.Equal(6, rules.BreachThreshold);
        Assert.Equal(9, rules.ForcedBreachAnomaly);
        Assert.Equal(3, rules.MinPlayers);
        Assert.Equal(6, rules.MaxPlayers);
        Assert.Equal(TimeSpan.FromSeconds(8), rules.RotationChoice);
        Assert.Equal(TimeSpan.FromSeconds(60), rules.TurnInactivity);
    }

    [Fact]
    public void Round_trips_through_json()
    {
        var rules = new MeridianRules { BreachThreshold = 4 };
        Assert.Equal(rules, Json.RoundTrip(rules));
    }
}
