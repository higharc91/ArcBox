using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class RegionPeopleTests
{
    [Fact]
    public void Existing_people_keep_their_exact_json_and_legacy_identity()
    {
        const string json = "{\"preset\":\"guard\",\"at\":[4,5],\"face\":\"sw\"}";
        var person = JsonSerializer.Deserialize<RegionPerson>(json, ArcJson.Options)!;
        Assert.Equal(json, JsonSerializer.Serialize(person, ArcJson.Options));
        Assert.Equal("person:7:guard", RegionPeople.Id(person, 7));
        Assert.True(RegionPeople.Present(person, ImmutableHashSet<string>.Empty));
    }

    [Fact]
    public void All_required_flags_and_no_excluded_flags_are_needed()
    {
        var person = new RegionPerson("guard", new Tile(4, 5), "sw", Id: "coll", Character: "dagna_holt",
            Requires: ["met", "ready"], Unless: ["gone"]);
        Assert.False(RegionPeople.Present(person, new HashSet<string> { "met" }));
        Assert.True(RegionPeople.Present(person, new HashSet<string> { "met", "ready" }));
        Assert.False(RegionPeople.Present(person, new HashSet<string> { "met", "ready", "gone" }));
        Assert.Equal("coll", RegionPeople.Id(person, 7));
        var json = JsonSerializer.Serialize(person, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<RegionPerson>(json, ArcJson.Options), ArcJson.Options));
    }

    [Theory]
    [InlineData("ch1_skarrow_slain", "ch1_ford_assault")]
    [InlineData("ch1_skarrow_spared", "ch1_river_crossing")]
    public void A_committed_ford_choice_is_not_yet_a_successful_ending(string flag, string beat)
    {
        var committed = new HashSet<string> { flag, "sq_forge_asked" };
        Assert.DoesNotContain(flag, RegionPeople.PresenceFlags(committed, ImmutableHashSet<string>.Empty));
        Assert.DoesNotContain(flag, RegionPeople.PresenceFlags(committed, new HashSet<string> { "ch1_the_ford_choice" }));
        var won = RegionPeople.PresenceFlags(committed, new HashSet<string> { beat });
        Assert.Contains(flag, won);
        Assert.Contains("sq_forge_asked", won);
        Assert.Contains(flag, committed); // Presence never rewrites the saved story choice.
    }
}
