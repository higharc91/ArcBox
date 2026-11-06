using System.Text.Json;

namespace Tactics.Logic.Tests.Content;

/// <summary>
/// Regions against the lore. Lore isn't part of the rules pack, so the pack loader can't see it: <c>lore/arcs.json</c> is read here, and
/// by the export, and nowhere in the loader.
/// </summary>
public sealed class RegionLoreTests
{
    private static RulesPack Rules => BasePackTests.Rules;

    private static IEnumerable<JsonElement> Arcs()
    {
        using var lore = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(ContentPaths.ContentBase, "lore", "arcs.json")));
        return [.. lore.RootElement.GetProperty("arcs").EnumerateArray().Select(arc => arc.Clone())];
    }

    private static IEnumerable<(RegionDef Region, JsonElement Beat)> ChapterBeats()
    {
        Assert.NotEmpty(Rules.Regions);
        foreach (var region in Rules.Regions.Values.Select(r => r.Def))
            foreach (var arc in Arcs().Where(a => a.GetProperty("region").GetString() == region.Id))
                foreach (var beat in arc.GetProperty("beats").EnumerateArray())
                    yield return (region, beat);
    }

    [Fact]
    public void Every_beat_of_a_regions_chapter_has_a_spot()
    {
        var beats = ChapterBeats().ToList();
        Assert.NotEmpty(beats);
        foreach (var (region, beat) in beats)
        {
            var id = beat.GetProperty("id").GetString();
            Assert.True(region.Beats.Any(b => b.Beat == id), $"The beat '{id}' has no spot in {region.Id}.");
        }
    }

    [Fact]
    public void Every_battle_beats_encounter_has_a_stamp()
    {
        var battles = ChapterBeats().Where(b => b.Beat.GetProperty("kind").GetString() == "battle").ToList();
        Assert.NotEmpty(battles);
        foreach (var (region, beat) in battles)
        {
            var encounter = beat.GetProperty("encounter").GetString()!;
            Assert.True(Rules.Encounters.ContainsKey(encounter), $"The beat '{beat.GetProperty("id").GetString()}' names an unknown encounter '{encounter}'.");
            Assert.True(region.Stamps.Any(s => s.Map == Rules.Encounters[encounter].Map), $"The encounter '{encounter}' has no stamp in {region.Id}.");
        }
    }
}
