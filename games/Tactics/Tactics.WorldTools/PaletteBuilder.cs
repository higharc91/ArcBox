using System.Text.Json;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.WorldTools;

/// <summary>One paintable material: its name, its texture relative to the terrain manifest, and its swatch's file name.</summary>
public sealed record MaterialSwatch(string Material, string Texture, string File);

/// <summary>One paintable prop: its id, variant, blocking state, cover, height, texture and file name.</summary>
public sealed record PropSwatch(string Id, string? Variant, bool Blocks, Cover Cover, int Height, string Texture, string File);

/// <summary>
/// What the painting workspace's palette holds. Materials come from the terrain manifest, and sides aren't listed because the game
/// chooses them. Height and flags tiles are drawn here as plain RGBA, top row first, so the Godot project only saves them.
/// </summary>
public static class PaletteBuilder
{
    /// <summary>A swatch's size in pixels.</summary>
    public const int TileSize = PaintedRegion.TilePixels;

    /// <summary>The flag combinations a tile can have, one per value of the defined bits.</summary>
    public const int FlagCombinations = 32;

    /// <summary>Tiles along a row of the flags atlas.</summary>
    private const int FlagsAtlasColumns = 8;

    /// <summary>Tiles along a row of the props atlas.</summary>
    public const int PropAtlasColumns = 16;

    /// <summary>A height tile's alpha: enough to read as a wash, little enough to see the material through it.</summary>
    private const byte ShadeAlpha = 140;

    /// <summary>How much of the wash's colour a height number keeps.</summary>
    private const double InkShade = 0.3;

    /// <summary>Where a height number sits: the middle of the tile, between the flag marks.</summary>
    private const int NumberLeft = 13, NumberTop = 11;

    /// <summary>Each height's hue, 0 to 9, cold to hot. Height 0's is only its number's ink, as its wash is clear.</summary>
    private static readonly (byte R, byte G, byte B)[] HeightRamp =
    [
        (120, 120, 120), (40, 90, 255), (0, 190, 235), (30, 200, 80), (165, 225, 30),
        (255, 225, 20), (255, 140, 10), (235, 40, 30), (225, 40, 180), (130, 50, 240),
    ];

