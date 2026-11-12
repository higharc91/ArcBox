using System.Text.Json;

namespace Tactics.Logic.Tests;

public sealed class MonsterRosterTests
{
    [Fact]
    public void Every_bestiary_creature_has_a_buildable_signature_move_and_existing_art()
    {
        var rules = BasePackTests.Rules;
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(ContentPaths.ContentBase, "bestiary", "manifest.json")));
        foreach (var creature in manifest.RootElement.GetProperty("creatures").EnumerateObject())
        {
            var monster = rules.Monsters[creature.Name];
            Assert.StartsWith("bestiary." + creature.Name + ".", monster.Sprite);
            var sheet = SheetBuilder.ForMonster(creature.Name, rules);
            Assert.NotEmpty(sheet.Abilities);
            Assert.All(sheet.Abilities, a => Assert.NotEmpty(rules.Abilities[a].Effects));
        }
    }

    [Fact]
    public void Each_encounter_and_size_variant_has_an_ability_or_active_passive()
    {
        var rules = BasePackTests.Rules;
        foreach (var enemy in rules.Encounters.Values.SelectMany(e => e.Enemies))
        foreach (var id in new[] { enemy.Unit }.Concat(enemy.UnitByParty?.Values ?? []))
        {
            Assert.False(id.StartsWith("form_", StringComparison.Ordinal) || id.StartsWith("summon_", StringComparison.Ordinal));
            var monster = rules.Monsters[id];
            Assert.True(monster.Abilities.Length > 0 || monster.Passives.Any(p => rules.Passives[p].Hook is not null && !rules.Passives[p].Effects.IsEmpty), id);
        }
    }

    [Fact]
    public void All_player_forms_have_independent_explicit_hp_through_level_ten()
    {
        var rules = BasePackTests.Rules;
        foreach (var id in new[] { "form_wolf", "form_bear", "form_dire_wolf", "form_elder_bear", "summon_wolf" })
        {
            var definition = rules.Monsters[id];
            Assert.Equal(Enumerable.Range(1, 10), definition.Hp.Steps.Keys);
            var hp = Enumerable.Range(1, 10).Select(level => SheetBuilder.ForMonster(id, rules, level).MaxHp).ToArray();
            Assert.All(hp, value => Assert.True(value > 0));
            Assert.True(hp.Zip(hp.Skip(1)).All(pair => pair.First < pair.Second));
            Assert.Equal(0, definition.Xp);
        }
        Assert.Equal(26, SheetBuilder.ForMonster("wolf", rules, 3).MaxHp);
        Assert.Equal(17, SheetBuilder.ForMonster("form_wolf", rules, 3).MaxHp);
        Assert.Equal(18, SheetBuilder.ForMonster("summon_wolf", rules, 3).MaxHp);
    }
    [Theory]
    [InlineData("cr", "nonsense", "/0/cr")]
    [InlineData("xp", -1, "/0/xp")]
    public void Monster_metadata_is_validated_at_its_content_pointer(string field, object invalid, string pointer)
    {
        var temp = Directory.CreateTempSubdirectory("arcbox-monster-validation-");
        try
        {
            var root = Path.Combine(ContentPaths.ContentBase, "rules");
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(temp.FullName, Path.GetRelativePath(root, path));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(path, target);
            }
            var file = Path.Combine(temp.FullName, "monsters", "bestiary.json");
            var data = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(file))!;
            data[0]![field] = System.Text.Json.Nodes.JsonValue.Create(invalid);
            File.WriteAllText(file, data.ToJsonString());
            var errors = Assert.Throws<ContentException>(() => RulesPack.Load([temp.FullName])).Errors;
            Assert.Contains(errors, e => e.Pointer == pointer);
        }
        finally { temp.Delete(true); }
    }

    [Theory]
    [InlineData("crab")]
    [InlineData("gargoyle")]
    [InlineData("harpy")]
    [InlineData("hookbeak")]
    [InlineData("scorpion")]
    [InlineData("warg")]
    [InlineData("wisp")]
    public void Bestiary_signature_actions_are_legal_and_resolve_in_battle(string id)
    {
        var rules = BasePackTests.Rules;
        var monster = SheetBuilder.ForMonster(id, rules) with { InitiativeBonus = 100 };
        var fixture = TestBattle.On("0. 0. 0.\n0. 0. 0.\n0. 0. 0.")
            .With([.. rules.Abilities.Values]).WithPassives([.. rules.Passives.Values]).WithConditions([.. rules.Conditions.Values])
            .Enemy("foe", monster, new Tile(1, 1)).Party("hero", TestSheets.Fighter, new Tile(1, 2));
        var state = fixture.Start().State;
        var result = Battle.Apply(state, new Use(new UnitId("foe"), monster.Abilities[0], new Target(Unit: new UnitId("hero"))), fixture.Rules);
        Assert.Null(result.Rejected);
        Assert.NotEmpty(result.Events);
    }
}
