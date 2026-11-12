using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class BackgroundPrivacyTests
{
    private static TacticsGameTests.Session NearFord()
    {
        var doc = TacticsDocument.Read(ExplorationTests.DocumentAt(null));
        var owner = doc.Members.Keys.Last();
        doc = doc with { Members = Ordinal.Map(doc.Members.Select(m => (m.Key, m.Value with
        { Character = m.Value.Character with { Background = m.Key == owner ? "soldier" : "sage", Name = m.Key == owner ? "Kara" : m.Value.Character.Name } }))) };
        var s = new TacticsGameTests.Session(document: doc.ToJson(), game: new TacticsGame(TacticsGameTests.Rules, lore: StoryTests.Lore,
            seed: StoryTests.SeedFor(StoryTests.FordChoice) with { At = StoryTests.NearSpot(StoryTests.FordChoice) }));
        s.Harness.StartCampaign(s.Lease);
        return s;
    }

    [Fact]
    public void Chapter_one_delivers_the_soldiers_secret_only_to_their_phone_and_remembers_dismissal()
    {
        var s = NearFord();
        var secret = StoryTests.Lore.Backgrounds["soldier"].Secret!;
        Assert.Equal(secret, s.Screen(3)!.Data.GetProperty("backgroundSecret").GetProperty("text").GetString());
        for (var i = 0; i < 3; i++) Assert.False(s.Screen(i)!.Data.TryGetProperty("backgroundSecret", out _));
        Assert.DoesNotContain("backgroundSecret", JsonSerializer.Serialize(s.Host, ArcJson.Options));
        var portal = new TacticsCampaignRules(TacticsGameTests.Rules);
        var creator = portal.PortalView(s.Lease.Info, portal.NewDocument(s.Lease.Info), s.Profiles[0]!.Value);
        var hook = creator.GetProperty("details").GetProperty("backgrounds").GetProperty("soldier").GetProperty("hook").GetString();
        Assert.Equal(StoryTests.Lore.Backgrounds["soldier"].Hook, hook);
        Assert.DoesNotContain(JsonSerializer.Serialize(secret, ArcJson.Options).Trim('"'), creator.GetRawText());
        s.Send(0, "backgroundSecretRead"); // Other profiles cannot dismiss Kara's secret.
        Assert.Equal(secret, s.Screen(3)!.Data.GetProperty("backgroundSecret").GetProperty("text").GetString());
        s.Send(3, "backgroundSecretRead");
        Assert.False(s.Screen(3)!.Data.TryGetProperty("backgroundSecret", out _));
        var restarted = new TacticsGameTests.Session(document: s.Game.Document.ToJson(), game: new TacticsGame(TacticsGameTests.Rules, lore: StoryTests.Lore));
        restarted.Harness.Resume(restarted.Lease);
        for (var i = 0; i < 4; i++) restarted.Harness.SetConnected(i, true);
        Assert.False(restarted.Screen(3)!.Data.TryGetProperty("backgroundSecret", out _));
    }

    [Fact]
    public void Personal_options_credit_the_character_on_both_screens_and_only_the_owner_can_start_the_choice()
    {
        var s = NearFord(); var lead = StageBattleTests.Lead(s); Assert.NotEqual(3, lead);
        s.Send(lead, "interact");
        StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the ford question");
        var option = Assert.Single(s.Host.Story!.Ask!.Options, o => o.Id == "truth");
        Assert.Equal("Soldier · Kara", option.Credit); Assert.Contains("Kara", option.Label);
        var own = s.Screen(3)!.Data; var leader = s.Screen(lead)!.Data;
        Assert.True(own.GetProperty("canChoose").GetBoolean()); Assert.True(own.GetProperty("personal").GetBoolean());
        Assert.Equal("Soldier · Kara", own.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetString() == "truth").GetProperty("credit").GetString());
        Assert.False(leader.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetString() == "truth").GetProperty("can").GetBoolean());
        var before = s.Game.Document.ToJson().GetRawText();
        s.Send(lead, "vote", new { option = "truth" });
        Assert.Equal(before, s.Game.Document.ToJson().GetRawText());
        s.Send(3, "vote", new { option = "why" }); // Kara cannot choose the leader's ordinary answers.
        Assert.Equal(before, s.Game.Document.ToJson().GetRawText());
        s.Send(3, "vote", new { option = "truth" });
        var agreed = s.Screen(lead)!.Data.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetString() == "truth");
        Assert.True(agreed.GetProperty("can").GetBoolean());
        s = new TacticsGameTests.Session(document: s.Game.Document.ToJson(), game: new TacticsGame(TacticsGameTests.Rules, lore: StoryTests.Lore));
        s.Harness.Resume(s.Lease);
        for (var i = 0; i < 4; i++) s.Harness.SetConnected(i, true);
        lead = StageBattleTests.Lead(s);
        // StorySave deliberately resets all votes after restart: the owner must agree again.
        Assert.False(s.Screen(lead)!.Data.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetString() == "truth").GetProperty("can").GetBoolean());
        s.Send(lead, "vote", new { option = "truth" });
        Assert.False(s.Screen(lead)!.Data.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetString() == "truth").GetProperty("can").GetBoolean());
        s.Send(3, "vote", new { option = "truth" });
        Assert.True(s.Screen(lead)!.Data.GetProperty("options").EnumerateArray().Single(o => o.GetProperty("id").GetString() == "truth").GetProperty("can").GetBoolean());
        s.Send(lead, "vote", new { option = "truth" }); // Only after Kara's explicit choice.
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Story!.Node == "truth", "Kara's personal answer");
    }

    [Fact]
    public void Private_unlock_flags_are_respected_and_public_hooks_match_lore_without_copying_secrets()
    {
        var rules = TacticsGameTests.Rules;
        foreach (var background in StoryTests.Lore.Backgrounds.Values)
        {
            Assert.Equal(background.Hook, rules.Backgrounds[background.Id].Hook);
            if (background.Secret is { } secret) Assert.DoesNotContain(secret, JsonSerializer.Serialize(rules.Backgrounds[background.Id], ArcJson.Options));
        }
        var s = NearFord();
        Assert.False(s.Screen(0)!.Data.TryGetProperty("backgroundSecret", out _)); // Sage's later unlock has not occurred.
    }

    [Fact]
    public void Background_phone_fixtures_are_actual_private_and_credited_views()
    {
        var s = NearFord();
        var screens = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal)
        { ["walk-background-secret"] = JsonNode.Parse(JsonSerializer.Serialize(s.Screen(3), ArcJson.Options))! };
        s.Send(StageBattleTests.Lead(s), "interact");
        StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Asking, "the ford question");
        screens["vote-background-owner"] = JsonNode.Parse(JsonSerializer.Serialize(s.Screen(3), ArcJson.Options))!;
        screens["vote-background-leader"] = JsonNode.Parse(JsonSerializer.Serialize(s.Screen(StageBattleTests.Lead(s)), ArcJson.Options))!;
        var portal = new TacticsCampaignRules(TacticsGameTests.Rules);
        screens["portal-background-creator"] = JsonNode.Parse(portal.PortalView(s.Lease.Info, portal.NewDocument(s.Lease.Info), s.Profiles[0]!.Value).GetRawText())!;
        PhoneFixtureTests.Check(screens);
    }
}
