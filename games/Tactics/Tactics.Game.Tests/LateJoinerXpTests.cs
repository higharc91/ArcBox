using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class LateJoinerXpTests
{
    private static RulesPack Rules => TacticsGameTests.Rules;

    private static TacticsDocument LevelThreeParty()
    {
        var members = TacticsDocument.New.Members;
        for (var i = 1; i <= 3; i++)
        {
            var character = SheetBuilder.DefaultCharacter($"Player{i}", "human", "fighter", null, 3, Rules);
            members = members.SetItem($"pr_member{i}", new MemberRecord(character, 2400));
        }
        return TacticsDocument.New with { PartyLevel = 3, Members = members };
    }

    [Theory]
    [InlineData("class pick")]
    [InlineData("absent battle member")]
    [InlineData("portal character")]
    public void A_late_joiner_catches_up_and_the_whole_party_reaches_level_four(string path)
    {
        var doc = LevelThreeParty();
        var session = new TacticsGameTests.Session(document: doc.ToJson());
        var newcomer = session.Profiles[3]!.Value;
        TacticsDocument joined;

        if (path == "portal character")
        {
            var rules = new TacticsCampaignRules(Rules);
            var data = JsonSerializer.SerializeToElement(new { race = "human", @class = "fighter",
                background = Rules.Backgrounds.Keys.First(),
                scores = new { str = 15, dex = 15, con = 15, @int = 8, wis = 8, cha = 8 } }, ArcJson.Options);
            var result = rules.Handle(session.Lease.Info, doc.ToJson(), newcomer, "createCharacter", data);
            Assert.Null(result.Error);
            joined = TacticsDocument.Read(result.Document!.Value);
        }
        else
        {
            session.Harness.StartCampaign(session.Lease);
            if (path == "class pick")
                session.Send(3, "pickClass", new { @class = "fighter" });
            else
            {
                session.Harness.SetConnected(3, false);
                session.Send(0, "battles");
                session.Send(0, "pickEncounter", new { encounter = "river_crossing" });
            }
            joined = session.Game.Document;
        }

        Assert.Equal(3, joined.PartyLevel);
        Assert.Equal(4, joined.Members.Count);
        Assert.All(joined.Members.Values, member => Assert.Equal(2400, member.Xp));
        Assert.Equal(3, joined.Members[newcomer.Value].Character.Level);

        var afterReward = joined with { Members = Ordinal.Map(joined.Members.Select(m =>
            (m.Key, m.Value with { Xp = m.Value.Xp + 600 }))) };
        Assert.All(afterReward.Members.Values, member => Assert.Equal(3000, member.Xp));
        var level = TacticsDocument.LevelFor(afterReward.Members.Values.Select(m => m.Xp), Rules.Xp);
        Assert.Equal(4, level);
        Assert.All(afterReward.Members.Values, member =>
            Assert.Equal(4, CharacterRules.LevelUp(member.Character, level, Rules).Level));
    }

    [Fact]
    public void A_version_zero_campaign_keeps_its_members_and_xp_when_read()
    {
        var old = JsonNode.Parse(LevelThreeParty().ToJson().GetRawText())!.AsObject();
        old["version"] = 0;
        old.Remove("world");

        var read = TacticsDocument.Read(JsonDocument.Parse(old.ToJsonString()).RootElement);

        Assert.Equal(3, read.PartyLevel);
        Assert.Equal(3, read.Members.Count);
        Assert.All(read.Members.Values, member => Assert.Equal((2400, 3), (member.Xp, member.Character.Level)));
        Assert.Equal(3, TacticsDocument.LevelFor(read.Members.Values.Select(m => m.Xp), Rules.Xp));
    }

    [Fact]
    public void A_new_member_starts_at_zero_only_in_an_empty_party_and_never_below_the_party_level()
    {
        Assert.Equal(0, TacticsDocument.New.StartingXpForNewMember(Rules.Xp));
        var party = LevelThreeParty();
        var first = party.Members["pr_member1"];
        party = party with { Members = party.Members.SetItem("pr_member1", first with { Xp = 1500 }) };
        Assert.Equal(2000, party.StartingXpForNewMember(Rules.Xp));
    }
}
