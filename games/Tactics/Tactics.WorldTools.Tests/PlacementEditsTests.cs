using System.Text.Json.Nodes;
using Tactics.Logic;
using Tactics.Logic.Tests;
namespace Tactics.WorldTools.Tests;

public sealed class PlacementEditsTests
{
    [Fact]
    public void Ashen_Fords_existing_placements_round_trip_without_changing_any_file()
    {
        var source = PlacementEdits.Read(ContentPaths.BaseRules, "ashen_ford");
        Assert.True(source.Points.Count(p => p.Kind == PlacementKind.Interactable) >= 30);
        Assert.Equal(5, source.Points.Count(p => p.Kind == PlacementKind.MobPatch));
        Assert.Equal(3, source.Shops.Length);
        Assert.Empty(source.Plan(source.Points));
    }

    [Fact]
    public void A_moved_thing_changes_only_its_coordinates_and_the_second_export_is_byte_identical()
    {
        var rules = RegionExportTests.CopyRules();
        try
        {
            var source = PlacementEdits.Read(rules, "ashen_ford");
            var thing = source.Points.First(p => p.Kind == PlacementKind.Interactable);
            var moved = source.Points.Select(p => p == thing ? p with { At = new Tile(p.At.X + 1, p.At.Y) } : p).ToArray();
            var edits = source.Plan(moved);
            var file = Assert.Single(edits);
            var before = JsonNode.Parse(File.ReadAllBytes(Path.Combine(rules, file.Key)))!;
            var after = JsonNode.Parse(file.Value)!;
            var entry = after["interactables"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == thing.Id)!;
            Assert.Equal(thing.At.X + 1, entry["at"]![0]!.GetValue<int>());
            entry["at"] = new JsonArray(thing.At.X, thing.At.Y);
            Assert.True(JsonNode.DeepEquals(before, after));
            File.WriteAllBytes(Path.Combine(rules, file.Key), file.Value);
            Assert.Empty(PlacementEdits.Read(rules, "ashen_ford").Plan(moved));
            Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(rules, file.Key)));
        }
        finally { Directory.Delete(rules, true); }
    }

    [Fact]
    public void Moving_a_patch_translates_every_tile_and_preserves_shape_and_authored_order()
    {
        var source = PlacementEdits.Read(ContentPaths.BaseRules, "ashen_ford");
        var patch = source.Points.First(p => p.Kind == PlacementKind.MobPatch);
        var edits = source.Plan(source.Points.Select(p => p == patch ? p with { At = new Tile(p.At.X + 2, p.At.Y - 1) } : p));
        var entry = JsonNode.Parse(Assert.Single(edits).Value)!["mobs"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == patch.Id)!;
        var tiles = entry["patch"]!.AsArray().Select(n => new Tile(n![0]!.GetValue<int>(), n[1]!.GetValue<int>())).ToArray();
        Assert.Equal(patch.Offsets.Select(t => new Tile(patch.At.X + 2 + t.X, patch.At.Y - 1 + t.Y)), tiles);
    }

    [Fact]
    public void Missing_unknown_or_duplicate_markers_cannot_silently_erase_authored_content()
    {
        var source = PlacementEdits.Read(ContentPaths.BaseRules, "ashen_ford");
        Assert.Throws<InvalidDataException>(() => source.Plan(source.Points.Skip(1)));
        Assert.Throws<InvalidDataException>(() => source.Plan(source.Points.Append(source.Points[0])));
        Assert.Throws<InvalidDataException>(() => source.Plan(source.Points.Append(new(PlacementKind.Interactable, "missing", new Tile(1, 1)))));
        Assert.Empty(source.Plan(source.Points));
    }
    [Fact]
    public void Shop_indices_follow_the_labelled_keeper_without_changing_stock_or_dialogue()
    {
        var source = PlacementEdits.Read(ContentPaths.BaseRules, "ashen_ford");
        var labels = source.Shops.ToDictionary(s => s.Id, s => s.Person);
        Assert.Empty(source.PlanShops(labels));
        labels[source.Shops[0].Id]++;
        var update = Assert.Single(source.PlanShops(labels));
        var original = JsonNode.Parse(File.ReadAllBytes(Path.Combine(ContentPaths.BaseRules, update.Key)))!;
        var changed = JsonNode.Parse(update.Value)!;
        var shop = changed["shops"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == source.Shops[0].Id)!;
        Assert.Equal(labels[source.Shops[0].Id], shop["keeper"]!["person"]!.GetValue<int>());
        shop["keeper"]!["person"] = source.Shops[0].Person;
        Assert.True(JsonNode.DeepEquals(original, changed));
        Assert.Throws<InvalidDataException>(() => source.PlanShops(new Dictionary<string, int>()));
    }

    [Fact]
    public void Invalid_placement_pack_is_rejected_before_any_production_file_changes()
    {
        var rules = RegionExportTests.CopyRules();
        try
        {
            var scene = SceneText.Read(Path.Combine(ContentPaths.RepoRoot, "tools/tactics-world/regions/ashen_ford.tscn"),
                Path.Combine(ContentPaths.RepoRoot, "tools/tactics-world/palette/materials.tres"));
            var source = PlacementEdits.Read(rules, "ashen_ford");
            var point = source.Points.First(p => p.Kind == PlacementKind.Interactable);
            var updates = source.Plan(source.Points.Select(p => p == point ? p with { At = new Tile(-1, -1) } : p));
            var paths = updates.Keys.Append("world/ashen_ford.json").ToDictionary(p => p, p => File.ReadAllBytes(Path.Combine(rules, p)));
            var result = RegionExport.Export(scene, rules, ContentPaths.ContentBase, updates);
            Assert.Null(result.Written); Assert.NotEmpty(result.Problems);
            foreach (var (path, bytes) in paths) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(rules, path)));
        }
        finally { Directory.Delete(rules, true); }
    }

}
