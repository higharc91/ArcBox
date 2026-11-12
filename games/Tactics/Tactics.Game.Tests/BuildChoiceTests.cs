using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game.Tests;

public sealed class BuildChoiceTests
{
    private static RulesPack Rules => TacticsGameTests.Rules;
    private static CharacterRecord Character(int level = 4, string race = "dwarf") => SheetBuilder.DefaultCharacter("Kara", race, "fighter", null, level, Rules);

    [Fact]
    public void Feats_and_increases_share_the_two_milestones_and_cannot_be_duplicated_or_forged()
    {
        var c = Character();
        var feat = CharacterRules.ChooseFeat(c, "feat.alert", Rules);
        Assert.Null(feat.Problem); Assert.Equal(0, CharacterRules.IncreasesLeft(feat.Character!));
        Assert.Contains("feat.alert", SheetBuilder.ForCharacter(feat.Character!, Rules).Passives);
        Assert.NotNull(CharacterRules.SpendIncrease(feat.Character!, [Stat.Str], Rules).Problem);
        Assert.NotNull(CharacterRules.ChooseFeat(Character(1), "feat.alert", Rules).Problem);
        Assert.NotNull(CharacterRules.ChooseFeat(c, "equipment.weapon_plus_one", Rules).Problem);
        var raised = CharacterRules.SpendIncrease(c, [Stat.Str], Rules).Character!;
        Assert.NotNull(CharacterRules.ChooseFeat(raised, "feat.mobile", Rules).Problem);
        var eight = CharacterRules.ChooseFeat(Character(8), "feat.alert", Rules).Character!;
        Assert.Equal(2, CharacterRules.IncreasesLeft(eight));
        Assert.NotNull(CharacterRules.ChooseFeat(eight, "feat.alert", Rules).Problem);
        eight = CharacterRules.ChooseFeat(eight, "feat.mobile", Rules).Character!;
        Assert.Equal(0, CharacterRules.IncreasesLeft(eight));
        var plain = SheetBuilder.ForCharacter(c, Rules);
        var tough = SheetBuilder.ForCharacter(CharacterRules.ChooseFeat(c, "feat.tough", Rules).Character!, Rules);
        Assert.True(tough.MaxHp > plain.MaxHp);
        Assert.Throws<ContentException>(() => SheetBuilder.ForCharacter(Character(1) with { Feats = ["feat.tough"] }, Rules));
    }

    [Fact]
    public void Chosen_class_skills_replace_the_automatic_choices_and_affect_exploration_checks()
    {
        var sample = Character(1);
        var scores = sample.BaseScores;
        var result = CharacterRules.Create("Kara", "dwarf", "fighter", "sage", scores, 1, Rules, ["perception", "survival"]);
        Assert.Null(result.Problem);
        var sheet = SheetBuilder.ForCharacter(result.Character!, Rules);
        Assert.Contains("perception", sheet.Skills); Assert.Contains("survival", sheet.Skills);
        Assert.Contains("arcana", sheet.Skills); Assert.Contains("history", sheet.Skills);
        Assert.DoesNotContain("acrobatics", sheet.Skills);
        Assert.Equal(Stats.Mod(sheet.Score(Stat.Wis)) + 2, SkillCheck.Bonus(sheet, "perception"));
        Assert.NotNull(CharacterRules.Create("Kara", "dwarf", "fighter", "sage", scores, 1, Rules, ["perception"]).Problem);
        Assert.NotNull(CharacterRules.Create("Kara", "dwarf", "fighter", "sage", scores, 1, Rules, ["perception", "perception"]).Problem);
        Assert.NotNull(CharacterRules.Create("Kara", "dwarf", "fighter", "sage", scores, 1, Rules, ["perception", "arcana"]).Problem);
    }

