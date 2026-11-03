using System.Text.Json.Nodes;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

public sealed class CrossRegionQuestTests
{
    private static LoreBook Book(string targetRegion, bool realRules = false)
    {
        var folder = Directory.CreateTempSubdirectory("tactics-quest-regions-");
        try
        {
            foreach (var sub in realRules ? new[] { "lore", "rules" } : new[] { "lore" })
                foreach (var file in Directory.EnumerateFiles(Path.Combine(ContentBase(), sub), "*.json", SearchOption.AllDirectories))
                {
                    var target = Path.Combine(folder.FullName, Path.GetRelativePath(ContentBase(), file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                }
            var path = Path.Combine(folder.FullName, "lore", "quests.json");
            var data = JsonNode.Parse(File.ReadAllText(path))!;
            var quest = data["quests"]!.AsArray().Single(q => q!["id"]!.GetValue<string>() == "sq_missing_grain")!;
            quest["region"] = realRules ? "ashen_ford" : "outside";
            quest["stages"]![0]!["target"] = new JsonObject { ["region"] = targetRegion, ["tile"] = new JsonArray(4, 3) };
            File.WriteAllText(path, data.ToJsonString());
            return LoreBook.Load(folder.FullName);
        }
        finally { folder.Delete(recursive: true); }
    }

    [Theory]
    [InlineData("outside", 3, 2)]
    [InlineData("hollow", 4, 3)]
    public void A_known_quest_stays_in_the_journal_and_foreign_targets_guide_to_a_visible_exit(string targetRegion, int x, int y)
    {
        var world = new WorldSave("hollow", new Tile(2, 2), new Tile(3, 2), null,
            ["bounty_accepted", "sq_grain_asked"], [], PinnedQuest: "sq_missing_grain");
        var s = new Session(document: ExplorationTests.DocumentAt(world), game: new TacticsGame(EntranceTravelTests.TestRules, lore: Book(targetRegion)));
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal("sq_missing_grain", s.Host.Explore!.Pinned!.Id);
        Assert.Equal(new Tile(x, y), s.Host.Explore.Pinned.Target);
        s.Send(StageBattleTests.Lead(s), "questLog", new { action = "open" });
        Assert.Equal("sq_missing_grain", Assert.Single(s.Host.Explore.QuestLog!.Quests).Id);
    }

    [Fact]
    public void Unstarted_quests_in_other_regions_are_not_disclosed()
    {
        var world = new WorldSave("hollow", new Tile(2, 2), new Tile(3, 2), null, ["bounty_accepted"], [], PinnedQuest: "sq_missing_grain");
        var s = new Session(document: ExplorationTests.DocumentAt(world), game: new TacticsGame(EntranceTravelTests.TestRules, lore: Book("outside")));
        s.Harness.StartCampaign(s.Lease);
        Assert.Null(s.Host.Explore!.Pinned);
        s.Send(StageBattleTests.Lead(s), "questLog", new { action = "open" });
        Assert.Null(s.Host.Explore.QuestLog);
    }

    [Fact]
    public void A_target_region_is_validated_against_the_actual_rules_pack()
    {
        var valid = Book("ashen_ford", realRules: true);
        Assert.Equal("ashen_ford", valid.Quests.First(q => q.Id == "sq_missing_grain").Stages[0].Target!.Region);
        var error = Assert.Throws<InvalidDataException>(() => Book("missing", realRules: true));
        Assert.Contains("unknown target region", error.Message, StringComparison.Ordinal);
    }
}
