using System.Text.Json.Nodes;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.ConversationQuestTests;

namespace Tactics.Game.Tests;

public sealed class ClassTrinketRewardTests
{
    private static LoreBook Book(Action<JsonObject>? change = null)
    {
        var folder = Directory.CreateTempSubdirectory("tactics-class-rewards-");
        try
        {
            foreach (var sub in new[] { "lore", "rules" })
                foreach (var file in Directory.EnumerateFiles(Path.Combine(ContentBase(), sub), "*.json", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(folder.FullName, Path.GetRelativePath(ContentBase(), file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            var path = Path.Combine(folder.FullName, "lore", "quests.json");
            var data = JsonNode.Parse(File.ReadAllText(path))!;
            var rewards = data["quests"]!.AsArray().Single(q => q!["id"]!.GetValue<string>() == "sq_missing_grain")!["rewards"]!;
            var map = new JsonObject();
            foreach (var trinket in Rules.Trinkets.Values) map[trinket.Class] = trinket.Id;
            change?.Invoke(map);
            rewards["gold"] = 60;
            rewards["xp"] = 300;
            rewards["trinkets"] = map;
            File.WriteAllText(path, data.ToJsonString());
            return LoreBook.Load(folder.FullName);
        }
        finally { folder.Delete(recursive: true); }
    }

    [Fact]
    public void Turn_in_awards_every_class_including_away_members_in_one_saved_transaction_and_never_twice()
    {
        var book = Book();
        var members = Ordinal.Map(Rules.Classes.Keys.Select((cls, i) => ($"pr_member{i + 1}",
            new MemberRecord(SheetBuilder.DefaultCharacter(cls, "human", cls, null, 2, Rules), 300))));
        var halden = Rules.RegionFor("ashen_ford")!.Def.People.Single(p => p.Id == "halden_varrow");
        var world = ExplorationTests.Saved(halden.At) with { Flags = ["sq_grain_asked", "sq_grain_clue", "sq_grain_tally"] };
        var doc = TacticsDocument.New with { Members = members, PartyLevel = 2, World = world };
        var s = new Session(document: doc.ToJson(), game: new TacticsGame(Rules, lore: book));
        s.Harness.StartCampaign(s.Lease);
        s.Harness.SetConnected(3, false);
        Assert.All(s.Game.Document.Members.Values, m => Assert.Null(m.Character.Trinket));
        Talk(s, "halden_varrow"); Choose(s, "quiet"); WalkAgain(s);
        var after = TacticsDocument.Read(s.Lease.Document);
        Assert.Equal(12, after.Members.Count);
        Assert.Equal(60, after.Members.Values.Sum(m => m.Gold));
        foreach (var (id, member) in after.Members)
        {
            var expected = Assert.Single(Rules.Trinkets.Values, t => t.Class == member.Character.Class);
            Assert.Equal(expected.Id, member.Character.Trinket);
            Assert.Equal(members[id].Xp + 300, member.Xp);
            if (id is not ("pr_member1" or "pr_member2" or "pr_member3")) Assert.Equal(0, member.Gold);
        }
        Assert.Equal(["sq_missing_grain"], after.World!.QuestsRewarded);
        var next = new Session(document: after.ToJson(), game: new TacticsGame(Rules, lore: book));
        next.Harness.StartCampaign(next.Lease);
        Assert.Equal(after.ToJson().GetProperty("members").GetRawText(), next.Game.Document.ToJson().GetProperty("members").GetRawText());
    }

    [Theory]
    [InlineData("unknown_class")]
    [InlineData("unknown_trinket")]
    [InlineData("wrong_class")]
    [InlineData("missing_class")]
    public void Invalid_class_reward_maps_are_rejected_before_play(string kind)
    {
        var error = Assert.Throws<InvalidDataException>(() => Book(map =>
        {
            switch (kind)
            {
                case "unknown_class": map["invented"] = "ember_claw_gauntlet"; break;
                case "unknown_trinket": map["fighter"] = "missing"; break;
                case "wrong_class": map["fighter"] = "hearthstone_pendant"; break;
                case "missing_class": map.Remove("wizard"); break;
            }
        }));
        Assert.Contains("trinket", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
