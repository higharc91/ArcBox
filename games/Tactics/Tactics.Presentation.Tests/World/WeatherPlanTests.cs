using Tactics.Presentation.Tests.Board;
using Tactics.Presentation.World;

namespace Tactics.Presentation.Tests.World;

public sealed class WeatherPlanTests
{
    private static readonly AmbientManifest Manifest = AmbientManifest.Load(Pack.Root)!;

    [Theory]
    [InlineData("rain")]
    [InlineData("storm")]
    [InlineData("drizzle")]
    public void Rain_uses_the_authored_variant_and_loops_at_the_manifest_rate(string weather)
    {
        var asset = Manifest.Assets["rain"];
        var first = WeatherPlan.Of(weather, Manifest, 0)!;
        Assert.Equal(asset.Variants[weather], first.Path);
        Assert.True(File.Exists(Path.Combine(Pack.Root, "ambient", first.Path)));
        Assert.Equal(0, first.Frame);
        Assert.Equal(1, WeatherPlan.Of(weather, Manifest, 1d / asset.Fps)!.Frame);
        Assert.Equal(first, WeatherPlan.Of(weather, Manifest, (double)asset.Frames / asset.Fps));
        Assert.Equal(128, first.Width);
        Assert.Equal(128, first.Height);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("snow")]
    [InlineData("clear")]
    public void Dry_or_unsupported_weather_does_not_show_rain(string? weather)
        => Assert.Null(WeatherPlan.Of(weather, Manifest, 2));

    [Fact]
    public void Missing_optional_art_is_safe() => Assert.Null(WeatherPlan.Of("rain", null, 2));
}
