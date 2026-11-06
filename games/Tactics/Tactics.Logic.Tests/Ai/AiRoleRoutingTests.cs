namespace Tactics.Logic.Tests;

public sealed class AiRoleRoutingTests
{
    [Theory]
    [InlineData("controller", "control", "caster", "controller")]
    [InlineData("guardian", "defender", "melee", "guardian")]
    [InlineData("control", "caster", "ranged", "caster")]
    public void Explicit_specialist_roles_take_priority_without_changing_general_casters(
        string first, string second, string third, string expected)
        => Assert.Equal(expected, AiProfile.IdForRoles([first, second, third]));
    [Theory]
    [InlineData(1, 2.4)]
    [InlineData(8, 2.4)]
    [InlineData(9, 4.0)]
    public void Profile_control_can_follow_spell_tier_progression_without_changing_earlier_levels(int level, double expected)
    {
        var profile = System.Text.Json.JsonSerializer.Deserialize<AiProfileDef>(
            """{"id":"test","control":2.4,"controlByLevel":{"1":2.4,"9":4.0}}""", ArcBox.Sdk.ArcJson.Options)!;
        var battle = TestBattle.On("0. 0. 0.")
            .Party("hero", TestSheets.Cleric with { Level = level, AiProfile = "test" }, new Tile(0, 0))
            .Enemy("foe", TestSheets.Goblin, new Tile(2, 0)).WithAiProfiles([profile]);
        var (state, _) = battle.Start();
        Assert.Equal(expected, AiProfile.For(state, new("hero"), battle.Rules).Control);
    }

    [Fact]
    public void Legacy_scalar_profiles_keep_the_same_control_at_high_levels()
    {
        var profile = new AiProfileDef("test", Control: 1.2);
        var battle = TestBattle.On("0. 0. 0.")
            .Party("hero", TestSheets.Cleric with { Level = 10, AiProfile = "test" }, new Tile(0, 0))
            .Enemy("foe", TestSheets.Goblin, new Tile(2, 0)).WithAiProfiles([profile]);
        Assert.Equal(1.2, AiProfile.For(battle.Start().State, new("hero"), battle.Rules).Control);
    }

    [Fact]
    public void A_control_progression_must_cover_level_one_before_the_pack_can_load()
    {
        var root = Path.Combine(Path.GetTempPath(), "tactics-ai-profile-" + Guid.NewGuid().ToString("N"));
        var pack = Path.Combine(root, "base");
        try
        {
            foreach (var file in Directory.GetFiles(ContentPaths.Fixture("minimal", "base"), "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(pack, Path.GetRelativePath(ContentPaths.Fixture("minimal", "base"), file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            File.WriteAllText(Path.Combine(pack, "ai-profiles.json"),
                """[{"id":"brute","controlByLevel":{"9":4.0}}]""");
            var error = Assert.Throws<ContentException>(() => RulesPack.Load([pack]));
            Assert.Contains(error.Errors, e => e.Message.Contains("cover level 1", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

}
