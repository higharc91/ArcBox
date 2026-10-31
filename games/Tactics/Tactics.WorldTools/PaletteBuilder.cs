using System.Text.Json;
using Tactics.Logic;

namespace Tactics.WorldTools;

/// <summary>One paintable material: its name, its texture relative to the terrain manifest, and its swatch's file name.</summary>
public sealed record MaterialSwatch(string Material, string Texture, string File);

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

    /// <summary>A height tile's alpha: enough to read as a shade, little enough to see the material through it.</summary>
    private const byte ShadeAlpha = 120;

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
        (TileFlags.Deep, 'D', (20, 40, 130), 11, 10),
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

    /// <summary>A height's tile: a see-through shade, lighter the higher it is, with its number in the middle.</summary>
    public static byte[] HeightTile(int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(height, WorldRegion.MaxHeight);

        var shade = (byte)(20 + height * 235 / WorldRegion.MaxHeight);
        var tile = new byte[TileSize * TileSize * 4];
        Fill(tile, 0, 0, TileSize, TileSize, (shade, shade, shade), ShadeAlpha);
        var ink = height >= 5 ? (byte)0 : (byte)255;
        Draw(tile, (char)('0' + height), 3, (TileSize - 9) / 2, (TileSize - 15) / 2, (ink, ink, ink));
        return tile;
    }

    /// <summary>A flag combination's tile: clear, so the layers under it show, with a lettered mark per flag. No flags is a small dot.</summary>
    public static byte[] FlagsTile(TileFlags flags)
    {
        var tile = new byte[TileSize * TileSize * 4];
        if (flags == TileFlags.None)
            Fill(tile, 14, 14, 4, 4, (235, 235, 235), 255);
        foreach (var (flag, letter, colour, x, y) in FlagMarks)
            if (flags.HasFlag(flag))
            {
                Fill(tile, x, y, 10, 12, colour, 255);
                Draw(tile, letter, 2, x + 2, y + 1, (255, 255, 255));
            }
        return tile;
    }

    /// <summary>Where a flag combination sits in the flags atlas: its bits, read along the rows.</summary>
    public static (int X, int Y) FlagsAtlas(TileFlags flags) => ((int)flags % FlagsAtlasColumns, (int)flags / FlagsAtlasColumns);

    /// <summary>The flag combination at a cell of the flags atlas.</summary>
    public static TileFlags FlagsAt(int x, int y) => (TileFlags)(y * FlagsAtlasColumns + x);

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
