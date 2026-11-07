using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class EncounterGuidanceTests
{
    [Theory]
    [InlineData(1, "river_crossing", "Recommended")]
    [InlineData(1, "forest_ruins", "Higher level")]
    [InlineData(3, "river_crossing", "Lower level")]
    [InlineData(3, "forest_ruins", "Recommended")]
    public void Host_and_phone_compare_encounters_with_the_campaign_level(int level, string encounter, string guidance)
    {
        var session = new TacticsGameTests.Session(document: (TacticsDocument.New with { PartyLevel = level }).ToJson());
        session.ToWalking();
        session.Send(0, "battles");

        var host = Assert.Single(session.Host.Encounters!.Value, e => e.Id == encounter);
        Assert.Equal(guidance, host.Guidance);
        Assert.Equal(level, session.Screen(0)!.Data.GetProperty("partyLevel").GetInt32());
        var phone = session.Screen(0)!.Data.GetProperty("encounters").EnumerateArray().Single(e => e.GetProperty("id").GetString() == encounter);
        Assert.Equal(guidance, phone.GetProperty("guidance").GetString());
        Assert.Equal(host.UnavailableReason, phone.GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public void A_disabled_encounter_explains_its_limit_even_after_a_host_view_round_trip()
    {
        var option = new EncounterOption("small", "Small battle", 3, 1, 2, false, Guidance: "Recommended");
        var json = JsonSerializer.SerializeToElement(option, ArcJson.Options);
        var read = json.Deserialize<EncounterOption>(ArcJson.Options)!;

        Assert.Equal("Party too big (max 2)", read.UnavailableReason);
        Assert.Equal("Recommended", read.Guidance);
        Assert.False(read.Fits);
    }
}
