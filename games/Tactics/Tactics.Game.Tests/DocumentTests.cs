using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary>What <see cref="TacticsDocument"/> reads and writes, across versions.</summary>
public sealed class DocumentTests
{
    private const string Version0 = """{ "version": 0, "rulesHash": "h", "partyLevel": 2, "members": {}, "completed": ["river_crossing"], "battle": null }""";

    private static readonly ProfileId Ana = new("pr_ana");

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement;

    /// <summary>The document as a build from before the world saved it.</summary>
    private static JsonElement AsVersion0(JsonElement document)
    {
        var node = JsonNode.Parse(document.GetRawText())!.AsObject();
        node["version"] = 0;
        node.Remove("world");
        return Json(node.ToJsonString());
    }

    [Fact]
    public void A_new_document_is_the_current_version()
    {
        Assert.Equal(1, TacticsDocument.CurrentVersion);
        Assert.Equal(1, TacticsDocument.New.Version);
        Assert.Null(TacticsDocument.New.World);
    }

    [Fact]
    public void A_version_0_document_reads_with_no_world()
    {
        var doc = TacticsDocument.Read(Json(Version0));

        Assert.Null(doc.World);
        Assert.Equal(2, doc.PartyLevel);
        Assert.Equal(["river_crossing"], doc.Completed);
    }

    [Fact]
    public void A_version_0_document_is_written_back_as_the_current_version()
        => Assert.Equal(TacticsDocument.CurrentVersion, TacticsDocument.Read(TacticsDocument.Read(Json(Version0)).ToJson()).Version);

    [Fact]
    public void A_version_0_document_keeps_its_battle()
    {
        var saved = new TacticsGameTests.Session();
        saved.ToBattle(everyoneAway: true);
        var old = AsVersion0(saved.Lease.Document);

        var resumed = new TacticsGameTests.Session(document: old);
        resumed.Harness.Resume(resumed.Lease);

        Assert.Null(resumed.Host.Notice);
        Assert.Equal(TacticsPhase.Battle, resumed.Game.Phase);
        Assert.Equal(
            JsonSerializer.Serialize(TacticsDocument.Read(old).Battle, ArcJson.Options),
            JsonSerializer.Serialize(TacticsDocument.Read(saved.Lease.Document).Battle, ArcJson.Options));
    }

    [Fact]
    public void A_saved_world_round_trips()
    {
        var world = new WorldSave("ashen_ford", new Tile(44, 57), new Tile(45, 57), "pr_ana", ["ch1_met_maud"], ["ch1_the_ember", "ch1_old_maud"]);

        var read = TacticsDocument.Read((TacticsDocument.New with { World = world }).ToJson()).World!;

        Assert.Equal(("ashen_ford", new Tile(44, 57), new Tile(45, 57), "pr_ana"), (read.Region, read.At, read.Facing, read.LastLeader));
        Assert.Equal(["ch1_met_maud"], read.Flags);
        Assert.Equal(["ch1_the_ember", "ch1_old_maud"], read.Beats);
    }

    [Fact]
    public void A_saved_world_with_nothing_said_has_empty_lists()
    {
        var read = TacticsDocument.Read(Json("""{ "version": 1, "partyLevel": 1, "members": {}, "completed": [], "world": { "region": "r", "at": [1, 2], "facing": [1, 3] } }""")).World!;

        Assert.Empty(read.Flags);
        Assert.Empty(read.Beats);
        Assert.Null(read.LastLeader);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-1)]
    public void An_unknown_version_is_still_refused(int version)
        => Assert.Throws<JsonException>(() => TacticsDocument.Read(Json($$"""{ "version": {{version}}, "partyLevel": 1, "members": {}, "completed": [] }""")));

    [Fact]
    public void The_portal_reads_a_version_0_document()
    {
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", Ana, [new CampaignMember(Ana, "Ana", CampaignRole.Owner, DateTimeOffset.UnixEpoch)], 1);
        var rules = new TacticsCampaignRules(TacticsGameTests.Rules);

        Assert.Equal("No character yet", rules.CardFor(info, Json(Version0), Ana).Subtitle);
        Assert.False(rules.PortalView(info, Json(Version0), Ana).TryGetProperty("problem", out _));
    }

    [Fact]
    public void Walking_has_its_own_clocks_and_old_timings_still_get_them()
    {
        var timing = new TacticsTiming(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);

        Assert.Equal(WalkTiming.Default, timing.Walk);
        Assert.Equal(WalkTiming.Default, TacticsTiming.Default.Walk);
        Assert.Equal(
            new WalkTiming(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(750), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15)),
            WalkTiming.Default);
    }
}
