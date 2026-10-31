using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

public sealed class RegionExportTests : IDisposable
{
    private const string Grass = "grass/lush", Beat = "ch1_mayors_request";

    private const string Unreachable = "Paint a walkable way to it, climbing no more than 2 levels a step, or move the marker.";

    private static readonly Lazy<RulesPack> Base = new(() => RulesPack.Load([ContentPaths.BaseRules]));

    /// <summary>A copy of the base rules: an export writes into its pack, so each test gets its own.</summary>
    private readonly string _rules = CopyRules();

    public void Dispose() => Directory.Delete(_rules, recursive: true);

    internal static string CopyRules()
    {
        var root = Directory.CreateTempSubdirectory("tactics-export-test-").FullName;
        foreach (var file in Directory.EnumerateFiles(ContentPaths.BaseRules, "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(root, Path.GetRelativePath(ContentPaths.BaseRules, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        return root;
    }

    private string RegionFile(string id = "r") => Path.Combine(_rules, "world", $"{id}.json");

    private ExportResult Export(PaintedRegion painted) => RegionExport.Export(painted, _rules, ContentPaths.ContentBase);

    private void ListInManifest(string id = "r")
    {
        var path = Path.Combine(_rules, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["files"]!.AsArray().Add($"world/{id}.json");
        File.WriteAllText(path, manifest.ToJsonString());
    }

    private static Dictionary<Tile, string> Grid(int width, int height, string material = Grass)
        => Enumerable.Range(0, width * height).ToDictionary(i => new Tile(i % width, i / width), _ => material);

    /// <summary>A 6x6 field of grass: a start and a recovery on its top row, and a beat in the far corner.</summary>
    private static PaintedRegion Field(string id = "r") => new(id, "Region", 1, 6, 6)
    {
        Materials = Grid(6, 6),
        Starts = [new Tile(0, 0)],
        Recoveries = [new Tile(5, 0)],
        Beats = [new BeatSpot(Beat, new Tile(5, 5))],
    };

    /// <summary>A 12x12 field cut in two by an impassable wall down column 7, with the beat on the far side.</summary>
    private static PaintedRegion Walled() => Field() with
    {
        Width = 12,
        Height = 12,
        Materials = Grid(12, 12),
        Flags = Enumerable.Range(0, 12).ToDictionary(y => new Tile(7, y), _ => TileFlags.Impassable),
        Beats = [new BeatSpot(Beat, new Tile(9, 9))],
    };

    /// <summary>A field with <paramref name="map"/> painted in at (4, 4), as a scene holds it: tiles in the layers, plus a marker.</summary>
    private static PaintedRegion Stamped(BattleMap map)
    {
        var (width, height) = (map.Width + 8, map.Height + 8);
        var materials = Grid(width, height);
        var heights = new Dictionary<Tile, int>();
        var flags = new Dictionary<Tile, TileFlags>();
        foreach (var cell in StampBrush.Cells(map, new Tile(4, 4), 0))
        {
            materials[cell.At] = cell.Material;
            heights[cell.At] = cell.Height;
            flags[cell.At] = cell.Flags;
        }
        return new PaintedRegion("r", "Region", 1, width, height)
        {
            Materials = materials,
            Heights = heights,
            Flags = flags,
            Starts = [new Tile(0, 0)],
            Recoveries = [new Tile(1, 0)],
            Stamps = [new RegionStamp(map.Id, new Tile(4, 4), 0)],
        };
    }

    private static PaintedRegion Stamped() => Stamped(Base.Value.MapFor("river_crossing")!);

    [Fact]
    public void A_clean_region_is_written_where_the_pack_expects_it()
    {
        var result = Export(Field());

        Assert.Empty(result.Problems);
        Assert.Equal(RegionFile(), result.Written);
        var def = JsonSerializer.Deserialize<RegionDef>(File.ReadAllBytes(RegionFile()), ArcJson.Options)!;
        Assert.Equal(("r", 1), (def.Id, def.Chapter));
    }

    [Fact]
    public void A_written_region_loads_once_the_manifest_lists_it()
    {
        Assert.Empty(Export(Stamped()).Problems);
        ListInManifest();

        var region = RulesPack.Load([_rules]).RegionFor("r")!;

        Assert.Equal(new RegionStamp("river_crossing", new Tile(4, 4), 0), Assert.Single(region.Def.Stamps));
    }

    [Fact]
    public void The_export_leaves_the_manifest_alone()
    {
        var before = File.ReadAllBytes(Path.Combine(_rules, "manifest.json"));

        Assert.Empty(Export(Field()).Problems);

        Assert.Equal(before, File.ReadAllBytes(Path.Combine(_rules, "manifest.json")));
    }

    [Fact]
    public void A_beat_cut_off_from_the_start_is_refused_and_nothing_is_written()
    {
        var result = Export(Walled());

        Assert.Null(result.Written);
        Assert.Equal([$"The beat '{Beat}' at (9, 9) can't be reached from the start. {Unreachable}"], result.Problems);
        Assert.False(File.Exists(RegionFile()));
    }

    [Fact]
    public void A_stamped_tile_painted_over_is_refused_and_nothing_is_written()
    {
        var stamped = Stamped();
        var materials = new Dictionary<Tile, string>(stamped.Materials) { [new Tile(4, 4)] = "ash/grey" };

        var result = Export(stamped with { Materials = materials });

        Assert.Null(result.Written);
        Assert.Equal(
            ["The stamp 'river_crossing' doesn't match its map at (4, 4): the region has material 'ash/grey', the map has 'grass/lush'. Select its marker and press Apply stamp to put the map back."],
            result.Problems);
        Assert.False(File.Exists(RegionFile()));
    }

    [Fact]
    public void Ten_painted_over_stamp_tiles_are_one_line_with_a_count()
    {
        var stamped = Stamped();
        var materials = new Dictionary<Tile, string>(stamped.Materials);
        for (var x = 4; x < 14; x++)
            materials[new Tile(x, 4)] = "ash/grey";

        var problem = Assert.Single(Export(stamped with { Materials = materials }).Problems);

        Assert.Contains("9 more tiles differ, x 4 to 13, y 4 to 4.", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stamp_whose_map_is_missing_is_refused()
    {
        var result = Export(Field() with { Stamps = [new RegionStamp("nowhere", new Tile(0, 0), 0)] });

        Assert.Null(result.Written);
        Assert.Equal(["Unknown map 'nowhere'. Set the marker's Map to a file in rules/maps."], result.Problems);
    }

    [Fact]
    public void A_material_nobody_can_draw_is_refused_naming_the_tile()
    {
        var field = Field();
        var materials = new Dictionary<Tile, string>(field.Materials) { [new Tile(2, 3)] = "lava/hot" };

        var result = Export(field with { Materials = materials });

        Assert.Null(result.Written);
        Assert.Equal(["The tile (2, 3) uses the material 'lava/hot', which isn't in the palette or a stamped map. Repaint it from the palette."], result.Problems);
    }

    [Fact]
    public void A_stamped_maps_own_materials_are_known()
    {
        // crypt_tile/dark is no terrain top: only a map names it, and the game resolves it for drawing.
        var map = Base.Value.Maps.Values.First(m => m.Materials.Contains("crypt_tile/dark"));

        Assert.Empty(Export(Stamped(map) with { Starts = PartySpawn(map), Recoveries = PartySpawn(map) }).Problems);
    }

    private static ImmutableArray<Tile> PartySpawn(BattleMap map)
        => [new Tile(map.Zones["party_spawn"][0].X + 4, map.Zones["party_spawn"][0].Y + 4)];

    public static TheoryData<string> Maps() => [.. Base.Value.Maps.Keys];

    [Theory]
    [MemberData(nameof(Maps))]
    public void Every_map_in_the_base_pack_can_be_stamped_and_exported(string id)
    {
        var map = Base.Value.MapFor(id)!;

        var result = Export(Stamped(map) with { Starts = PartySpawn(map), Recoveries = PartySpawn(map) });

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Written);
    }

    public static TheoryData<string> Palette() => [.. PaletteBuilder.AllSwatches(ContentPaths.ContentBase).Select(s => s.Material)];

    [Theory]
    [MemberData(nameof(Palette))]
    public void Every_palette_swatch_exports_with_no_stamp(string material)
    {
        var field = Field();
        var materials = new Dictionary<Tile, string>(field.Materials) { [new Tile(2, 3)] = material };

        Assert.Empty(Export(field with { Materials = materials }).Problems);
    }

    [Fact]
    public void A_pending_old_stamp_warns_but_the_file_is_written()
    {
        var map = Base.Value.MapFor("river_crossing")!;
        var old = new RegionStamp("river_crossing", new Tile(4, 4), 0);
        var moved = new RegionStamp("river_crossing", new Tile(6, 4), 0);

        var result = Export(Stamped(map) with { Width = map.Width + 8, Stamps = [], OldStamps = [old], Heights = new Dictionary<Tile, int>(), Flags = new Dictionary<Tile, TileFlags>() });
        var covered = Export(Stamped(map) with { Stamps = [moved], OldStamps = [old], Heights = new Dictionary<Tile, int>(), Flags = new Dictionary<Tile, TileFlags>() });

        Assert.NotNull(result.Written);
        Assert.Equal(["168 tiles, x 4 to 17, y 4 to 15, first (4, 4), still hold the old stamp 'river_crossing'. Repaint them, then press Done repainting."], result.Warnings);
        Assert.Equal($"Wrote {RegionFile()}. Add \"world/r.json\" to rules/manifest.json, or the game won't load it. 1 old stamp area still needs repainting.", result.Status);
        Assert.Contains("24 tiles, x 4 to 5, y 4 to 15, first (4, 4)", Assert.Single(covered.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void An_old_stamp_the_current_stamps_cover_doesnt_warn()
    {
        var old = new RegionStamp("river_crossing", new Tile(4, 4), 0);

        Assert.Empty(Export(Stamped() with { OldStamps = [old] }).Warnings);
    }

    [Fact]
    public void A_refused_export_still_shows_its_warnings()
    {
        var result = Export(Field() with { Starts = [], OldStamps = [new RegionStamp("gone", new Tile(1, 1), 0)] });

        Assert.Null(result.Written);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void A_moved_node_row_has_no_tile_to_go_to()
    {
        var problem = Assert.Single(Export(Field() with { MovedNodes = ["Beats"] }).Problems);

        Assert.Null(RegionExport.TileIn(problem));
    }

    [Theory]
    [InlineData(0, 0, false)]        // A moment ago, on a clock too coarse to show the save: give it the benefit of the doubt.
    [InlineData(-3600, -3600, true)] // The file is an hour old and didn't change.
    [InlineData(3600, 3600, true)]   // Ahead of the clock and didn't change, so nothing wrote it.
    [InlineData(3600, 0, false)]     // Ahead of the clock, and a real save moved it back: the original bug.
    [InlineData(-3600, 0, false)]    // Moved forward.
    public void A_save_is_judged_by_whether_the_file_changed(int beforeSeconds, int afterSeconds, bool failed)
    {
        var now = new DateTime(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);

        Assert.Equal(failed, RegionExport.SaveFailed(now.AddSeconds(beforeSeconds), now.AddSeconds(afterSeconds), now));
    }

    [Fact]
    public void A_read_only_file_elsewhere_in_the_pack_leaves_no_temporary_pack_behind()
    {
        var file = Path.Combine(_rules, "xp.json");
        File.SetAttributes(file, FileAttributes.ReadOnly);
        var temp = Path.GetTempPath();
        var before = Directory.GetDirectories(temp, "tactics-region-check-*").ToHashSet();

        var result = Export(Field());
        File.SetAttributes(file, FileAttributes.Normal);

        Assert.Empty(result.Problems);
        Assert.Subset(before, Directory.GetDirectories(temp, "tactics-region-check-*").ToHashSet());
    }

    [Fact]
    public void A_read_only_region_file_is_a_problem_not_a_crash()
    {
        Assert.Empty(Export(Field()).Problems);
        File.SetAttributes(RegionFile(), FileAttributes.ReadOnly);

        var result = Export(Field());
        File.SetAttributes(RegionFile(), FileAttributes.Normal);

        if (OperatingSystem.IsWindows())
            Assert.Contains("then export again.", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_locked_region_file_is_a_problem_not_a_crash()
    {
        if (!OperatingSystem.IsWindows())
            return;
        Assert.Empty(Export(Field()).Problems);
        using var hold = File.Open(RegionFile(), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = Export(Field());

        Assert.Contains("then export again.", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_locked_file_elsewhere_in_the_pack_is_a_problem_not_a_crash()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var hold = File.Open(Path.Combine(_rules, "xp.json"), FileMode.Open, FileAccess.Read, FileShare.None);

        var result = Export(Field());

        Assert.Null(result.Written);
        Assert.Contains("xp.json", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_manifest_is_a_problem_not_a_crash()
    {
        File.WriteAllText(Path.Combine(_rules, "manifest.json"), "not json");

        var result = Export(Field());

        Assert.Null(result.Written);
        Assert.Contains("then export again.", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_refused_export_keeps_the_file_already_there()
    {
        Assert.Empty(Export(Field()).Problems);
        var before = File.ReadAllBytes(RegionFile());

        var result = Export(Field() with { Beats = [new BeatSpot(Beat, new Tile(50, 50))] });

        Assert.NotEmpty(result.Problems);
        Assert.Equal(before, File.ReadAllBytes(RegionFile()));
    }

    [Fact]
    public void A_region_the_manifest_already_lists_can_be_exported_again()
    {
        // Once the region is in the pack, checking it on top of that pack would find its id twice.
        Assert.Empty(Export(Field()).Problems);
        ListInManifest();

        var result = Export(Field() with { Name = "Renamed" });

        Assert.Empty(result.Problems);
        Assert.Equal("Renamed", RulesPack.Load([_rules]).RegionFor("r")!.Def.Name);
    }

    [Fact]
    public void A_listed_region_is_checked_as_the_new_one_not_the_old()
    {
        Assert.Empty(Export(Field()).Problems);
        ListInManifest();

        var result = Export(Field() with { Beats = [new BeatSpot(Beat, new Tile(50, 50))] });

        Assert.Equal([$"The beat '{Beat}' at (50, 50) can't be reached from the start. {Unreachable}"], result.Problems);
    }

    [Fact]
    public void A_broken_file_already_there_doesnt_stop_a_good_export()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(RegionFile())!);
        File.WriteAllText(RegionFile(), "not json");
        ListInManifest();

        Assert.Empty(Export(Field()).Problems);
    }

    [Fact]
    public void A_problem_found_reading_the_scene_is_listed_with_the_checks_and_stops_the_write()
    {
        var result = Export(Field() with { Starts = [new Tile(0, 0), new Tile(1, 1)], Beats = [new BeatSpot(Beat, new Tile(5, 50))] });

        Assert.Null(result.Written);
        Assert.Equal(
            [
                "The scene has 2 start spawns, at (0, 0) and (1, 1), but needs one. Delete the extra.",
                $"The beat '{Beat}' at (5, 50) can't be reached from the start. {Unreachable}",
            ],
            result.Problems);
        Assert.False(File.Exists(RegionFile()));
    }

    [Fact]
    public void A_problem_elsewhere_in_the_pack_says_which_file()
    {
        File.WriteAllText(Path.Combine(_rules, "xp.json"), "not json");

        var problem = Assert.Single(Export(Field()).Problems);

        Assert.StartsWith("base/xp.json: ", problem, StringComparison.Ordinal);
        Assert.EndsWith("Fix that file, then export again.", problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("Ashen Ford")]
    public void An_id_that_cant_be_a_file_name_is_refused(string id)
    {
        var result = Export(Field(id));

        Assert.Null(result.Written);
        Assert.Equal([$"The region id '{id}' must be lower-case letters, digits and underscores. Change Id on the Region node."], result.Problems);
        Assert.False(File.Exists(Path.Combine(_rules, "escape.json")));
    }

    [Fact]
    public void A_bad_id_doesnt_hide_a_missing_start()
    {
        var problems = Export(Field("Bad Id") with { Starts = [] }).Problems;

        Assert.Equal(
            [
                "The region id 'Bad Id' must be lower-case letters, digits and underscores. Change Id on the Region node.",
                "The scene has no start spawn. Add a SpawnMarker under Spawns and set Kind to Start.",
            ],
            problems);
    }

    [Fact]
    public void A_bad_id_doesnt_hide_a_cut_off_beat()
        => Assert.Contains($"The beat '{Beat}' at (9, 9) can't be reached from the start. {Unreachable}", Export(Walled() with { Id = "Bad Id" }).Problems);

    [Fact]
    public void Tiles_outside_the_region_dont_hide_a_cut_off_beat()
    {
        var walled = Walled();
        var materials = new Dictionary<Tile, string>(walled.Materials);
        for (var y = 0; y < 12; y++)
            for (var x = 12; x < 17; x++)
                materials[new Tile(x, y)] = Grass;

        var problems = Export(walled with { Materials = materials }).Problems;

        Assert.Equal(
            [
                "60 tiles, x 12 to 16, y 0 to 11, first (12, 0), are outside the 12x12 region. Erase what's painted there, or change Size on the Region node.",
                $"The beat '{Beat}' at (9, 9) can't be reached from the start. {Unreachable}",
            ],
            problems);
    }

    [Fact]
    public void An_unknown_material_doesnt_hide_a_cut_off_beat()
    {
        var walled = Walled();
        var materials = new Dictionary<Tile, string>(walled.Materials) { [new Tile(1, 1)] = "lava/hot" };

        var problems = Export(walled with { Materials = materials }).Problems;

        Assert.Equal(2, problems.Length);
        Assert.Contains("lava/hot", problems[0], StringComparison.Ordinal);
        Assert.Contains("can't be reached", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_material_on_a_stamped_tile_isnt_also_reported_as_a_stamp_mismatch()
    {
        var stamped = Stamped();
        var materials = new Dictionary<Tile, string>(stamped.Materials) { [new Tile(4, 4)] = "lava/hot" };

        var problem = Assert.Single(Export(stamped with { Materials = materials }).Problems);

        Assert.Contains("lava/hot", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_material_on_a_stamped_tile_doesnt_hide_a_changed_height()
    {
        var stamped = Stamped();
        var materials = new Dictionary<Tile, string>(stamped.Materials) { [new Tile(4, 4)] = "lava/hot" };
        var heights = new Dictionary<Tile, int>(stamped.Heights) { [new Tile(4, 4)] = 7 };

        var problems = Export(stamped with { Materials = materials, Heights = heights }).Problems;

        Assert.Equal(2, problems.Length);
        Assert.Contains("the region has height 7", problems[1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_hole_in_a_chunk_is_refused()
    {
        var field = Field();
        var materials = new Dictionary<Tile, string>(field.Materials);
        materials.Remove(new Tile(2, 2));

        var result = Export(field with { Materials = materials });

        Assert.Null(result.Written);
        Assert.StartsWith("The tile (2, 2) has no material", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void No_start_means_no_phantom_lines_from_tile_0_0()
    {
        var field = Field();
        var materials = new Dictionary<Tile, string>(field.Materials);

        var flags = new Dictionary<Tile, TileFlags> { [new Tile(0, 0)] = TileFlags.Impassable };

        Assert.Equal(["The scene has no start spawn. Add a SpawnMarker under Spawns and set Kind to Start."],
            Export(field with { Materials = materials, Flags = flags, Starts = [] }).Problems);
    }

    [Fact]
    public void No_recovery_means_no_phantom_line_for_tile_0_0()
    {
        var walled = Walled();

        // The start is across the wall from (0, 0), so a recovery defaulting there would be reported as cut off.
        var problems = Export(walled with { Starts = [new Tile(9, 9)], Recoveries = [] }).Problems;

        Assert.Equal(["The scene has no recovery spawn. Add a SpawnMarker under Spawns and set Kind to Recovery."], problems);
    }

    [Fact]
    public void A_beat_the_lore_doesnt_have_is_a_problem()
    {
        var result = Export(Field() with { Beats = [new BeatSpot("ch1_mayors_requst", new Tile(5, 5))] });

        Assert.Null(result.Written);
        Assert.Equal(["The beat 'ch1_mayors_requst' at (5, 5) isn't in lore/arcs.json for chapter 1. Check its spelling."], result.Problems);
    }

    [Fact]
    public void A_beat_from_another_chapter_is_a_problem()
        => Assert.Equal([$"The beat '{Beat}' at (5, 5) isn't in lore/arcs.json for chapter 2. Check its spelling."],
            Export(Field() with { Chapter = 2 }).Problems);

    [Fact]
    public void A_good_export_says_if_the_manifest_doesnt_list_the_region_yet()
    {
        var result = Export(Field());

        Assert.False(result.Listed);
        Assert.Equal($"Wrote {RegionFile()}. Add \"world/r.json\" to rules/manifest.json, or the game won't load it.", result.Status);
    }

    [Fact]
    public void A_good_export_of_a_listed_region_just_says_where()
    {
        ListInManifest();
        Directory.CreateDirectory(Path.GetDirectoryName(RegionFile())!);
        File.WriteAllText(RegionFile(), "{}");

        var result = Export(Field());

        Assert.True(result.Listed);
        Assert.Equal($"Wrote {RegionFile()}.", result.Status);
    }

    [Fact]
    public void A_refused_export_counts_its_problems()
        => Assert.Equal("Not exported: 1 problem. Click it to go there.", Export(Walled()).Status);

    [Fact]
    public void A_refused_export_with_no_place_to_go_doesnt_offer_a_click()
        => Assert.Equal("Not exported: 1 problem.", Export(Field() with { Starts = [] }).Status);

    [Fact]
    public void A_file_that_cant_be_written_is_a_problem_not_a_crash()
    {
        // A folder where the file should go stops the move on every platform, as a locked file does on Windows.
        Directory.CreateDirectory(RegionFile());
        var before = Directory.GetFiles(Path.Combine(_rules, "world"));

        var result = Export(Field());

        Assert.Null(result.Written);
        Assert.Contains("then export again.", Assert.Single(result.Problems), StringComparison.Ordinal);
        Assert.Equal(before, Directory.GetFiles(Path.Combine(_rules, "world")));
    }

    [Fact]
    public void Exporting_twice_gives_the_same_bytes()
    {
        Assert.Empty(Export(Stamped()).Problems);
        var first = File.ReadAllBytes(RegionFile());

        Assert.Empty(Export(Stamped()).Problems);

        Assert.Equal(first, File.ReadAllBytes(RegionFile()));
    }

    [Fact]
    public void An_export_leaves_no_temporary_file_beside_the_region()
    {
        var before = Directory.GetFiles(Path.Combine(_rules, "world"));

        Assert.Empty(Export(Field()).Problems);

        Assert.Equal([.. before, RegionFile()], Directory.GetFiles(Path.Combine(_rules, "world")).OrderBy(f => f, StringComparer.Ordinal));
    }

    [Fact]
    public void Each_chunk_is_on_its_own_line_so_a_small_paint_change_is_a_small_diff()
    {
        var field = new PaintedRegion("r", "Region", 1, 64, 32)
        {
            Materials = Grid(64, 32),
            Starts = [new Tile(0, 0)],
            Recoveries = [new Tile(1, 0)],
        };
        Assert.Empty(Export(field).Problems);
        var before = File.ReadAllLines(RegionFile());
        Assert.Equal(2, before.Count(line => line.Contains("\"tiles\"", StringComparison.Ordinal)));

        Assert.Empty(Export(field with { Heights = new Dictionary<Tile, int> { [new Tile(40, 3)] = 1 } }).Problems);
        var after = File.ReadAllLines(RegionFile());

        Assert.Equal(before.Length, after.Length);
        Assert.Single(before.Zip(after), pair => pair.First != pair.Second);
    }

    [Fact]
    public void The_file_is_utf8_with_lf_endings_and_no_bom()
    {
        Assert.Empty(Export(Field() with { Name = "Emberwick’s Reach" }).Problems);

        var bytes = File.ReadAllBytes(RegionFile());

        Assert.NotEqual((byte)0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal((byte)'\n', bytes[^1]);
        Assert.Equal("Emberwick’s Reach", JsonSerializer.Deserialize<RegionDef>(bytes, ArcJson.Options)!.Name);
    }

    [Fact]
    public void Checking_leaves_no_temporary_pack_behind()
    {
        var temp = Path.GetTempPath();
        var before = Directory.GetDirectories(temp, "tactics-region-check-*").ToHashSet();

        Export(Field() with { Beats = [new BeatSpot(Beat, new Tile(50, 50))] });
        Export(Field());

        Assert.Subset(before, Directory.GetDirectories(temp, "tactics-region-check-*").ToHashSet());
    }

    [Theory]
    [InlineData("The tile (2, 3) uses the material 'x'.", 2, 3)]
    [InlineData("600 tiles, x 40 to 44, y 0 to 9, first (40, 0), are outside the 40x30 region.", 40, 0)]
    [InlineData("The stamp 'x' doesn't match its map at (4, 5): the region has void there. 3 more tiles differ, x 4 to 5, y 5 to 6.", 4, 5)]
    [InlineData("The beat 'x' at (-1, 7) can't be reached from the start.", -1, 7)]
    public void A_problem_gives_the_tile_to_go_to(string problem, int x, int y)
        => Assert.Equal(new Tile(x, y), RegionExport.TileIn(problem));

    [Fact]
    public void A_problem_with_no_tile_gives_none()
        => Assert.Null(RegionExport.TileIn("The scene has no start spawn."));

    [Fact]
    public void A_region_that_breaks_the_story_is_written_with_warnings_that_say_what_to_do()
    {
        var field = Field("ashen_ford");
        var result = Export(field with { Beats = [.. field.Beats, new BeatSpot("ch1_ford_assault", new Tile(3, 3))] });

        Assert.Empty(result.Problems);
        Assert.Equal(RegionFile("ashen_ford"), result.Written);
        Assert.Empty(result.Warnings);
        Assert.Contains(result.StoryWarnings, w => w.Contains("ch1_the_ford_choice", StringComparison.Ordinal) && w.Contains("Add a BeatMarker", StringComparison.Ordinal));
        Assert.Contains(result.StoryWarnings, w => w.Contains("ch1_ford_assault", StringComparison.Ordinal) && w.Contains("Add a StampMarker", StringComparison.Ordinal));
        Assert.All(result.StoryWarnings, w => Assert.True(w.Trim().Split(". ").Length >= 2, w));
        Assert.Contains($"{result.StoryWarnings.Length} story warnings", result.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void One_story_warning_reads_as_one()
    {
        var result = new ExportResult("f", []) { Listed = true, StoryWarnings = ["The beat 'x' has no spot in the region. Add it."] };
        Assert.Equal("Wrote f. 1 story warning.", result.Status);
    }

    [Fact]
    public void A_region_no_arc_names_has_no_story_to_check()
        => Assert.Empty(Export(Field()).StoryWarnings);

    [Fact]
    public void A_refused_export_doesnt_check_the_story()
    {
        var result = Export(Field("ashen_ford") with { Recoveries = [] });

        Assert.NotEmpty(result.Problems);
        Assert.Empty(result.StoryWarnings);
    }

    [Fact]
    public void Every_kind_of_problem_says_what_to_do()
    {
        var walled = Walled();
        var problems = Export(walled with
        {
            Id = "Bad",
            Chapter = 0,
            Starts = [],
            Recoveries = [],
            Stamps = [new RegionStamp("nowhere", new Tile(0, 0), 0), new RegionStamp("river_crossing", new Tile(0, 0), 0), new RegionStamp("river_crossing", new Tile(100, 0), 0)],
            Beats = [new BeatSpot("", new Tile(0, 0))],
        }).Problems;

        Assert.All(problems, p => Assert.True(p.Trim().Split(". ").Length >= 2, p));
    }
}
