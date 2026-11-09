using System.Collections.Immutable;
using System.Text;
using ArcBox.Sdk.Content;

namespace Tactics.Logic.Tests;

public sealed class PurityBoundaryTests
{
    private sealed class MemoryFiles(Dictionary<string, string> files) : IFileSource
    {
        public List<string> Reads { get; } = [];
        public bool Exists(string path) => files.ContainsKey(path);
        public byte[] ReadAllBytes(string path) { Reads.Add(path); return Encoding.UTF8.GetBytes(files[path]); }
        public string ReadAllText(string path) => Encoding.UTF8.GetString(ReadAllBytes(path));
        public string[] GetDirectories(string path) => [];
        public Stream OpenRead(string path) => new MemoryStream(ReadAllBytes(path), writable: false);
    }

    [Fact]
    public void Logic_references_no_game_presentation_or_Godot_assembly()
        => Assert.DoesNotContain(typeof(Battle).Assembly.GetReferencedAssemblies(), a =>
            a.Name!.Contains("Godot", StringComparison.OrdinalIgnoreCase) || a.Name is "Tactics.Game" or "Tactics.Presentation");

    [Fact]
    public void State_unit_iteration_is_guaranteed_sorted_by_its_public_type()
    {
        Assert.Equal(typeof(ImmutableSortedDictionary<UnitId, UnitState>), typeof(BattleState).GetProperty(nameof(BattleState.Units))!.PropertyType);
        foreach (var type in new[] { typeof(BattleState), typeof(UnitState), typeof(UnitSheet) })
            Assert.DoesNotContain(type.GetProperties(), p => p.PropertyType.IsGenericType &&
                new[] { typeof(Dictionary<,>), typeof(ImmutableDictionary<,>), typeof(HashSet<>) }.Contains(p.PropertyType.GetGenericTypeDefinition()));
    }

    [Fact]
    public void Rule_and_art_loaders_accept_content_without_touching_a_physical_directory()
    {
        var path = Path.Combine("virtual-content", "rules");
        var files = new MemoryFiles(new()
        {
            [Path.Combine(path, "manifest.json")] = """{"id":"memory","rulesVersion":1,"requires":[],"files":[]}""",
        });
        var rules = RulesPack.Load([path], source: files);
        Assert.Empty(rules.Encounters);
        Assert.Matches("^[0-9a-f]{64}$", rules.Hash);
        Assert.Single(files.Reads);
        Assert.Empty(ArtManifests.Load("virtual-art", files).Keys);
    }

    [Fact]
    public void Lore_and_lazy_clip_measurement_keep_the_injected_source()
    {
        var root = "virtual-lore";
        var files = new MemoryFiles(new()
        {
            [Path.Combine(root, "lore", "pack.json")] = """{"files":["arcs.json","scenes/test.json"]}""",
            [Path.Combine(root, "lore", "arcs.json")] = """{"arcs":[{"id":"chapter","chapter":1,"region":"memory","title":"Memory","beats":[{"id":"beat","kind":"story","place":"start","requires":[],"sets":[]}]}]}""",
            [Path.Combine(root, "lore", "scenes", "test.json")] = """{"scenes":[{"id":"scene","beat":"beat","title":"Memory","speakers":["narrator"],"start":"start","nodes":[{"id":"start","end":true,"lines":[{"id":"line","speaker":"narrator","text":"Hello.","mood":"idle"}]}]}]}""",
            [Path.Combine(root, "lore", "audio", "manifest.json")] = """{"lines":{"line":{"main":"line.ogg"}}}""",
            [Path.Combine(root, "lore", "audio", "line.ogg")] = "invalid ogg",
        });
        var lore = LoreBook.Load(root, files);
        Assert.Single(lore.Arcs);
        Assert.Single(lore.Scenes);
        Assert.Null(lore.ClipSeconds("line"));
        Assert.Null(lore.ClipSeconds("line"));
        Assert.Equal(1, files.Reads.Count(p => p.EndsWith("line.ogg", StringComparison.Ordinal)));
    }
}
