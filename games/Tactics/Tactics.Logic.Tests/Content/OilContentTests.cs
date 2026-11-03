using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class OilContentTests
{
    [Theory]
    [InlineData("damage", "conditions.json", "/damageTaken/type")]
    [InlineData("bonus", "conditions.json", "/damageTaken/bonus")]
    [InlineData("surface", "abilities/common.json", "/effects/0/zone/kind")]
    [InlineData("loot", "encounters/ford_assault.json", "/itemLoot/0/item")]
    [InlineData("count", "encounters/ford_assault.json", "/itemLoot/0/count")]
    public void Invalid_oil_definitions_report_the_exact_reference_or_value(string change, string file, string suffix)
    {
        var root = Path.Combine(Path.GetTempPath(), "oil-validation-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var source in Directory.GetFiles(ContentPaths.BaseRules, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(ContentPaths.BaseRules, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
            }
            var path = Path.Combine(root, file); var json = JsonNode.Parse(File.ReadAllText(path))!;
            JsonNode row = change switch
            {
                "damage" or "bonus" => json.AsArray().Single(n => n!["id"]!.GetValue<string>() == "oiled")!,
                "surface" => json["abilities"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "item.oil_flask")!,
                _ => json,
            };
            switch (change)
            {
                case "damage": row["damageTaken"]!["type"] = "__missing__"; break;
                case "bonus": row["damageTaken"]!["bonus"] = -1; break;
                case "surface": row["effects"]![0]!["zone"]!["kind"] = "__missing__"; break;
                case "loot": row["itemLoot"]![0]!["item"] = "__missing__"; break;
                case "count": row["itemLoot"]![0]!["count"] = 0; break;
            }
            File.WriteAllText(path, json.ToJsonString());
            var error = Assert.Throws<ContentException>(() => RulesPack.Load([root]));
            Assert.Contains(error.Errors, e => e.File.EndsWith(file, StringComparison.Ordinal) && e.Pointer.EndsWith(suffix, StringComparison.Ordinal));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Surface_effects_round_trip_and_old_zones_keep_their_json()
    {
        EffectDef old = new ZoneEffect("web", new AreaDef(AreaShape.Burst, 1), 10, []);
        Assert.DoesNotContain("surface", JsonSerializer.Serialize(old, ArcJson.Options));
        EffectDef oil = new ZoneEffect("oil", new AreaDef(AreaShape.Burst, 1), 10, [], Surface: true);
        var json = JsonSerializer.Serialize(oil, ArcJson.Options);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<EffectDef>(json, ArcJson.Options), ArcJson.Options));
        var rules = BasePackTests.Rules;
        Assert.Contains("5 extra fire", Tooltips.For(rules.Conditions[new ConditionId("oiled")], rules));
        Assert.Contains("10 rounds", Tooltips.For(rules.Abilities[new AbilityId("item.oil_flask")], 1, rules));
    }
}
