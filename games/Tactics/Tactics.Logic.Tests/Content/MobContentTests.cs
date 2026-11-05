namespace Tactics.Logic.Tests;

public sealed class MobContentTests
{
    private const string File = "mobs/cinder_hollow.json";
    private const string Valid = """
    { "region":"cinder_hollow", "mobs":[{
      "id":"test_mob", "encounter":"hollow_throat", "arena":"hollow_throat",
      "patch":[[7,9]], "show":["characters.kobold"], "pace":2, "xp":75, "gold":10
    }] }
    """;

    private static RulesPack Load(string json)
    {
        var folder = Path.Combine(Path.GetTempPath(), "arcbox-mobs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "mobs"));
        try
        {
            System.IO.File.WriteAllText(Path.Combine(folder, "manifest.json"), """
                { "id":"mob-test", "rulesVersion":1, "requires":["base"], "files":["mobs/cinder_hollow.json"] }
                """);
            System.IO.File.WriteAllText(Path.Combine(folder, File), json);
            return RulesPack.Load([Path.Combine(ContentPaths.ContentBase, "rules"), folder]);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void Manifest_listed_mob_files_are_loaded_into_their_region()
    {
        var rules = Load(Valid);
        var mob = Assert.Single(rules.MobsIn("cinder_hollow").Where(m => m.Id == "test_mob"));
        Assert.Equal("test_mob", mob.Id);
        Assert.Equal(75, mob.Xp);
        Assert.Empty(rules.MobsIn("absent"));
    }

    [Theory]
    [InlineData("[[7,9]]", "[[0,0]]", "/patch")]
    [InlineData("[[7,9]]", "[[7,9],[9,9]]", "/patch")]
    [InlineData("\"pace\":2", "\"pace\":0", "/pace")]
    [InlineData("\"xp\":75", "\"xp\":-1", "/mobs/0")]
    [InlineData("\"region\":\"cinder_hollow\"", "\"region\":\"missing\"", "/region")]
    [InlineData("\"arena\":\"hollow_throat\"", "\"arena\":\"missing\"", "/arena")]
    [InlineData("\"encounter\":\"hollow_throat\"", "\"encounter\":\"missing\"", "/encounter")]
    public void Invalid_mob_content_reports_a_located_error(string before, string after, string pointer)
    {
        var errors = Assert.Throws<ContentException>(() => Load(Valid.Replace(before, after))).Errors;
        Assert.Contains(errors, e => e.Pointer.EndsWith(pointer, StringComparison.Ordinal));
    }
    [Fact]
    public void Gaining_levels_does_not_raise_roaming_enemy_stats()
    {
        var rules = BasePackTests.Rules;
        foreach (var mob in rules.Mobs.Values.SelectMany(m => m))
            for (var size = 1; size <= 6; size++)
            {
                UnitSheet[] Enemies(int level) => [.. Encounters.Setup(mob.Encounter,
                    [.. Enumerable.Range(0, size).Select(i => new PartyMember(new UnitId("hero" + i),
                        SheetBuilder.DefaultCharacter("Hero " + i, "human", "fighter", null, level, rules), new AiControlled("companion")))],
                    Difficulty.Normal, rules).Units.Where(u => u.Sheet.Side == Side.Enemy).Select(u => u.Sheet)];
                var arrival = Enemies(1);
                foreach (var level in new[] { 2, 3 })
                {
                    var later = Enemies(level);
                    Assert.Equal(System.Text.Json.JsonSerializer.Serialize(arrival, ArcBox.Sdk.ArcJson.Options),
                        System.Text.Json.JsonSerializer.Serialize(later, ArcBox.Sdk.ArcJson.Options));
                    Assert.All(later, enemy => Assert.Equal(1, enemy.Level));
                }
            }
    }

}
