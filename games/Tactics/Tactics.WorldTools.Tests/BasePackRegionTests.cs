using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

/// <summary>
/// The regions the base pack ships. These read the exported files and the saved scenes, not <see cref="AshenFordDraft"/>, so they keep
/// meaning something once a person has painted over the draft.
/// </summary>
public sealed class BasePackRegionTests
{
    private static readonly Lazy<RulesPack> Pack = new(() => RulesPack.Load([ContentPaths.BaseRules], ArtManifests.Load(ContentPaths.ContentBase)));

    private static string World(params string[] parts) => Path.Combine([ContentPaths.RepoRoot, "tools", "tactics-world", .. parts]);

    private static PaintedRegion Scene(string id) => SceneText.Read(World("regions", $"{id}.tscn"), World("palette", "materials.tres"));

    public static TheoryData<string> Regions() => [.. Pack.Value.Regions.Keys];

    [Fact]
    public void The_pack_has_regions_to_check() => Assert.NotEmpty(Regions());

    /// <summary>
    /// Every arc in the lore replayed on Ashen Ford's ground, renamed for the arc's region, as a painted region of its own will be:
    /// whatever the ground lacks is a problem to report, never a throw. <c>ch4_pip_remembers</c> has no place, which once threw.
    /// </summary>
    [Fact]
    public void Every_arc_in_the_lore_can_be_replayed()
    {
        var ford = Pack.Value.RegionFor("ashen_ford")!.Def;
        var arcs = LoreBook.Load(ContentPaths.ContentBase).Arcs;
        Assert.Contains(arcs, a => a.Beats.Any(b => b.Place is null));

        foreach (var arc in arcs)
        {
            var story = StoryOrder.Check(WorldRegion.From(ford with { Id = arc.Region, Chapter = arc.Chapter }), Pack.Value, ContentPaths.ContentBase);

            if (arc.Region == ford.Id)
                Assert.NotEmpty(story.Legs);
            else
                Assert.Contains(story.Problems, p => p.Contains($"'{arc.Beats[^1].Id}' has no spot", StringComparison.Ordinal));
        }
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void A_region_plays_in_story_order_on_every_branch(string id)
    {
        var story = StoryOrder.Check(Pack.Value.RegionFor(id)!, Pack.Value, ContentPaths.ContentBase);

        Assert.Empty(story.Problems);
        Assert.NotEmpty(story.Legs);
        Assert.All(story.Legs, leg => Assert.NotNull(leg.Seconds));
        Assert.All(story.Retries, leg => Assert.NotNull(leg.Seconds));

        var branches = story.Legs.SelectMany(l => l.Branch.Split('/')).ToHashSet(StringComparer.Ordinal);
        Assert.All(Choices(id), choice => Assert.Contains(choice, branches));
    }

    /// <summary>Every option of every choice in the arcs set in a region.</summary>
    private static List<string> Choices(string id)
    {
        using var lore = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContentPaths.ContentBase, "lore", "arcs.json")));
        var choices = lore.RootElement.GetProperty("arcs").EnumerateArray().Where(a => a.GetProperty("region").GetString() == id)
            .SelectMany(a => a.GetProperty("beats").EnumerateArray())
            .Where(b => b.TryGetProperty("choices", out _))
            .SelectMany(b => b.GetProperty("choices").EnumerateArray())
            .Select(c => c.GetProperty("id").GetString()!).ToList();
        if (choices.Count == 0)
            Assert.True(lore.RootElement.GetProperty("arcs").EnumerateArray().Single(a => a.GetProperty("region").GetString() == id).TryGetProperty("sideAdventure", out var side) && side.GetBoolean(), "Only a linear side adventure has no main-story vote.");
        return choices;
    }

    [Theory]
    [MemberData(nameof(Regions))]
    public void A_region_file_is_what_its_scene_exports(string id)
    {
        var rules = RegionExportTests.CopyRules();
        try
        {
            var result = RegionExport.Export(Scene(id), rules, ContentPaths.ContentBase);
            Assert.Empty(result.Problems);
            Assert.Empty(result.Warnings);
            Assert.Empty(result.StoryWarnings);

            var exported = File.ReadAllBytes(result.Written!);
            var committed = File.ReadAllBytes(Path.Combine(ContentPaths.BaseRules, "world", $"{id}.json"));
            Assert.True(exported.AsSpan().SequenceEqual(committed),
                $"rules/world/{id}.json isn't what regions/{id}.tscn exports. It's generated, so don't edit it: open the scene and press Export region.");
        }
        finally
        {
            Directory.Delete(rules, recursive: true);
        }
    }
}
