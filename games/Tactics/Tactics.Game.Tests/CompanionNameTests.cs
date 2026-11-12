using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class CompanionNameTests
{
    [Fact]
    public void Choosing_and_naming_are_atomic_private_and_clear_on_remake()
    {
        var rules = TacticsGameTests.Rules;
        var portal = new TacticsCampaignRules(rules);
        var owner = new ProfileId("mara");
        var other = new ProfileId("other");
        var info = new CampaignInfo("c", "ABCD", TacticsGame.Id, "Embers", owner,
            [new(owner, "Mara", CampaignRole.Owner, DateTimeOffset.UnixEpoch), new(other, "Other", CampaignRole.Player, DateTimeOffset.UnixEpoch)], 1);
        var character = SheetBuilder.DefaultCharacter("Mara", "human", "ranger", null, 3, rules);
        var doc = TacticsDocument.Read(portal.NewDocument(info)) with { Members = Ordinal.Map((owner.Value, new MemberRecord(character, 2000)), (other.Value, new MemberRecord(character with { Name = "Other" }, 2000))) };
        CommandResult Send(string action, object data, ProfileId? member = null) => portal.Handle(info, doc.ToJson(), member ?? owner, action, JsonSerializer.SerializeToElement(data, ArcJson.Options));
        var invalid = Send("chooseSubclass", new { subclass = "ranger.beastmaster", companionName = "Ash7" });
        Assert.Null(invalid.Document); Assert.NotNull(invalid.Error); Assert.Null(doc.For(owner)!.Character.Subclass);
        var valid = Send("chooseSubclass", new { subclass = "ranger.beastmaster", companionName = " Ash " });
        Assert.Null(valid.Error); doc = TacticsDocument.Read(valid.Document!.Value);
        Assert.Equal("Ash", doc.For(owner)!.Character.CompanionName);
        var view = portal.PortalView(info, doc.ToJson(), owner).GetProperty("me").GetProperty("companion");
        Assert.Equal("Ash", view.GetProperty("name").GetString()); Assert.Equal(8, view.GetProperty("suggestions").GetArrayLength());
        Assert.False(portal.PortalView(info, doc.ToJson(), other).GetProperty("me").TryGetProperty("companion", out _));
        Assert.NotNull(Send("nameCompanion", new { name = "Ember", member = owner.Value }, other).Error);
        Assert.NotNull(Send("nameCompanion", new { name = "Ember" }, other).Error);
        var battleSession = new TacticsGameTests.Session(); battleSession.ToBattle();
        var saved = doc;
        doc = doc with { Battle = battleSession.Game.Document.Battle };
        Assert.NotNull(Send("nameCompanion", new { name = "Ember" }).Error);
        doc = saved;
        var renamed = Send("nameCompanion", new { name = " Cinder " });
        Assert.Equal("Cinder", TacticsDocument.Read(renamed.Document!.Value).For(owner)!.Character.CompanionName);
        var cleared = Send("nameCompanion", new { name = "" });
        Assert.Null(TacticsDocument.Read(cleared.Document!.Value).For(owner)!.Character.CompanionName);
        var remade = Send("createCharacter", new { race = "human", @class = "fighter", background = "soldier", scores = character.BaseScores });
        Assert.Null(remade.Error); Assert.Null(TacticsDocument.Read(remade.Document!.Value).For(owner)!.Character.CompanionName);
    }
    [Fact]
    public void Companion_phone_fixtures_come_from_real_portal_and_battle_views()
    {
        var rules = TacticsGameTests.Rules;
        var seed = new TacticsGameTests.Session();
        var portal = new TacticsCampaignRules(rules);
        var doc = TacticsDocument.Read(seed.Lease.Document) with { PartyLevel = 3,
            Members = Ordinal.Map(seed.Profiles.Select((p, i) => (p!.Value.Value, new MemberRecord(
                SheetBuilder.DefaultCharacter($"Player{i + 1}", "human", i == 0 ? "ranger" : "fighter", null, 3, rules), 2000)))) };
        var info = seed.Lease.Info;
        var screens = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            ["portal-companion-name"] = JsonNode.Parse(portal.PortalView(info, doc.ToJson(), seed.Profiles[0]!.Value).GetRawText())!,
        };
        var first = doc.Members[seed.Profiles[0]!.Value.Value];
        doc = doc with { Members = doc.Members.SetItem(seed.Profiles[0]!.Value.Value,
            first with { Character = first.Character with { Subclass = "ranger.beastmaster", CompanionName = "abcdefghijklmnop" } }) };
        var session = new TacticsGameTests.Session(document: doc.ToJson());
        session.Harness.StartCampaign(session.Lease);
        session.Send(0, "battles"); session.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        StoryVoteTests.Until(session, () => Enumerable.Range(0, 4).Any(i => session.Screen(i)?.Screen == "turn"), "a real turn");
        var turn = Enumerable.Range(0, 4).Select(session.Screen).First(v => v?.Screen == "turn")!;
        screens["turn-named-companion"] = JsonNode.Parse(JsonSerializer.Serialize(turn, ArcJson.Options))!;
        PhoneFixtureTests.Check(screens);
    }
}
