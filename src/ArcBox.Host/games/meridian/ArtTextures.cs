using Godot;
using Meridian.Presentation.Art;
using Meridian.Presentation.Hud;

namespace ArcBox.Host.Games.Meridian;

/// <summary>The pack's art as textures, loaded once each, shared by the scene's layers; null for a file that won't load.</summary>
public sealed class ArtTextures
{
    private readonly Dictionary<string, Texture2D?> _cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, StyleBoxTexture?> _boxes = new(StringComparer.Ordinal);

    public MeridianArt Art { get; private set; } = MeridianArt.None;

    /// <summary>A crew member's id by their name, as the host view names them.</summary>
    public Func<string, string?> CrewId { get; private set; } = _ => null;

    public void Use(MeridianArt art, Func<string, string?> crewId)
    {
        (Art, CrewId) = (art, crewId);
        _cache.Clear();
        _boxes.Clear();
    }

    public Texture2D? Get(string path)
    {
        if (!_cache.TryGetValue(path, out var texture))
        {
            texture = Image.LoadFromFile(path) is { } image ? ImageTexture.CreateFromImage(image) : null;
            _cache[path] = texture;
        }
        return texture;
    }

    /// <summary>The portrait of the crew member called <paramref name="crewName"/>, if the pack has one.</summary>
    public Texture2D? Portrait(string? crewName)
        => crewName is not null && CrewId(crewName) is { } id && Art.Portrait(id) is { } entry ? Get(entry.Path) : null;

    /// <summary>
    /// A UI-kit piece as a 9-slice box (M5 plan, Task 5): Godot's StyleBoxTexture keeps the corners at their pixel size and
    /// stretches the edges and middle, so one piece fits any panel. A piece without insets stretches whole. Null when the
    /// entry is null or its file won't load: the caller draws the M4 look.
    /// </summary>
    public StyleBoxTexture? Box(ArtEntry? entry)
    {
        if (entry is null)
        {
            return null;
        }
        if (!_boxes.TryGetValue(entry.Path, out var box))
        {
            box = Get(entry.Path) is { } texture ? new StyleBoxTexture { Texture = texture } : null;
            if (box is not null && entry.Insets is [var left, var top, var right, var bottom])
            {
                (box.TextureMarginLeft, box.TextureMarginTop, box.TextureMarginRight, box.TextureMarginBottom) = (left, top, right, bottom);
            }
            _boxes[entry.Path] = box;
        }
        return box;
    }

    /// <summary>Draws <paramref name="entry"/> as a box over <paramref name="rect"/>; false when there's no art, to draw the fallback.</summary>
    public bool DrawBox(CanvasItem on, ArtEntry? entry, Rect2 rect)
    {
        if (Box(entry) is not { } box)
        {
            return false;
        }
        // At the kit's whole-step scale: the box drawn at a fraction of the size under a matching transform, so its corners
        // and edges are art pixels multiplied, never resampled.
        const float scale = UiKit.PixelScale;
        on.DrawSetTransform(rect.Position, 0, new Vector2(scale, scale));
        box.Draw(on.GetCanvasItem(), new Rect2(Vector2.Zero, rect.Size / scale));
        on.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        return true;
    }

    /// <summary>One frame of a strip, picked by time; false when there's no art.</summary>
    public bool DrawFrame(CanvasItem on, ArtEntry? entry, Rect2 rect, double time)
    {
        if (entry is null || Get(entry.Path) is not { } texture)
        {
            return false;
        }
        var frame = entry.Frames > 1 ? (int)(time * Math.Max(1, entry.Fps)) % entry.Frames : 0;
        on.DrawTextureRectRegion(texture, rect, new Rect2(frame * entry.FrameW, 0, entry.FrameW, entry.FrameH));
        return true;
    }
}
