using System.Collections.Immutable;
using System.Numerics;
using System.Text.Json;

namespace Tactics.Presentation.Board;

/// <summary>A texture by the manifests' names: its family (<c>"cliff_side"</c>) and variant (<c>"grey"</c>).</summary>
public sealed record ArtRef(string Family, string Variant);

/// <summary>An image in the pack: a still, or a horizontal strip of frames.</summary>
/// <param name="Path">Relative to the pack's root, with forward slashes.</param>
/// <param name="Frames">How many frames the strip has.</param>
/// <param name="Fps">How fast they loop.</param>
/// <param name="Height">The image's height in pixels, where a face needs it to sample one.</param>
/// <param name="Glow">A texture's rule for glowing by itself, or null.</param>
public sealed record ArtImage(string Path, int Frames, double Fps, int Height = 32, TextureGlow? Glow = null)
{
    /// <summary>The frame showing <paramref name="seconds"/> into the loop.</summary>
    public int FrameAt(double seconds) => Frames <= 1 ? 0 : (int)(seconds * Fps) % Frames;

    /// <summary>A U in one frame (0 to 1), as a U in the whole strip.</summary>
    public float FrameU(float u, int frame) => (u + frame) / Frames;
}

/// <summary>A prop's sprite. The anchor is its ground contact pixel, which stands on the tile's centre.</summary>
public sealed record PropArt(ArtImage Image, int Width, int Height, int AnchorX, int AnchorY, PropLight? Light = null);

/// <summary>A hazard's sprites: <see cref="Ground"/> on the tile top, <see cref="Over"/>, if any, above the units.</summary>
public sealed record HazardArt(ArtImage Ground, ArtImage? Over, Vector2 Frame, Vector2 Origin, Vector2 OverFrame, Vector2 OverOrigin);

/// <summary>
/// What the pack's art manifests say about the board's art: <c>terrain/</c> and <c>dungeon/</c>'s textures,
/// <c>props/</c> and <c>dungeon/</c>'s props, and <c>hazards/</c>. A variant a set doesn't have falls back to its first,
/// since the maps name some that don't exist (<c>flagstone/mossy</c>, <c>mine_boards/oak</c>).
/// </summary>
public sealed class ArtIndex
{
    private sealed record Family(ImmutableArray<(string Name, string Path)> Variants, int Frames, double Fps, int Height,
        ImmutableDictionary<string, TextureGlow> Glows);

    private sealed record PropFamily(ImmutableArray<(string Name, string Path)> Variants, int Frames, double Fps,
        int Width, int Height, int AnchorX, int AnchorY, PropLight? Light, ImmutableDictionary<string, Rgb> Colours);

    private readonly Dictionary<string, Family> _textures = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PropFamily> _props = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HazardArt> _hazards = new(StringComparer.Ordinal);

    private ArtIndex()
    {
    }

    /// <summary>Reads the manifests under the pack's root. Throws if one is missing or malformed.</summary>
    public static ArtIndex Load(string packRoot)
    {
        string Read(string set) => File.ReadAllText(Path.Combine(packRoot, set, "manifest.json"));
        var index = new ArtIndex();
        foreach (var set in new[] { "terrain", "dungeon" })
        {
            using var doc = JsonDocument.Parse(Read(set));
            index.AddTextures(doc.RootElement.GetProperty("textures"), $"{set}/");
        }
        using (var props = JsonDocument.Parse(Read("props")))
            index.AddProps(props.RootElement.GetProperty("props"), "props/");
        using (var dungeon = JsonDocument.Parse(Read("dungeon")))
            index.AddProps(dungeon.RootElement.GetProperty("props"), "dungeon/");
        using (var hazards = JsonDocument.Parse(Read("hazards")))
            index.AddHazards(hazards.RootElement);
        using (var recipe = JsonDocument.Parse(Read("dungeon")))
            index.Lighting = Recipe(recipe.RootElement);
        return index;
    }

    /// <summary>The pack's lighting recipe.</summary>
    public LightRecipe Lighting { get; private set; } = null!;

    /// <summary>The texture, or null if the pack has no such family.</summary>
    public ArtImage? Texture(ArtRef art)
    {
        if (!_textures.TryGetValue(art.Family, out var family))
            return null;
        var (name, path) = Pick(family.Variants, [art.Variant]);
        return new ArtImage(path, family.Frames, family.Fps, family.Height, family.Glows.GetValueOrDefault(name));
    }

    /// <summary>The prop in the first of <paramref name="prefer"/>'s variants it has, else its first; null if there's no such prop.</summary>
    public PropArt? Prop(string id, IEnumerable<string> prefer)
    {
        if (!_props.TryGetValue(id, out var prop))
            return null;
        var (name, path) = Pick(prop.Variants, prefer);
        return new PropArt(new ArtImage(path, prop.Frames, prop.Fps, prop.Height), prop.Width, prop.Height, prop.AnchorX, prop.AnchorY,
            prop.Light is { } light ? light with { Colour = prop.Colours.GetValueOrDefault(name, light.Colour) } : null);
    }

    /// <summary>The hazard, or null if the pack has no such hazard.</summary>
    public HazardArt? Hazard(string id) => _hazards.GetValueOrDefault(id);

