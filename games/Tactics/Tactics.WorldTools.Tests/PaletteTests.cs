using System.Text.Json;
using Tactics.Logic;
using Tactics.Logic.Tests;

namespace Tactics.WorldTools.Tests;

public sealed class PaletteTests
{
    private const string Small = """
        { "textures": {
            "wood": { "kind": "top", "variants": { "pale": "top/wood_pale.png", "dark": "top/wood_dark.png" } },
            "brick": { "kind": "side", "variants": { "red": "side/brick_red.png" } },
            "ash": { "kind": "top", "variants": { "grey": "top/ash_grey.png" } }
        } }
        """;

    private static readonly string TerrainManifest = Path.Combine(ContentPaths.ContentBase, "terrain", "manifest.json");

    private static readonly Lazy<RulesPack> Base = new(() => RulesPack.Load([ContentPaths.BaseRules]));

    /// <summary>Runs <paramref name="read"/> on a temporary manifest holding <paramref name="json"/>.</summary>
    private static T WithManifest<T>(string json, Func<string, T> read)
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, json);
            return read(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void The_palette_lists_every_material_in_the_terrain_manifest()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(TerrainManifest));
        var tops = manifest.RootElement.GetProperty("textures").EnumerateObject()
            .Where(t => t.Value.GetProperty("kind").GetString() == "top")
            .SelectMany(t => t.Value.GetProperty("variants").EnumerateObject().Select(v => $"{t.Name}/{v.Name}"));

        var materials = PaletteBuilder.Materials(TerrainManifest);

        Assert.Equal(tops.Order(StringComparer.Ordinal), materials);
        Assert.Contains("grass/lush", materials);
    }

    [Theory]
    [InlineData("ash")]
    [InlineData("basalt")]
    [InlineData("forest_floor")]
    public void The_palette_has_the_new_biome_tops(string family)
        => Assert.Contains(PaletteBuilder.Materials(TerrainManifest), m => m.StartsWith(family + "/", StringComparison.Ordinal));

    [Fact]
    public void Only_tops_are_painted_and_they_go_by_ordinal_name()
        => Assert.Equal(["ash/grey", "wood/dark", "wood/pale"], WithManifest(Small, PaletteBuilder.Materials));

    [Fact]
    public void Each_swatch_names_its_texture_and_a_flat_file_name()
        => Assert.Equal(new MaterialSwatch("wood/pale", "top/wood_pale.png", "wood_pale.png"), WithManifest(Small, PaletteBuilder.Swatches)[2]);

    [Fact]
    public void Every_swatch_of_the_real_manifest_has_a_texture_and_its_own_file()
    {
        var swatches = PaletteBuilder.Swatches(TerrainManifest);

        Assert.All(swatches, s => Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(TerrainManifest)!, s.Texture)), s.Texture));
        Assert.Equal(swatches.Count, swatches.Select(s => s.File).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void River_crossing_can_be_painted_from_the_palette()
        => Assert.Subset(PaletteBuilder.Materials(TerrainManifest).ToHashSet(), Base.Value.MapFor("river_crossing")!.Materials.ToHashSet());

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void A_height_tile_is_one_tile_of_rgba(int height)
        => Assert.Equal(PaletteBuilder.TileSize * PaletteBuilder.TileSize * 4, PaletteBuilder.HeightTile(height).Length);

    [Fact]
    public void Every_height_looks_different()
    {
        var tiles = Enumerable.Range(0, WorldRegion.MaxHeight + 1).Select(h => Convert.ToBase64String(PaletteBuilder.HeightTile(h)));

        Assert.Equal(WorldRegion.MaxHeight + 1, tiles.Distinct().Count());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void A_height_outside_the_range_has_no_tile(int height)
        => Assert.Throws<ArgumentOutOfRangeException>(() => PaletteBuilder.HeightTile(height));

    [Fact]
    public void A_higher_tile_is_a_lighter_shade()
    {
        // A corner pixel is the shade: the number sits in the middle.
        var shades = Enumerable.Range(0, WorldRegion.MaxHeight + 1).Select(h => PaletteBuilder.HeightTile(h)[0]).ToList();

        Assert.Equal(shades.Order(), shades);
        Assert.Equal(shades.Count, shades.Distinct().Count());
    }

    [Fact]
    public void A_height_tile_lets_the_material_show_through()
        => Assert.InRange(PaletteBuilder.HeightTile(5)[3], (byte)1, (byte)200);

    [Fact]
    public void Every_combination_of_flags_looks_different()
    {
        var tiles = Enumerable.Range(0, PaletteBuilder.FlagCombinations)
            .Select(bits => Convert.ToBase64String(PaletteBuilder.FlagsTile((TileFlags)bits)));

        Assert.Equal(PaletteBuilder.FlagCombinations, tiles.Distinct().Count());
    }

    [Fact]
    public void The_flag_combinations_cover_every_defined_flag()
    {
        var all = Enum.GetValues<TileFlags>().Aggregate((all, flag) => all | flag);

        Assert.Equal(PaletteBuilder.FlagCombinations - 1, (int)all);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 7, 0)]
    [InlineData(8, 0, 1)]
    [InlineData(19, 3, 2)]
    [InlineData(31, 7, 3)]
    public void A_flags_tile_sits_in_the_atlas_by_its_bits(int bits, int x, int y)
    {
        Assert.Equal((x, y), PaletteBuilder.FlagsAtlas((TileFlags)bits));
        Assert.Equal((TileFlags)bits, PaletteBuilder.FlagsAt(x, y));
    }
}
