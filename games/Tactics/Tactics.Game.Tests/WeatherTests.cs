using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class WeatherTests
{
    [Theory]
    [InlineData("rain")]
    [InlineData("storm")]
    [InlineData("drizzle")]
    public void Authored_region_weather_reaches_the_host_and_survives_resume(string weather)
    {
        var dir = Directory.CreateTempSubdirectory("tactics-weather-");
        try
        {
            Directory.CreateDirectory(Path.Combine(dir.FullName, "world"));
            var region = new RegionDef("rainy_bank", "Rainy bank", 1, [8, 8], ["grass/lush"],
                [new RegionChunk(0, 0, Convert.ToBase64String(new byte[32 * 32 * 3]))], [], [],
                new RegionSpawns(new Tile(1, 1), new Tile(2, 1)), [], Weather: weather);
            File.WriteAllText(Path.Combine(dir.FullName, "world", "rainy_bank.json"), JsonSerializer.Serialize(region, ArcJson.Options));
            File.WriteAllText(Path.Combine(dir.FullName, "manifest.json"), JsonSerializer.Serialize(new
            {
                id = "weather_test", rulesVersion = 1, requires = new[] { "base" }, files = new[] { "world/rainy_bank.json" },
            }, ArcJson.Options));
            var rules = RulesPack.Load([Path.Combine(ContentBase(), "rules"), dir.FullName], ArtManifests.Load(ContentBase()));
            var session = Vault.At(new Tile(2, 2), rules, world => world with { Region = "rainy_bank" });
            Assert.Equal(weather, session.Host.Explore!.Weather);
            var resumed = new Session(document: session.Game.Document.ToJson(), game: new TacticsGame(rules));
            resumed.Harness.Resume(resumed.Lease);
            Assert.Equal(weather, resumed.Host.Explore!.Weather);
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Existing_regions_stay_dry()
    {
        var session = new Session();
        session.ToWalking();
        Assert.Null(session.Host.Explore!.Weather);
    }
}