    [Theory]
    [InlineData("acid")]
    [InlineData("cold")]
    [InlineData("fire")]
    [InlineData("lightning")]
    [InlineData("poison")]
    public void Dragon_ancestry_controls_breath_damage_and_resistance(string ancestry)
    {
        var c = Character(1, "dragonborn") with { DragonAncestry = ancestry };
        var sheet = SheetBuilder.ForCharacter(c, Rules);
        Assert.Contains(ancestry, sheet.Resist);
        if (ancestry != "fire") Assert.DoesNotContain("fire", sheet.Resist);
        var breath = BuildChoices.Breath(ancestry);
        Assert.Contains(breath, sheet.Abilities);
        var damage = EffectTree.All(Rules.Abilities[breath].Effects).OfType<DamageEffect>().ToArray();
        Assert.NotEmpty(damage); Assert.All(damage, d => Assert.Equal(ancestry, d.Type));
        Assert.Throws<ContentException>(() => SheetBuilder.ForCharacter(Character(1) with { DragonAncestry = ancestry }, Rules));
        Assert.Throws<ContentException>(() => SheetBuilder.ForCharacter(c with { DragonAncestry = "thunder" }, Rules));
    }

    [Fact]
    public void The_portal_validates_creation_choices_and_offers_feat_or_increase_at_four_and_eight()
    {
        var session = new TacticsGameTests.Session(); var portal = new TacticsCampaignRules(Rules); var owner = session.Profiles[0]!.Value;
        var info = session.Lease.Info; var empty = portal.NewDocument(info); var scores = Character(1).BaseScores;
        var valid = portal.Handle(info, empty, owner, "createCharacter", JsonSerializer.SerializeToElement(new
        { race = "dragonborn", @class = "fighter", background = "sage", scores, skills = new[] { "perception", "survival" }, ancestry = "cold" }, ArcJson.Options));
        Assert.Null(valid.Error); var doc = TacticsDocument.Read(valid.Document!.Value);
        Assert.Equal("cold", doc.For(owner)!.Character.DragonAncestry);
        Assert.Equal(new[] { "perception", "survival" }, doc.For(owner)!.Character.ClassSkills);
        var fixtures = new SortedDictionary<string, System.Text.Json.Nodes.JsonNode>(StringComparer.Ordinal)
        { ["portal-build-creation"] = System.Text.Json.Nodes.JsonNode.Parse(portal.PortalView(info, empty, owner).GetRawText())! };
        foreach (var level in new[] { 4, 8 })
        {
            var member = doc.For(owner)!;
            var leveled = doc with { PartyLevel = level, Members = doc.Members.SetItem(owner.Value, member with { Character = CharacterRules.LevelUp(member.Character, level, Rules) }) };
            var me = portal.PortalView(info, leveled.ToJson(), owner).GetProperty("me");
            Assert.Equal(3, me.GetProperty("featChoices").GetArrayLength());
            var chosen = portal.Handle(info, leveled.ToJson(), owner, "chooseFeat", JsonSerializer.SerializeToElement(new { feat = "feat.alert" }, ArcJson.Options));
            Assert.Null(chosen.Error);
            if (level == 4)
            {
                fixtures["portal-build-feats"] = System.Text.Json.Nodes.JsonNode.Parse(portal.PortalView(info, leveled.ToJson(), owner).GetRawText())!;
                fixtures["portal-build-owned-feat"] = System.Text.Json.Nodes.JsonNode.Parse(portal.PortalView(info, chosen.Document!.Value, owner).GetRawText())!;
            }
        }
        PhoneFixtureTests.Check(fixtures);
        var invalid = portal.Handle(info, empty, owner, "createCharacter", JsonSerializer.SerializeToElement(new
        { race = "dwarf", @class = "fighter", background = "sage", scores, skills = new[] { "perception", "perception" } }, ArcJson.Options));
        Assert.Null(invalid.Document); Assert.NotNull(invalid.Error);
    }

    [Fact]
    public void Legacy_records_keep_their_bytes_when_new_choices_were_never_recorded()
    {
        var c = Character(1); var json = JsonSerializer.Serialize(c, ArcJson.Options);
        Assert.DoesNotContain("classSkills", json); Assert.DoesNotContain("dragonAncestry", json); Assert.DoesNotContain("feats", json);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<CharacterRecord>(json, ArcJson.Options), ArcJson.Options));
    }
}
