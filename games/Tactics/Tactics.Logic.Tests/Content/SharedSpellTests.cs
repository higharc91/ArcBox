using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

/// <summary>Same-named class spells share mechanics; presentation and the class's resource vocabulary stay their own.</summary>
public sealed class SharedSpellTests
{
    private static RulesPack Rules => BasePackTests.Rules;
    private static readonly string[] Priority = ["wizard", "cleric", "ranger", "bard", "druid", "paladin", "sorcerer", "warlock"];

    // Pact slots are a separate, small replenishing pool. These exact class-cost adaptations are intentional;
    // they do not change a spell's effect, action economy, range, area, save or duration.
    private static readonly IReadOnlyDictionary<string, int> PactCosts = new Dictionary<string, int>
    {
        ["warlock.shatter"] = 2, ["warlock.misty_step"] = 1, ["warlock.hold_person"] = 1,
        ["warlock.counterspell"] = 1, ["warlock.blight"] = 1, ["warlock.dimension_door"] = 1, ["warlock.hold_monster"] = 1,
    };

    private static IEnumerable<IGrouping<string, AbilityDef>> Shared()
    {
        var root = Path.Combine(ContentPaths.ContentBase, "rules", "abilities");
        var ids = Rules.Classes.Keys.SelectMany(cls =>
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, cls + ".json")));
            return doc.RootElement.GetProperty("abilities").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToArray();
        });
        return ids.Select(id => Rules.Abilities[new AbilityId(id)]).GroupBy(a => a.Name).Where(g => g.Count() > 1);
    }

    private static JsonNode Mechanics(AbilityDef ability)
    {
        var json = JsonSerializer.SerializeToNode(ability, ArcJson.Options)!.AsObject();
        foreach (var key in new[] { "id", "name", "presentation", "ai", "flavor", "replaces" }) json.Remove(key);
        var cost = json["cost"]!.AsObject();
        if (cost["resource"] is JsonObject resource)
        {
            Assert.Single(resource);
            var value = resource.Single();
            if (value.Key == "pact")
            {
                Assert.True(PactCosts.TryGetValue(ability.Id.Value, out var expected), $"Undocumented pact adaptation: {ability.Id}");
                Assert.Equal(expected, value.Value!.GetValue<int>());
                cost["resource"] = new JsonObject { ["classPool"] = ability.Tier };
            }
            else cost["resource"] = new JsonObject { ["classPool"] = value.Value!.DeepClone() };
        }
        void RemoveLocalIds(JsonNode? node)
        {
            if (node is JsonObject obj)
            {
                obj.Remove("id"); // inline zone-passive identity is namespaced per caster class
                foreach (var child in obj.ToArray()) RemoveLocalIds(child.Value);
            }
            else if (node is JsonArray arr) foreach (var child in arr) RemoveLocalIds(child);
        }
        RemoveLocalIds(json["effects"]);
        return json;
    }

    [Fact]
    public void Every_shared_spell_has_identical_mechanics_except_documented_class_resource_adaptations()
    {
        var groups = Shared().ToArray();
        Assert.Equal(46, groups.Length);
        foreach (var group in groups)
        {
            var versions = group.OrderBy(a => Array.IndexOf(Priority, a.Id.Value.Split('.')[0]) is < 0 ? int.MaxValue : Array.IndexOf(Priority, a.Id.Value.Split('.')[0])).ToArray();
            var expected = Mechanics(versions[0]);
            foreach (var other in versions.Skip(1))
                Assert.True(JsonNode.DeepEquals(expected, Mechanics(other)), $"Undocumented shared-spell difference: {versions[0].Id} / {other.Id}");
        }
        Assert.All(PactCosts.Keys, id => Assert.Contains(groups.SelectMany(g => g), a => a.Id.Value == id));
    }

    [Theory]
    [InlineData("druid.cure_wounds", "2d8")]
    [InlineData("druid.healing_word", "2d4")]
    [InlineData("druid.mass_cure_wounds", "5d8")]
    public void Druid_healing_uses_the_shared_SRD_dice_at_every_level(string id, string dice)
    {
        var heal = Assert.IsType<HealEffect>(Assert.Single(Rules.Abilities[new AbilityId(id)].Effects));
        for (var level = 1; level <= 10; level++) Assert.Equal(Dice.Parse(dice), heal.Dice!.At(level));
    }

    [Theory]
    [InlineData("Shatter")]
    [InlineData("Thunderwave")]
    [InlineData("Hypnotic Pattern")]
    public void Shared_area_control_spells_do_not_silently_spare_allies_for_one_class(string spell)
        => Assert.All(Shared().Single(g => g.Key == spell), a => Assert.True(a.Target.Area.FriendlyFire, a.Id.Value));
}
