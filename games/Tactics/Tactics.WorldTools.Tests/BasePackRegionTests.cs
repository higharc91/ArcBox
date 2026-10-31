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

    [Theory]
    [MemberData(nameof(Regions))]
    public void A_region_plays_in_story_order_on_every_branch(string id)
    {
        var story = StoryOrder.Check(Pack.Value.RegionFor(id)!, Pack.Value, ContentPaths.ContentBase);

        Assert.Empty(story.Problems);
        Assert.NotEmpty(story.Legs);
        Assert.All(story.Legs, leg => Assert.NotNull(leg.Seconds));
        Assert.All(story.Retries, leg => Assert.NotNull(leg.Seconds));
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
