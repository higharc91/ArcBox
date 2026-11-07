using System.Text.Json.Nodes;

namespace Tactics.Logic.Tests;

public sealed class SupportTargetValidationTests
{
    [Theory]
    [InlineData("self", "burst", "[{\"statMod\":{\"mods\":{\"attack\":2},\"rounds\":2}}]")]
    [InlineData("self", "burst", "[{\"tempHp\":{\"dice\":\"1d8\"}}]")]
    [InlineData("tile", "burst", "[{\"heal\":{\"dice\":\"1d8\"}}]")]
    [InlineData("enemy", "single", "[{\"attack\":{\"kind\":\"spell\"},\"onHit\":[{\"heal\":{\"dice\":\"1d8\"}}]}]")]
    [InlineData("any_unit", "single", "[{\"repeat\":{\"times\":2},\"effects\":[{\"heal\":{\"dice\":\"1d8\"}}]}]")]
    public void Beneficial_effects_cannot_reach_hostiles_even_through_branches(string kind, string shape, string effects)
    {
        using var pack = new Pack(kind, shape, effects);
        var error = Assert.Throws<ContentException>(() => pack.Load());
        Assert.Contains(error.Errors, e => e.Message.Contains("beneficial effect", StringComparison.Ordinal) && e.Pointer.StartsWith("/abilities/5/effects/", StringComparison.Ordinal));
    }

    [Fact]
    public void Zone_healing_requires_an_allied_passive_scope_regardless_of_the_ability_target()
    {
        const string effects = """[{"zone":{"kind":"grove","area":{"shape":"burst","size":1},"rounds":3,"passives":[{"id":"mend","hook":"onTurnStart","target":"self","effects":[{"heal":{"dice":"1d6"}}]}]}}]""";
        using var unsafePack = new Pack("self", "self", effects);
        var error = Assert.Throws<ContentException>(() => unsafePack.Load());
        Assert.Contains(error.Errors, e => e.Pointer.EndsWith("/zone/passives/0/effects/0/heal", StringComparison.Ordinal));
        using var safe = new Pack("self", "self", effects, a => a["effects"]![0]!["zone"]!["affects"] = "allies");
        Assert.NotNull(safe.Load());
    }

    [Theory]
    [InlineData("self", "self", "[{\"heal\":{\"dice\":\"1d8\"}}]")]
    [InlineData("enemy", "single", "[{\"heal\":{\"dice\":\"1d8\",\"self\":true}}]")]
    [InlineData("enemy", "single", "[{\"statMod\":{\"mods\":{\"attack\":-2},\"rounds\":2}}]")]
    public void Self_heals_and_hostile_debuffs_are_valid(string kind, string shape, string effects)
    {
        using var pack = new Pack(kind, shape, effects);
        Assert.NotNull(pack.Load());
    }

    [Fact]
    public void Explicit_allied_tile_area_is_valid_and_unknown_filters_are_rejected()
    {
        const string effects = """[{"heal":{"dice":"1d8"}}]""";
        using var safe = new Pack("tile", "burst", effects, a => a["target"]!["area"]!["affects"] = "allies");
        Assert.NotNull(safe.Load());
        using var typo = new Pack("tile", "burst", effects, a => a["target"]!["area"]!["affects"] = "aliies");
        Assert.Throws<ContentException>(() => typo.Load());
    }

    [Fact]
    public void A_self_targeted_cast_cannot_hide_an_unfiltered_healing_hazard()
    {
        using var pack = new Pack("self", "self", """[{"hazard":{"hazardArt":"hazards.spikes","area":{"shape":"single"},"rounds":2},"onEnter":[{"heal":{"dice":"1d6"}}]}]""");
        var error = Assert.Throws<ContentException>(() => pack.Load());
        Assert.Contains(error.Errors, e => e.Pointer.EndsWith("/onEnter/0/heal", StringComparison.Ordinal) && e.Message.Contains("hostile", StringComparison.Ordinal));
    }

    private sealed class Pack : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "arcbox-support-" + Guid.NewGuid().ToString("N"));
        public Pack(string kind, string shape, string effects, Action<JsonObject>? edit = null)
        {
            var source = ContentPaths.Fixture("minimal", "base");
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(root, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            var path = Path.Combine(root, "abilities", "test.json");
            var doc = JsonNode.Parse(File.ReadAllText(path))!;
            var ability = JsonNode.Parse("""{"id":"test.support","name":"Support","tier":0,"cost":{"action":1},"target":{"kind":"self","range":0,"los":false,"area":{"shape":"single","size":1,"includeSelf":true}},"effects":[]} """)!.AsObject();
            ability["target"]!["kind"] = kind;
            ability["target"]!["area"]!["shape"] = shape;
            ability["effects"] = JsonNode.Parse(effects);
            edit?.Invoke(ability);
            doc["abilities"]!.AsArray().Add(ability);
            File.WriteAllText(path, doc.ToJsonString());
        }
        public RulesPack Load() => RulesPack.Load([root]);
        public void Dispose() => Directory.Delete(root, recursive: true);
    }
}
