using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class TrinketTests
{
    private static RulesPack Rules => BasePackTests.Rules;
    private static CharacterRecord Character(string cls) => SheetBuilder.DefaultCharacter("Ash", "human", cls, null, 2, Rules);

    private static (BattleState State, UnitId Hero) Start(CharacterRecord character)
    {
        var map = Rules.MapFor("river_crossing")!;
        var hero = new UnitId("hero");
        var setup = new BattleSetup(map,
            [new(hero, SheetBuilder.ForCharacter(character, Rules), new PlayerControlled(new ProfileId("hero")), map.Zones["party_spawn"][0], []),
             new(new UnitId("wolf"), SheetBuilder.ForMonster("wolf", Rules), new AiControlled("brute"), map.Zones["goblin_camp"][0], [])],
            ObjectivesDef.Default, [], Difficulty.Normal, 0, []);
        return (Battle.Start(setup, Rules, 7).State, hero);
    }

    [Theory]
    [InlineData("barbarian")]
    [InlineData("bard")]
    [InlineData("cleric")]
    [InlineData("druid")]
    [InlineData("fighter")]
    [InlineData("monk")]
    [InlineData("paladin")]
    [InlineData("ranger")]
    [InlineData("rogue")]
    [InlineData("sorcerer")]
    [InlineData("warlock")]
    [InlineData("wizard")]
    public void Each_class_reward_reaches_the_existing_battle_modifiers_and_hooks(string cls)
    {
        var reward = Assert.Single(Rules.Trinkets.Values, t => t.Class == cls);
        Assert.Equal("rare", reward.Rarity);
        Assert.False(string.IsNullOrWhiteSpace(reward.Text));
        var plain = Character(cls);
        var equipped = plain with { Trinket = reward.Id };
        var (before, hero) = Start(plain);
        var (after, _) = Start(equipped);
        var a = Modifiers.For(before, hero, Rules);
        var b = Modifiers.For(after, hero, Rules);
        Assert.Contains(RulesPack.TrinketPassiveId(reward.Id), after.Units[hero].Sheet.Passives);
        switch (cls)
        {
            case "barbarian":
                Assert.Equal(before.Units[hero].Sheet.MaxHp + 4, after.Units[hero].Sheet.MaxHp);
                Assert.Equal(a.MaxHp, b.MaxHp);
                Assert.Contains("fire", b.Resist!); break;
            case "bard":
                Assert.Equal(a.Initiative + 2, b.Initiative);
                Assert.Equal(1, b.Saves![Stat.Cha] - (a.Saves?.GetValueOrDefault(Stat.Cha) ?? 0)); break;
            case "cleric": Assert.Equal(Edge.Advantage, b.SaveEdges![Stat.Wis]); break;
            case "druid": Assert.Equal(a.Move + 1, b.Move); Assert.Contains("fire", b.Resist!); break;
            case "fighter": Assert.Equal(a.Attack + 1, b.Attack); break;
            case "monk": Assert.Equal(a.Ac + 1, b.Ac); break;
            case "paladin":
                Assert.Equal(1, b.Saves![Stat.Wis] - (a.Saves?.GetValueOrDefault(Stat.Wis) ?? 0));
                Assert.Equal(1, b.Saves[Stat.Cha] - (a.Saves?.GetValueOrDefault(Stat.Cha) ?? 0)); break;
            case "ranger": Assert.Equal(a.Damage + 1, b.Damage); break;
            case "rogue": Assert.Equal(a.Initiative + 2, b.Initiative); Assert.Equal(1, b.Saves![Stat.Dex]); break;
            case "sorcerer": Assert.Equal(3, after.Units[hero].TempHp); Assert.Equal(1, b.Saves![Stat.Con]); break;
            case "warlock": Assert.Equal(1, b.Saves![Stat.Wis]); Assert.Contains("necrotic", b.Resist!); break;
            case "wizard": Assert.Equal(a.Initiative + 1, b.Initiative); Assert.Equal(1, b.Saves![Stat.Int]); break;
        }
    }

    [Fact]
    public void A_class_change_keeps_the_trinket_but_makes_it_dormant_until_the_class_matches_again()
    {
        var reward = Assert.Single(Rules.Trinkets.Values, t => t.Class == "fighter");
        var dormant = Character("wizard") with { Trinket = reward.Id };
        Assert.Equal(reward.Id, dormant.Trinket);
        Assert.DoesNotContain(RulesPack.TrinketPassiveId(reward.Id), SheetBuilder.ForCharacter(dormant, Rules).Passives);
        var active = Character("fighter") with { Trinket = dormant.Trinket };
        Assert.Contains(RulesPack.TrinketPassiveId(reward.Id), SheetBuilder.ForCharacter(active, Rules).Passives);
    }

    [Fact]
    public void Unknown_trinkets_are_content_errors_instead_of_silently_granting_a_passive()
    {
        var error = Assert.Throws<ContentException>(() => SheetBuilder.ForCharacter(Character("fighter") with { Trinket = "invented" }, Rules));
        Assert.Contains(error.Errors, e => e.Pointer == "/trinket");
    }

    [Fact]
    public void Old_characters_omit_the_new_slot_and_equipped_characters_round_trip()
    {
        var plain = Character("fighter");
        var old = JsonSerializer.Serialize(plain, ArcJson.Options);
        Assert.DoesNotContain("trinket", old);
        Assert.Equal(old, JsonSerializer.Serialize(JsonSerializer.Deserialize<CharacterRecord>(old, ArcJson.Options), ArcJson.Options));
        var equipped = plain with { Trinket = Rules.Trinkets.Values.First().Id };
        Assert.Equal(equipped.Trinket, JsonSerializer.Deserialize<CharacterRecord>(JsonSerializer.Serialize(equipped, ArcJson.Options), ArcJson.Options)!.Trinket);
    }

    [Theory]
    [InlineData("class", "not_a_class", "/0/class")]
    [InlineData("rarity", "common", "/0/rarity")]
    [InlineData("hook", "not_a_hook", "/0/hook")]
    [InlineData("text", "", "/0/text")]
    public void Bad_trinket_content_reports_its_file_and_pointer(string field, string value, string pointer)
    {
        WithPack((path, trinket) =>
        {
            trinket[field] = value;
            WriteTrinket(path, trinket);
            var error = Assert.Throws<ContentException>(() => RulesPack.Load([path]));
            Assert.Contains(error.Errors, e => e.File.EndsWith("trinkets.json", StringComparison.Ordinal) && e.Pointer == pointer);
        });
    }

    [Fact]
    public void Trinket_changes_affect_the_rules_hash()
    {
        WithPack((path, trinket) =>
        {
            var before = RulesPack.Load([path]).Hash;
            trinket["static"]!["attack"] = 2;
            WriteTrinket(path, trinket);
            Assert.NotEqual(before, RulesPack.Load([path]).Hash);
        });
    }

    private static void WriteTrinket(string path, JsonNode trinket)
        => File.WriteAllText(Path.Combine(path, "trinkets.json"), new JsonArray(trinket.DeepClone()).ToJsonString());

    private static void WithPack(Action<string, JsonNode> run)
    {
        var root = Directory.CreateTempSubdirectory("tactics-trinkets-");
        try
        {
            var source = ContentPaths.Fixture("minimal", "base");
            var path = Path.Combine(root.FullName, "base");
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(path, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var manifestPath = Path.Combine(path, "manifest.json");
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!;
            manifest["files"]!.AsArray().Add("trinkets.json");
            File.WriteAllText(manifestPath, manifest.ToJsonString());
            var trinket = JsonNode.Parse("""{"id":"test_gauntlet","name":"Test gauntlet","class":"fighter","rarity":"rare","static":{"attack":1},"text":"Attack +1."}""")!;
            WriteTrinket(path, trinket);
            run(path, trinket);
        }
        finally { root.Delete(recursive: true); }
    }
}
