using Tactics.Logic;

namespace Tactics.Logic.Tests;

public sealed class RecoveryRulesTests
{
    [Theory]
    [InlineData("tend", "medicine", 12)]
    [InlineData("cook", "survival", 10)]
    [InlineData("watch", "perception", 0)]
    public void Jobs_use_the_specified_skill_and_dc(string job, string skill, int dc)
    {
        Assert.Equal(skill, RecoveryRules.Skill(job, _ => 0));
        Assert.Equal(dc, RecoveryRules.Dc(job));
    }

    [Fact]
    public void Rally_uses_the_better_social_skill_and_checks_have_no_natural_one_or_twenty_rule()
    {
        Assert.Equal("performance", RecoveryRules.Skill("rally", s => s == "performance" ? 4 : 1));
        Assert.Equal("persuasion", RecoveryRules.Skill("rally", s => s == "persuasion" ? 4 : 1));
        Assert.True(RecoveryRules.Passed(1, 12, 12));
        Assert.False(RecoveryRules.Passed(20, -10, 12));
    }

    [Fact]
    public void Injury_kinds_and_the_campfire_are_part_of_the_shipped_pack()
    {
        var rules = RulesPack.Load([Path.Combine(ContentPaths.ContentBase, "rules")], ArtManifests.Load(ContentPaths.ContentBase));
        Assert.Equal("rest", rules.Injuries[new("black_eye")].Mends);
        Assert.Equal("spirit", rules.Injuries[new("shaken")].Mends);
        Assert.Equal("body", rules.Injuries[new("deep_cut")].Mends);
        Assert.Equal(InteractableFamily.Rest, InteractableKinds.Of("campfire")!.Family);
        Assert.True(InteractableKinds.Of("campfire")!.Blocks);
        Assert.Equal("unlit", Interactables.Initial(rules.InteractablesIn("ashen_ford").First(t => t.Kind == "campfire")).State);
        Assert.Contains("rest.well_fed", rules.Passives.Keys);
    }
    [Theory]
    [InlineData("unknown")]
    [InlineData("")]
    public void Invalid_injury_recovery_kinds_name_the_source_pointer(string kind)
    {
        var errors = ValidationTests.ErrorsOfChapter(null, ("injuries.json", $$"""[{"id":"bad_mend","name":"Bad","weight":1,"static":{},"mends":"{{kind}}"}]"""));
        Assert.Contains(errors, e => e.Pointer == "/0/mends");
    }

    [Theory]
    [InlineData("room", 0)]
    [InlineData("cure", -1)]
    [InlineData("resurrect", 10)]
    public void Invalid_service_kinds_and_prices_are_rejected(string kind, int price)
    {
        var region = Vault.Region() with { People = [new RegionPerson("merchant", new Tile(3, 1), "sw")] };
        var json = $$"""{"region":"vault","shops":[{"id":"recovery","name":"Recovery","keeper":{"person":0},"stock":[],"buys":[],"services":[{"id":"service","name":"Service","kind":"{{kind}}","price":{{price}}}]}]}""";
        var errors = ValidationTests.ErrorsOfChapter(Vault.World(region), ("shops/vault.json", json));
        Assert.Contains(errors, e => e.Pointer.StartsWith("/shops/0/services/0", StringComparison.Ordinal));
    }
    [Theory]
    [InlineData(false, "black_eye", false)]
    [InlineData(false, "cracked_ribs", true)]
    [InlineData(false, "shaken", true)]
    [InlineData(true, "black_eye", false)]
    [InlineData(true, "cracked_ribs", false)]
    [InlineData(true, "shaken", true)]
    public void The_sleep_matrix_covers_each_service_and_injury_kind(bool room, string injury, bool remains)
    {
        var result = RecoveryRules.AfterSleep([new(injury)], BasePackTests.Rules, room);
        Assert.Equal(remains, result.Contains(new InjuryId(injury)));
        Assert.Equal(room ? 10 : 0, RecoveryRules.Price(room ? "inn" : "camp"));
    }

    [Fact]
    public void A_room_mends_exactly_one_body_injury_and_keeps_spirit_and_unknown_injuries()
    {
        var injuries = new InjuryId[] { new("cracked_ribs"), new("deep_cut"), new("black_eye"), new("shaken"), new("future_injury") };
        var result = RecoveryRules.AfterSleep([.. injuries], BasePackTests.Rules, true);
        Assert.Equal([new InjuryId("deep_cut"), new("shaken"), new("future_injury")], result);
        Assert.Equal("check", RecoveryRules.Recovery("camp", "body"));
        Assert.Equal("check", RecoveryRules.Recovery("camp", "spirit"));
        Assert.Equal("body", RecoveryRules.Mends("tend")); Assert.Equal("spirit", RecoveryRules.Mends("rally"));
        Assert.False(RecoveryRules.Passed(1, 0, RecoveryRules.Dc("tend")));
        Assert.True(RecoveryRules.Passed(12, 0, RecoveryRules.Dc("rally")));
    }
}
