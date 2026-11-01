using Godot;
using Tactics.Logic;
using Tactics.WorldTools;

namespace Tactics.World.Editor;

/// <summary>
/// Generates <c>palette/</c>: a swatch per material, the height and flags tiles, and a tile set per layer. It takes two steps,
/// because a tile set needs its images imported first: <see cref="WriteImages"/>, then Godot's import, then
/// <see cref="WriteTileSets"/>.
/// </summary>
public static class Palette
{
    /// <summary>The Material layer's tile set: one source per material, named after it, with one tile.</summary>
    public const string Materials = "res://palette/materials.tres";

    /// <summary>The Height layer's tile set: one source, with a tile per height along the top row.</summary>
    public const string Heights = "res://palette/heights.tres";

    /// <summary>The Flags layer's tile set: one source, with a tile per flag combination.</summary>
    public const string Flags = "res://palette/flags.tres";

    private const string SwatchFolder = "res://palette/materials/";
    private const string HeightsImage = "res://palette/heights.png";
    private const string FlagsImage = "res://palette/flags.png";

    private static readonly Vector2I TileSize = new(PaletteBuilder.TileSize, PaletteBuilder.TileSize);

    /// <summary>Writes every image. A swatch is the corner of the material's real texture, which for an animated one is its first frame.</summary>
    public static void WriteImages()
    {
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(SwatchFolder));
        foreach (var swatch in PaletteBuilder.AllSwatches(RegionPaths.Content))
        {
            var texture = Image.LoadFromFile(Path.Combine(RegionPaths.Content, swatch.Texture));
            Save(texture.GetRegion(new Rect2I(Vector2I.Zero, TileSize)), SwatchFolder + swatch.File);
        }

        var heights = Image.CreateEmpty(TileSize.X * (WorldRegion.MaxHeight + 1), TileSize.Y, false, Image.Format.Rgba8);
        for (var height = 0; height <= WorldRegion.MaxHeight; height++)
            Blit(heights, PaletteBuilder.HeightTile(height), new Vector2I(height, 0));
        Save(heights, HeightsImage);

        var (columns, rows) = PaletteBuilder.FlagsAtlas((TileFlags)(PaletteBuilder.FlagCombinations - 1));
        var flags = Image.CreateEmpty(TileSize.X * (columns + 1), TileSize.Y * (rows + 1), false, Image.Format.Rgba8);
        for (var bits = 0; bits < PaletteBuilder.FlagCombinations; bits++)
        {
            var (x, y) = PaletteBuilder.FlagsAtlas((TileFlags)bits);
            Blit(flags, PaletteBuilder.FlagsTile((TileFlags)bits), new Vector2I(x, y));
        }
        Save(flags, FlagsImage);
    }

    /// <summary>
    /// Writes the tile sets. Painted scenes store a material's source id, so an existing source is never renumbered or removed: new
    /// materials are added after the others. A material dropped from the manifest stays too, so the export can name it on the tile
    /// rather than the scene losing it.
    /// </summary>
    public static void WriteTileSets()
    {
        var materials = ResourceLoader.Exists(Materials) ? GD.Load<TileSet>(Materials) : new TileSet { TileSize = TileSize };
        var known = Enumerable.Range(0, materials.GetSourceCount())
            .Select(i => materials.GetSource(materials.GetSourceId(i)).ResourceName).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var swatch in PaletteBuilder.AllSwatches(RegionPaths.Content).Where(s => !known.Contains(s.Material)))
        {
            var source = NewSource(swatch.Material, SwatchFolder + swatch.File);
            materials.AddSource(source);
            source.CreateTile(Vector2I.Zero);
            added++;
        }
        if (added > 0 || !ResourceLoader.Exists(Materials))
            Check(ResourceSaver.Save(materials, Materials), Materials);

        var heights = NewSource("heights", HeightsImage);
        for (var height = 0; height <= WorldRegion.MaxHeight; height++)
            heights.CreateTile(new Vector2I(height, 0));
        SaveSet(heights, Heights);

        var flags = NewSource("flags", FlagsImage);
        for (var bits = 0; bits < PaletteBuilder.FlagCombinations; bits++)
        {
            var (x, y) = PaletteBuilder.FlagsAtlas((TileFlags)bits);
            flags.CreateTile(new Vector2I(x, y));
        }
        SaveSet(flags, Flags);
    }

    private static TileSetAtlasSource NewSource(string name, string image) => new()
    {
        ResourceName = name,
        Texture = GD.Load<Texture2D>(image),
        TextureRegionSize = TileSize,
    };

    private static void SaveSet(TileSetAtlasSource source, string path)
    {
        if (ResourceLoader.Exists(path))
            return;
        var set = new TileSet { TileSize = TileSize };
        set.AddSource(source);
        Check(ResourceSaver.Save(set, path), path);
    }

    private static void Blit(Image onto, byte[] tile, Vector2I cell)
        => onto.BlitRect(Image.CreateFromData(TileSize.X, TileSize.Y, false, Image.Format.Rgba8, tile), new Rect2I(Vector2I.Zero, TileSize), cell * TileSize);

    private static void Save(Image image, string path) => Check(image.SavePng(ProjectSettings.GlobalizePath(path)), path);

    private static void Check(Error error, string path)
    {
        if (error != Error.Ok)
            throw new IOException($"Couldn't write {path}: {error}.");
    }
}