    /// <summary>Each glyph's 3x5 pixels, a row to a string.</summary>
    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = ["###", "#.#", "#.#", "#.#", "###"],
        ['1'] = [".#.", "##.", ".#.", ".#.", "###"],
        ['2'] = ["###", "..#", "###", "#..", "###"],
        ['3'] = ["###", "..#", ".##", "..#", "###"],
        ['4'] = ["#.#", "#.#", "###", "..#", "..#"],
        ['5'] = ["###", "#..", "###", "..#", "###"],
        ['6'] = ["###", "#..", "###", "#.#", "###"],
        ['7'] = ["###", "..#", ".#.", ".#.", ".#."],
        ['8'] = ["###", "#.#", "###", "#.#", "###"],
        ['9'] = ["###", "#.#", "###", "..#", "###"],
        ['W'] = ["#.#", "#.#", "#.#", "###", "#.#"],
        ['R'] = ["##.", "#.#", "##.", "#.#", "#.#"],
        ['X'] = ["#.#", "#.#", ".#.", "#.#", "#.#"],
        ['H'] = ["#.#", "#.#", "###", "#.#", "#.#"],
        ['D'] = ["##.", "#.#", "#.#", "#.#", "##."],
    };

    /// <summary>How each flag is marked: its letter, its colour and where it sits on the tile.</summary>
    private static readonly (TileFlags Flag, char Letter, (byte R, byte G, byte B) Colour, int X, int Y)[] FlagMarks =
    [
        (TileFlags.Water, 'W', (40, 110, 220), 2, 2),
        (TileFlags.Difficult, 'R', (190, 150, 30), 20, 2),
        (TileFlags.Impassable, 'X', (200, 40, 40), 2, 18),
        (TileFlags.Hazard, 'H', (230, 110, 20), 20, 18),
        (TileFlags.Deep, 'D', (20, 40, 130), 12, 0),
    ];

    /// <summary>Every paintable material, <c>"grass/lush"</c>, by ordinal name.</summary>
    public static IReadOnlyList<string> Materials(string manifestPath) => [.. Swatches(manifestPath).Select(s => s.Material)];

    /// <summary>Every paintable material with its texture, by ordinal name.</summary>
    public static IReadOnlyList<MaterialSwatch> Swatches(string manifestPath)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        return
        [
            .. manifest.RootElement.GetProperty("textures").EnumerateObject()
                .Where(texture => texture.Value.GetProperty("kind").GetString() == "top")
                .SelectMany(texture => texture.Value.GetProperty("variants").EnumerateObject().Select(variant =>
                    new MaterialSwatch($"{texture.Name}/{variant.Name}", variant.Value.GetString()!, $"{texture.Name}_{variant.Name}.png")))
                .OrderBy(swatch => swatch.Material, StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Every source a painted cell can use: the terrain tops, plus every material a battle map names that isn't a top, such as
    /// <c>dungeon_wall/dark</c>, so any map can be stamped. Those show the art the game draws for them. Textures here are relative to
    /// <paramref name="contentBase"/>. By ordinal name.
    /// </summary>
    public static IReadOnlyList<MaterialSwatch> AllSwatches(string contentBase)
    {
        var tops = Swatches(Path.Combine(contentBase, "terrain", "manifest.json")).Select(s => s with { Texture = $"terrain/{s.Texture}" }).ToList();
        var known = tops.Select(s => s.Material).ToHashSet(StringComparer.Ordinal);
        var art = ArtIndex.Load(contentBase);

        var fromMaps = Directory.EnumerateFiles(Path.Combine(contentBase, "rules", "maps"), "*.json").SelectMany(MapMaterials)
            .Where(known.Add)
            .Select(material => (Material: material, Image: art.Texture(MaterialLook.Of(material).Top)))
            .Where(m => m.Image is not null)
            .Select(m => new MaterialSwatch(m.Material, m.Image!.Path, $"{m.Material.Replace('/', '_')}.png"));
        return [.. tops.Concat(fromMaps).OrderBy(s => s.Material, StringComparer.Ordinal)];
    }

    private static List<string> MapMaterials(string mapFile)
    {
        using var map = JsonDocument.Parse(File.ReadAllBytes(mapFile));
        return [.. map.RootElement.GetProperty("materials").EnumerateArray().Select(m => m.GetString()!)];
    }

    /// <summary>
    /// A height's tile. 0 is clear, so a painted 0 and a blank cell look alike. Above that each height has its own hue, cold to hot, so
    /// the land's shape reads at region zoom, where the number blurs into the wash. The number is the wash's hue darkened, so it reads
    /// over snow and doesn't speckle over grass.
    /// </summary>
    public static byte[] HeightTile(int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, WorldRegion.MaxHeight);

        var tile = new byte[TileSize * TileSize * 4];
        var (r, g, b) = HeightRamp[height];
        if (height > 0)
            Fill(tile, 0, 0, TileSize, TileSize, (r, g, b), ShadeAlpha);
        Draw(tile, (char)('0' + height), 2, NumberLeft, NumberTop, ((byte)(r * InkShade), (byte)(g * InkShade), (byte)(b * InkShade)));
        return tile;
    }

    /// <summary>A flag combination's tile: clear, so the layers under it show, with a lettered mark per flag clear of the height number. No flags is a small dot.</summary>
    public static byte[] FlagsTile(TileFlags flags)
    {
        var tile = new byte[TileSize * TileSize * 4];
        if (flags == TileFlags.None)
            Fill(tile, 14, 3, 4, 4, (235, 235, 235), 255);
        foreach (var (flag, letter, colour, x, y) in FlagMarks)
            if (flags.HasFlag(flag))
            {
                var (wide, inset) = flag == TileFlags.Deep ? (8, 1) : (10, 2);
                Fill(tile, x, y, wide, flag == TileFlags.Deep ? 11 : 12, colour, 255);
                Draw(tile, letter, 2, x + inset, y + (flag == TileFlags.Deep ? 0 : 1), (255, 255, 255));
            }
        return tile;
    }

    /// <summary>Where a flag combination sits in the flags atlas: its bits, read along the rows.</summary>
    public static (int X, int Y) FlagsAtlas(TileFlags flags) => ((int)flags % FlagsAtlasColumns, (int)flags / FlagsAtlasColumns);

    /// <summary>The flag combination at a cell of the flags atlas.</summary>
    public static TileFlags FlagsAt(int x, int y) => (TileFlags)(y * FlagsAtlasColumns + x);

    /// <summary>Default swatches loaded for prop atlas coordinate lookups.</summary>
    public static IReadOnlyList<PropSwatch>? DefaultPropSwatches { get; set; }

    /// <summary>Every paintable prop from its manifest, ordered by prop id then variant.</summary>
    public static IReadOnlyList<PropSwatch> PropSwatches(string manifestPath)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var list = new List<PropSwatch>();
        var props = manifest.RootElement.GetProperty("props");
        foreach (var prop in props.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
        {
            var blocks = prop.Value.TryGetProperty("blocks", out var b) && b.GetBoolean();
            var cover = prop.Value.TryGetProperty("cover", out var c) ? ParseCover(c.GetString()) : Cover.None;
            var height = prop.Value.TryGetProperty("height", out var h) ? h.GetInt32() : 1;
            if (prop.Value.TryGetProperty("variants", out var vars) && vars.ValueKind == JsonValueKind.Object)
            {
                var variants = vars.EnumerateObject().OrderBy(v => v.Name, StringComparer.Ordinal).ToList();
                if (variants.Count > 0)
                {
                    foreach (var variant in variants)
                        list.Add(new PropSwatch(prop.Name, variant.Name, blocks, cover, height, $"props/{variant.Value.GetString()!}", $"{prop.Name}_{variant.Name}.png"));
                    continue;
                }
            }
            list.Add(new PropSwatch(prop.Name, null, blocks, cover, height, "", $"{prop.Name}.png"));
        }
        return list;
    }

    /// <summary>Maps each cell in an existing prop atlas to the cell for the same prop in a rebuilt atlas.</summary>
    public static IReadOnlyDictionary<(int X, int Y), (int X, int Y)> PropAtlasRemap(
        IReadOnlyList<PropSwatch> oldSwatches, IReadOnlyList<PropSwatch> newSwatches)
    {
        var newCells = new Dictionary<(string Id, string? Variant), (int X, int Y)>();
        for (var i = 0; i < newSwatches.Count; i++)
        {
            var swatch = newSwatches[i];
            if (!newCells.TryAdd((swatch.Id, swatch.Variant), (i % PropAtlasColumns, i / PropAtlasColumns)))
                throw new ArgumentException($"The new prop atlas contains '{swatch.Id}/{swatch.Variant}' more than once.", nameof(newSwatches));
        }

        var remap = new Dictionary<(int X, int Y), (int X, int Y)>();
        for (var i = 0; i < oldSwatches.Count; i++)
        {
            var swatch = oldSwatches[i];
            if (!newCells.TryGetValue((swatch.Id, swatch.Variant), out var target))
                throw new ArgumentException($"The rebuilt prop atlas has no '{swatch.Id}/{swatch.Variant}' swatch.", nameof(newSwatches));
            remap.Add((i % PropAtlasColumns, i / PropAtlasColumns), target);
        }
        return remap;
    }

    /// <summary>Every prop swatch from <paramref name="contentBase"/>'s <c>props/manifest.json</c>.</summary>
    public static IReadOnlyList<PropSwatch> AllPropSwatches(string contentBase)
        => PropSwatches(Path.Combine(contentBase, "props", "manifest.json"));

    /// <summary>Where a prop sits in the props atlas.</summary>
    public static (int X, int Y) PropAtlas(string id, string? variant = null)
    {
        var swatches = DefaultPropSwatches ??= FallbackSwatches();
        var index = -1;
        for (var i = 0; i < swatches.Count; i++)
        {
            if (swatches[i].Id == id && (swatches[i].Variant == variant || variant is null))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            throw new ArgumentException($"Unknown prop '{id}' with variant '{variant}'.", nameof(id));
        return (index % PropAtlasColumns, index / PropAtlasColumns);
    }

    /// <summary>The prop at a cell of the props atlas.</summary>
    public static (string Id, string? Variant) PropAt(int x, int y)
    {
        var swatches = DefaultPropSwatches ??= FallbackSwatches();
        var index = y * PropAtlasColumns + x;
        if (index < 0 || index >= swatches.Count)
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {y}) is outside the prop atlas.");
        var swatch = swatches[index];
        return (swatch.Id, swatch.Variant);
    }

    /// <summary>Marks a 32x32 RGBA tile as blocking with the impassable mark in the bottom left.</summary>
    public static void MarkBlocking(byte[] tile)
    {
        Fill(tile, 2, 18, 10, 12, (200, 40, 40), 255);
        Draw(tile, 'X', 2, 4, 19, (255, 255, 255));
    }

    /// <summary>Whether a prop swatch tile has the blocking mark.</summary>
    public static bool HasBlockingMark(byte[] tile)
    {
        var at = (18 * TileSize + 2) * 4;
        return tile[at] == 200 && tile[at + 1] == 40 && tile[at + 2] == 40;
    }

    private static Cover ParseCover(string? cover) => cover switch
    {
        "full" or "total" => Cover.Total,
        "half" => Cover.Half,
        "three_quarters" => Cover.ThreeQuarters,
        _ => Cover.None,
    };

    private static List<PropSwatch> FallbackSwatches()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "content", "tactics", "base", "props", "manifest.json");
            if (File.Exists(candidate))
                return [.. PropSwatches(candidate)];
            dir = dir.Parent;
        }
        dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "content", "tactics", "base", "props", "manifest.json");
            if (File.Exists(candidate))
                return [.. PropSwatches(candidate)];
            dir = dir.Parent;
        }
        return [];
    }

    private static void Fill(byte[] tile, int left, int top, int width, int height, (byte R, byte G, byte B) colour, byte alpha)
    {
        for (var y = top; y < top + height; y++)
            for (var x = left; x < left + width; x++)
            {
                var at = (y * TileSize + x) * 4;
                (tile[at], tile[at + 1], tile[at + 2], tile[at + 3]) = (colour.R, colour.G, colour.B, alpha);
            }
    }

    private static void Draw(byte[] tile, char glyph, int scale, int left, int top, (byte R, byte G, byte B) colour)
    {
        var rows = Glyphs[glyph];
        for (var row = 0; row < rows.Length; row++)
            for (var column = 0; column < rows[row].Length; column++)
                if (rows[row][column] == '#')
                    Fill(tile, left + column * scale, top + row * scale, scale, scale, colour, 255);
    }
}
