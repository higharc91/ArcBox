using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class CompanionNameTests
{
    [Theory]
    [InlineData("  Épine   d'Or ", "Épine d'Or")]
    [InlineData("白 桜", "白 桜")]
    [InlineData("", null)]
    [InlineData("   ", null)]
    public void Names_are_normalized_without_changing_their_script(string raw, string? expected)
    {
        var result = CompanionNames.Normalize(raw);
        Assert.Null(result.Problem);
        Assert.Equal(expected, result.Name);
    }

    [Theory]
    [InlineData("Hound7")]
    [InlineData("Ash🐕")]
    [InlineData("Ash\nEmber")]
    [InlineData("abcdefghijklmnopq")]
    [InlineData("---")]
    public void Invalid_names_are_refused(string raw) => Assert.NotNull(CompanionNames.Normalize(raw).Problem);

    [Theory]
    [InlineData("ranger", "beastmaster", "hound")]
    [InlineData("druid", "grovekeeper", "sapling")]
    [InlineData("warlock", "fiendbound", "imp")]
    public void Only_the_persistent_subclass_companion_inherits_the_name_and_survives_a_save(string cls, string sub, string monster)
    {
        var rules = BasePackTests.Rules;
        var character = SheetBuilder.DefaultCharacter("Mara", "human", cls, $"{cls}.{sub}", 3, rules);
        var party = Enumerable.Range(1, 4).Select(i => new PartyMember(new UnitId($"hero{i}"),
            i == 1 ? character with { CompanionName = "abcdefghijklmnop" } : SheetBuilder.DefaultCharacter($"Hero{i}", "human", "fighter", null, 3, rules), new AiControlled("companion"))).ToImmutableArray();
        var setup = Encounters.Setup("river_crossing", party, Difficulty.Normal, rules);
        var state = Battle.Start(setup, rules, 1).State;
        var companion = Assert.Single(state.Units.Values, u => u.SummonedBy == new UnitId("hero1"));
        Assert.Equal("abcdefghijklmnop", companion.Sheet.Name);
        Assert.Equal($"Mara's {monster}", companion.Sheet.CompanionOwner);
        var restored = JsonSerializer.Deserialize<BattleState>(JsonSerializer.Serialize(state, ArcJson.Options), ArcJson.Options)!;
        Assert.Equal(companion.Sheet.Name, restored.Units[companion.Id].Sheet.Name);
        Assert.Equal(companion.Sheet.CompanionOwner, restored.Units[companion.Id].Sheet.CompanionOwner);
        var writer = new BattleWriter(state, rules);
        var temporary = Summons.Spawn(writer, new UnitId("hero1"), monster, new Tile(0, 0), 3, true);
        Assert.Equal(rules.Monsters[monster].Name, writer.State.Units[temporary].Sheet.Name);
        Assert.Null(writer.State.Units[temporary].Sheet.CompanionOwner);
        var oldJson = JsonSerializer.Serialize(character, ArcJson.Options);
        Assert.DoesNotContain("companionName", oldJson);
        Assert.Equal(oldJson, JsonSerializer.Serialize(JsonSerializer.Deserialize<CharacterRecord>(oldJson, ArcJson.Options), ArcJson.Options));
    }
}
