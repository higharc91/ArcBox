namespace ArcBox.Host.Core.Tests;

public class RenderPresetsTests
{
    [Theory]
    [InlineData("ultra", "high")]
    [InlineData("high", "low")]
    [InlineData("low", "ultra")]
    [InlineData("HIGH", "low")]
    [InlineData("bogus", "high")]   // an unknown name counts as the default (Ultra)
    public void Next_cycles_ultra_high_low(string current, string next)
    {
        Assert.Equal(next, RenderPresets.Next(current));
    }

    [Fact]
    public void Three_presses_come_back_round()
    {
        var p = RenderPresets.Ultra;
        var seen = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            p = RenderPresets.Next(p);
            seen.Add(p);
        }
        Assert.Equal(["high", "low", "ultra"], seen);
    }

    [Theory]
    [InlineData("low", "high", "low")]       // the --preset argument wins over the saved choice
    [InlineData(null, "high", "high")]       // else the saved choice
    [InlineData(null, "High", "high")]
    [InlineData(null, null, "ultra")]        // else Ultra
    [InlineData(null, "medium", "ultra")]    // a saved name we don't know is ignored
    [InlineData("nope", "low", "low")]
    public void Resolve_prefers_the_argument_then_the_saved_choice(string? arg, string? saved, string expected)
    {
        Assert.Equal(expected, RenderPresets.Resolve(arg, saved));
    }

    [Fact]
    public void Title_is_upper_case()
    {
        Assert.Equal("HIGH", RenderPresets.Title("high"));
    }
}
