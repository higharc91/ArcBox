using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;

namespace Tactics.Logic.Tests;

public sealed class EnvironmentContentTests
{
    [Fact]
    public void Base_pack_has_the_agreed_rules_but_no_encounter_is_opted_in()
    {
        var rules = BasePackTests.Rules;
        Assert.Contains("planks/*", rules.Environment.Fuel);
        Assert.Contains("grass/dry", rules.Environment.Fuel);
        Assert.Equal(6, rules.Environment.Reactions.Single(r => r.Id == "conduct").Conduct!.Reach);
        Assert.Equal(2, rules.Environment.Surfaces["burning"].Rounds);
        Assert.Equal(3, rules.Environment.Surfaces["ice_sheet"].Rounds);
        Assert.Equal(10, rules.Environment.Surfaces["oil"].Rounds);
        Assert.Null(rules.Environment.Surfaces["scorched"].Rounds);
        Assert.All(rules.Encounters.Values, e => Assert.False(e.Environment));
    }

    [Theory]
    [InlineData("family", "/fuel/0")]
    [InlineData("surface", "/reactions/0/surface")]
    [InlineData("damage", "/reactions/0/damage")]
    [InlineData("duration", "/surfaces/burning/rounds")]
    [InlineData("condition", "/surfaces/burning/passives/0/effects/0/apply")]
    [InlineData("renewable", "/surfaces/burning/leaves")]
    public void Bad_environment_references_have_precise_json_pointers(string mutation, string pointer)
    {
        using var pack = new EnvironmentPack(root =>
        {
            switch (mutation)
            {
                case "family": root["fuel"]![0] = "__missing__/*"; break;
                case "surface": root["reactions"]![0]!["surface"] = "__missing__"; break;
                case "damage": root["reactions"]![0]!["damage"] = "__missing__"; break;
                case "duration": root["surfaces"]!["burning"]!["rounds"] = 0; break;
                case "renewable": root["surfaces"]!["scorched"]!["fuel"] = true; break;
                case "condition": root["surfaces"]!["burning"]!["passives"]![0]!["effects"] = JsonNode.Parse("[{\"apply\":\"__missing__\"}]"); break;
            }
        });
        var error = Assert.Throws<ContentException>(() => pack.Load());
        Assert.Contains(error.Errors, e => e.File == "base/environment.json" && e.Pointer == pointer);
    }

    [Fact]
    public void Large_connected_fuel_patches_warn_without_preventing_load()
    {
        using var pack = new EnvironmentPack(root => root["fuel"] = JsonNode.Parse("[\"grass/*\"]"));
        var rules = pack.Load();
        Assert.Contains(rules.Warnings, w => w.Message.Contains("fuel", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Surface_and_encounter_flags_preserve_old_json_when_absent()
    {
        var zone = new Zone(0, null, "fire", [new Tile(0, 0)], 2, false, false, []);
        var json = JsonSerializer.Serialize(zone, ArcJson.Options);
        Assert.DoesNotContain("surface", json, StringComparison.Ordinal);
        Assert.DoesNotContain("source", json, StringComparison.Ordinal);
        var restored = JsonSerializer.Deserialize<Zone>(json, ArcJson.Options)!;
        Assert.False(restored.Surface);
        Assert.Null(restored.Source);
        var state = TestBattle.On("0. 0.").Party("p", TestSheets.Fighter, new Tile(0, 0))
            .Enemy("e", TestSheets.Goblin, new Tile(1, 0)).Start().State;
        Assert.False(state.Environment);
        Assert.DoesNotContain("\"environment\"", JsonSerializer.Serialize(state, ArcJson.Options), StringComparison.Ordinal);
    }

    [Fact]
    public void Standalone_rules_validate_material_references_without_requiring_external_art()
    {
        using var valid = new EnvironmentPack(_ => { });
        Assert.Equal(6, valid.Load(withArt: false).Environment.Reactions.Length);
        using var invalid = new EnvironmentPack(root => root["fuel"]![0] = "__missing__/*");
        var error = Assert.Throws<ContentException>(() => invalid.Load(withArt: false));
        Assert.Contains(error.Errors, e => e.Pointer == "/fuel/0");
    }

    private sealed class EnvironmentPack : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "arcbox-environment-" + Guid.NewGuid().ToString("N"));
        public EnvironmentPack(Action<JsonObject> edit)
        {
            var source = ContentPaths.BaseRules;
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(_root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target);
            }
            var environment = JsonNode.Parse(File.ReadAllText(Path.Combine(_root, "environment.json")))!.AsObject();
            edit(environment);
            File.WriteAllText(Path.Combine(_root, "environment.json"), environment.ToJsonString());
        }
        public RulesPack Load(bool withArt = true) => RulesPack.Load([_root], withArt ? ArtManifests.Load(ContentPaths.ContentBase) : null);
        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
