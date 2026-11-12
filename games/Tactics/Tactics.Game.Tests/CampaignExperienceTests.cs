using System.Collections.Immutable;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class CampaignExperienceTests
{
    [Fact]
    public void Every_story_branch_arrives_within_one_level_without_optional_experience()
    {
        var rules = TacticsGameTests.Rules;
        var arcs = StoryTests.Lore.Arcs.Where(a => !a.SideAdventure).OrderBy(a => a.Chapter).ToArray();
        var choices = arcs.SelectMany(a => a.Beats).Where(b => !b.Choices.IsEmpty).ToArray();
        var visited = new HashSet<string>(); var paths = 0;
        void Walk(int index, Dictionary<string, ArcChoice> picked)
        {
            if (index < choices.Length)
            {
                foreach (var choice in choices[index].Choices)
                { picked[choices[index].Id] = choice; Walk(index + 1, picked); }
                return;
            }
            paths++;
            var xp = 0; var flags = new HashSet<string>();
            foreach (var beat in arcs.SelectMany(a => a.Beats))
            {
                if (!beat.Requires.All(flags.Contains)) continue;
                if (beat.Encounter is { } encounter)
                {
                    var definition = rules.Encounters[encounter];
                    var level = TacticsDocument.LevelFor([xp], rules.Xp);
                    Assert.True(definition.Level is { } tuned && Math.Abs(tuned - level) <= 1,
                        $"{beat.Id}: arrives at {level} with {xp} XP, tuned {definition.Level}; {string.Join(',', picked.Values.Select(c => c.Id))}");
                    visited.Add(encounter); xp += definition.Xp;
                }
                if (picked.TryGetValue(beat.Id, out var chosen))
                { flags.UnionWith(chosen.Sets); xp += chosen.Xp; }
                flags.UnionWith(beat.Sets); xp += beat.Xp;
            }
        }
        Walk(0, []);
        Assert.Equal(144, paths);
        Assert.Equal(arcs.SelectMany(a => a.Beats).Where(b => b.Encounter is not null).Select(b => b.Encounter!).Distinct().Order(), visited.Order());
    }

    [Theory]
    [InlineData("ford_assault")]
    [InlineData("river_crossing")]
    public void Ford_and_three_quests_reach_the_hollows_tuned_level_without_roaming(string ford)
    {
        var rules = TacticsGameTests.Rules;
        var xp = rules.Encounters[ford].Xp + StoryTests.Lore.Quests.Where(q => q.Id is "sq_missing_grain" or "sq_cold_forge" or "sq_guards_whistle").Sum(q => q.Rewards.Xp);
        Assert.Equal(1000, xp);
        Assert.Equal(rules.Encounters["hollow_lair"].Level, TacticsDocument.LevelFor([xp], rules.Xp));
    }

    [Fact]
    public void Story_and_peaceful_rewards_level_every_member_once_in_the_same_saved_completion()
    {
        var session = RestTests.AtCamp(); var doc = session.Game.Document;
        var beat = StoryTests.Lore.Arcs.SelectMany(a => a.Beats).Single(b => b.Id == "ch1_the_ember");
        var updated = CampaignExperience.CompleteBeat(doc, beat, TacticsGameTests.Rules);
        Assert.All(updated.Members, m => Assert.Equal(doc.Members[m.Key].Xp + 1300, m.Value.Xp));
        Assert.All(updated.Members.Values, m => Assert.Equal(updated.PartyLevel, m.Character.Level));
        Assert.Contains(beat.Id, updated.World!.Beats);
        Assert.Equal(updated.ToJson().GetRawText(), CampaignExperience.CompleteBeat(TacticsDocument.Read(updated.ToJson()), beat, TacticsGameTests.Rules).ToJson().GetRawText());
        var choice = StoryTests.Lore.Arcs.SelectMany(a => a.Beats).Single(b => b.Id == "ch4_ivos_choice");
        var peaceful = doc with { World = doc.World! with { Flags = [.. doc.World.Flags, "ch4_ivo_helped"] } };
        Assert.All(CampaignExperience.CompleteBeat(peaceful, choice, TacticsGameTests.Rules).Members,
            m => Assert.Equal(doc.Members[m.Key].Xp + 1300, m.Value.Xp));
        var combat = doc with { World = doc.World! with { Flags = [.. doc.World.Flags, "ch4_ivo_stopped"] } };
        Assert.Equal(doc.Members.Values.Sum(m => m.Xp), CampaignExperience.CompleteBeat(combat, choice, TacticsGameTests.Rules).Members.Values.Sum(m => m.Xp));
    }

    [Theory]
    [InlineData(1, 1, 75)]
    [InlineData(1, 2, 75)]
    [InlineData(1, 3, 0)]
    [InlineData(1, 10, 0)]
    public void Outleveled_roaming_groups_cannot_pay_repeated_chapter_experience(int encounter, int party, int expected)
        => Assert.Equal(expected, CampaignExperience.MobReward(75, encounter, party));
}
