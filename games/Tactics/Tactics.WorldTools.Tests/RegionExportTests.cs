using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

public sealed class RegionExportTests : IDisposable
{
    private const string Grass = "grass/lush";

    private static readonly string TerrainManifest = Path.Combine(ContentPaths.ContentBase, "terrain", "manifest.json");

    private static readonly Lazy<RulesPack> Base = new(() => RulesPack.Load([ContentPaths.BaseRules]));

    /// <summary>A copy of the base rules: an export writes into its pack, so each test gets its own.</summary>
    private readonly string _rules = CopyRules();

    public void Dispose() => Directory.Delete(_rules, recursive: true);

    private static string CopyRules()
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

    private ExportResult Export(PaintedRegion painted) => RegionExport.Export(painted, _rules, TerrainManifest);

    private void ListInManifest(string id = "r")
    {
        var path = Path.Combine(_rules, "manifest.json");
        var manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest["files"]!.AsArray().Add($"world/{id}.json");
        File.WriteAllText(path, manifest.ToJsonString());
    }

    /// <summary>A 6x6 field of grass: a start and a recovery on its top row, and a beat in the far corner.</summary>
    private static PaintedRegion Field(string id = "r") => new(id, "Region", 1, 6, 6)
    {
        Materials = Enumerable.Range(0, 36).ToDictionary(i => new Tile(i % 6, i / 6), _ => Grass),
        Starts = [new Tile(0, 0)],
        Recoveries = [new Tile(5, 0)],
        Beats = [new BeatSpot("ch1_ember", new Tile(5, 5))],
    };

    /// <summary>A field with river_crossing painted in at (4, 4), as a scene holds it: tiles in the layers, plus a marker.</summary>
    private static PaintedRegion Stamped()
    {
        var map = Base.Value.MapFor("river_crossing")!;
        var (width, height) = (map.Width + 8, map.Height + 8);
        var materials = new Dictionary<Tile, string>();
        var heights = new Dictionary<Tile, int>();
        var flags = new Dictionary<Tile, TileFlags>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                materials[new Tile(x, y)] = Grass;
        for (var y = 0; y < map.Height; y++)
            for (var x = 0; x < map.Width; x++)
            {
                var (tile, at) = (map[new Tile(x, y)], new Tile(x + 4, y + 4));
                materials[at] = map.Materials[tile.Material];
                heights[at] = tile.Height;
                flags[at] = tile.Flags;
            }
        return new PaintedRegion("r", "Region", 1, width, height)
        {
            Materials = materials,
            Heights = heights,
            Flags = flags,
            Starts = [new Tile(0, 0)],
            Recoveries = [new Tile(1, 0)],
            Stamps = [new RegionStamp("river_crossing", new Tile(4, 4), 0)],
        };
    }

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
        var result = Export(Field() with { Width = 12, Height = 12, Beats = [new BeatSpot("ch1_ember", new Tile(9, 9))] });

        Assert.Null(result.Written);
        Assert.Equal(["The beat 'ch1_ember' at (9, 9) can't be reached from the start."], result.Problems);
        Assert.False(File.Exists(RegionFile()));
    }

    [Fact]
    public void A_stamped_tile_painted_over_is_refused_and_nothing_is_written()
    {
        var stamped = Stamped();
        var materials = new Dictionary<Tile, string>(stamped.Materials) { [new Tile(4, 4)] = "ash/grey" };

        var result = Export(stamped with { Materials = materials });

        Assert.Null(result.Written);
        Assert.Contains("river_crossing", Assert.Single(result.Problems), StringComparison.Ordinal);
        Assert.Contains("(4, 4)", result.Problems[0], StringComparison.Ordinal);
        Assert.False(File.Exists(RegionFile()));
    }

    [Fact]
    public void A_stamp_whose_map_is_missing_is_refused()
    {
        var result = Export(Field() with { Stamps = [new RegionStamp("nowhere", new Tile(0, 0), 0)] });

        Assert.Null(result.Written);
        Assert.Contains("nowhere", Assert.Single(result.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void A_material_outside_the_terrain_manifest_is_refused_naming_the_tile()
    {
        var field = Field();
        var materials = new Dictionary<Tile, string>(field.Materials) { [new Tile(2, 3)] = "lava/hot" };

        var result = Export(field with { Materials = materials });

        Assert.Null(result.Written);
        Assert.Equal(["The tile (2, 3) uses the material 'lava/hot', which isn't in the terrain manifest."], result.Problems);
    }

    [Fact]
    public void A_refused_export_keeps_the_file_already_there()
    {
        Assert.Empty(Export(Field()).Problems);
        var before = File.ReadAllBytes(RegionFile());

        var result = Export(Field() with { Beats = [new BeatSpot("ch1_ember", new Tile(50, 50))] });

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

        var result = Export(Field() with { Beats = [new BeatSpot("ch1_ember", new Tile(50, 50))] });

        Assert.Equal(["The beat 'ch1_ember' at (50, 50) can't be reached from the start."], result.Problems);
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
        var result = Export(Field() with { Starts = [new Tile(0, 0), new Tile(1, 1)], Beats = [new BeatSpot("ch1_ember", new Tile(5, 50))] });

        Assert.Null(result.Written);
        Assert.Equal(
            [
                "The scene has 2 start spawns, at (0, 0) and (1, 1), but needs one.",
                "The beat 'ch1_ember' at (5, 50) can't be reached from the start.",
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
        Assert.Equal([$"The region id '{id}' must be lower-case letters, digits and underscores."], result.Problems);
        Assert.False(File.Exists(Path.Combine(_rules, "escape.json")));
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
    public void Each_chunk_is_on_its_own_line_so_a_small_paint_change_is_a_small_diff()
    {
        var field = new PaintedRegion("r", "Region", 1, 64, 32)
        {
            Materials = Enumerable.Range(0, 64 * 32).ToDictionary(i => new Tile(i % 64, i / 64), _ => Grass),
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

        Export(Field() with { Beats = [new BeatSpot("ch1_ember", new Tile(50, 50))] });
        Export(Field());

        Assert.Subset(before, Directory.GetDirectories(temp, "tactics-region-check-*").ToHashSet());
    }
}
