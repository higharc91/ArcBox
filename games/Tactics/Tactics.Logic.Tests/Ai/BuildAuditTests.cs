using System.Collections.Immutable;
using Tactics.Sim;

namespace Tactics.Logic.Tests;

public sealed class BuildAuditTests
{
    private static RulesPack Rules => BasePackTests.Rules;
    private static SimConfig Config => new(["forest_ruins"], [5], 1, [4], 1, Difficulty.Normal);

    [Theory]
    [InlineData(3, 0)]
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    [InlineData(10, 4)]
    public void Simulated_characters_spend_their_earned_increases_without_exceeding_twenty(int level, int points)
    {
        foreach (var cls in Rules.Classes.Keys)
        {
            var character = SimRunner.Character(Config, Rules, "hero", cls, level);
            Assert.Equal(points, character.Increases.Values.Sum());
            Assert.All(SheetBuilder.ForCharacter(character, Rules).Scores.Values, score => Assert.InRange(score, 1, 20));
            Assert.Empty(SimRunner.Character(Config with { AbilityIncreases = false }, Rules, "control", cls, level).Increases);
        }
    }

    [Fact]
    public void An_explicit_party_can_use_distinct_races_and_subclasses_with_authoritative_validation()
    {
        var config = Config with { FixedParty = ["fighter", "cleric", "rogue", "wizard"],
            Races = new Dictionary<string, string> { ["fighter"] = "dwarf", ["wizard"] = "elf" }.ToImmutableDictionary(),
            Subclasses = new Dictionary<string, string> { ["fighter"] = Rules.Classes["fighter"].Subclasses[0].Id }.ToImmutableDictionary() };
        Assert.Equal(config.FixedParty, Assert.Single(SimRunner.Parties(config, Rules)));
        Assert.Equal("dwarf", SimRunner.Character(config, Rules, "hero1", "fighter", 5).Race);
        Assert.Equal("elf", SimRunner.Character(config, Rules, "hero2", "wizard", 5).Race);
        Assert.Equal("human", SimRunner.Character(config, Rules, "hero3", "cleric", 5).Race);
        Assert.Throws<ArgumentException>(() => SimRunner.Run(config with { Races = config.Races!.SetItem("wizard", "missing") }, Rules).ToArray());
        Assert.Throws<ArgumentException>(() => SimRunner.Run(config with { FixedParty = ["missing"] }, Rules).ToArray());
    }

    [Fact]
    public void The_audit_pairs_every_subclass_and_race_at_all_four_required_levels()
    {
        var schedule = BuildAudit.Schedule(Rules, 20, 30).ToArray();
        var subclasses = Rules.Classes.Values.SelectMany(c => c.Subclasses).Select(s => s.Id).ToArray();
        Assert.Equal(36, subclasses.Length);
        foreach (var sub in subclasses) Assert.Equal([3, 5, 7, 10], schedule.Where(s => s.Kind == "subclass" && s.Build == sub).Select(s => s.Level));
        foreach (var race in Rules.Races.Keys) Assert.Equal(4, schedule.Count(s => s.Kind == "race" && s.Build == race));
        foreach (var cell in schedule.Where(s => s.Kind == "subclass"))
        {
            var control = Assert.Single(schedule, s => s.Kind == "class" && s.Class == cell.Class && s.Level == cell.Level);
            Assert.Equal(control.Config.FixedParty, cell.Config.FixedParty);
            Assert.Equal(control.Config.Seeds, cell.Config.Seeds);
            Assert.Equal(4, cell.Config.FixedParty.Distinct().Count());
            Assert.True(cell.Config.AbilityIncreases);
        }
    }
}