    private static (string Name, string Path) Pick(ImmutableArray<(string Name, string Path)> variants, IEnumerable<string> prefer)
    {
        foreach (var name in prefer)
            foreach (var variant in variants)
                if (variant.Name == name)
                    return variant;
        return variants[0];
    }

    private static ImmutableArray<(string Name, string Path)> Variants(JsonElement entry, string prefix)
        => [.. entry.GetProperty("variants").EnumerateObject().Select(v => (v.Name, prefix + v.Value.GetString()))];

    private void AddTextures(JsonElement textures, string prefix)
    {
        foreach (var entry in textures.EnumerateObject())
        {
            var e = entry.Value;
            _textures[entry.Name] = new Family(Variants(e, prefix), e.GetProperty("frames").GetInt32(), e.GetProperty("fps").GetDouble(),
                e.GetProperty("size")[1].GetInt32(), Glows(e));
        }
    }

    private void AddProps(JsonElement props, string prefix)
    {
        foreach (var entry in props.EnumerateObject())
        {
            var e = entry.Value;
            var size = e.GetProperty("size");
            var anchor = e.GetProperty("anchor");
            var lit = e.TryGetProperty("light", out var light);
            _props[entry.Name] = new PropFamily(Variants(e, prefix), e.GetProperty("frames").GetInt32(), e.GetProperty("fps").GetDouble(),
                size[0].GetInt32(), size[1].GetInt32(), anchor[0].GetInt32(), anchor[1].GetInt32(),
                lit ? Light(light, Point(anchor), prefix) : null,
                lit ? Colours(light.GetProperty("colour")) : ImmutableDictionary<string, Rgb>.Empty);
        }
    }

    private static ImmutableDictionary<string, Rgb> Colours(JsonElement colours)
        => colours.EnumerateObject().ToImmutableDictionary(c => c.Name, c => Rgb.Hex(c.Value.GetString()!), StringComparer.Ordinal);

    private static Vector2 Point(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle());

    /// <summary><c>at</c> is a list of points or, for what sways, one such list per frame.</summary>
    private static PropLight Light(JsonElement light, Vector2 anchor, string prefix)
    {
        var at = light.GetProperty("at");
        ImmutableArray<ImmutableArray<Vector2>> frames = at[0][0].ValueKind == JsonValueKind.Array
            ? [.. at.EnumerateArray().Select(f => f.EnumerateArray().Select(Point).ToImmutableArray())]
            : [[.. at.EnumerateArray().Select(Point)]];
        var mask = light.TryGetProperty("emissive", out var m) ? prefix + m.GetString() : null;
        return new PropLight(frames, anchor, light.GetProperty("radius").GetSingle(), default, light.GetProperty("flicker").GetString()!, mask);
    }

    private static ImmutableDictionary<string, TextureGlow> Glows(JsonElement texture)
    {
        if (!texture.TryGetProperty("emissive", out var rule))
            return ImmutableDictionary<string, TextureGlow>.Empty;
        var light = rule.GetProperty("light");
        return rule.GetProperty("colour").EnumerateObject().ToImmutableDictionary(c => c.Name, c => new TextureGlow(Rgb.Hex(c.Value.GetString()!),
            light.GetProperty("every").GetInt32(), light.GetProperty("radius").GetSingle(), light.GetProperty("lift").GetSingle(),
            light.GetProperty("flicker").GetString()!, light.GetProperty("power").GetSingle()), StringComparer.Ordinal);
    }

    private static LightRecipe Recipe(JsonElement dungeon)
    {
        var lighting = dungeon.GetProperty("lighting");
        return new LightRecipe(
            lighting.GetProperty("ambient").EnumerateObject().ToImmutableSortedDictionary(a => a.Name,
                a => new Rgb(a.Value[0].GetSingle(), a.Value[1].GetSingle(), a.Value[2].GetSingle()), StringComparer.Ordinal),
            lighting.GetProperty("flicker").EnumerateObject().ToImmutableSortedDictionary(f => f.Name,
                f => f.Value.EnumerateArray().Select(v => v.GetSingle()).ToImmutableArray(), StringComparer.Ordinal),
            [.. dungeon.GetProperty("light").GetProperty("halos").EnumerateObject().Select(h => new LightHalo(
                $"dungeon/{h.Value.GetProperty("file").GetString()}", Point(h.Value.GetProperty("size")), Rgb.Hex(h.Value.GetProperty("colour").GetString()!)))]);
    }

    private void AddHazards(JsonElement root)
    {
        static Vector2 Pair(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle());
        var (frame, origin) = (Pair(root.GetProperty("frame")), Pair(root.GetProperty("origin")));
        var (overFrame, overOrigin) = (Pair(root.GetProperty("overFrame")), Pair(root.GetProperty("overOrigin")));
        foreach (var entry in root.GetProperty("hazards").EnumerateObject())
        {
            var e = entry.Value;
            var (frames, fps) = (e.GetProperty("frames").GetInt32(), e.GetProperty("fps").GetDouble());
            ArtImage Image(string file, Vector2 size) => new($"hazards/{file}", frames, fps, (int)size.Y);
            var over = e.TryGetProperty("over", out var o) ? Image(o.GetString()!, overFrame) : null;
            _hazards[entry.Name] = new HazardArt(Image(e.GetProperty("ground").GetString()!, frame), over, frame, origin, overFrame, overOrigin);
        }
    }
}
