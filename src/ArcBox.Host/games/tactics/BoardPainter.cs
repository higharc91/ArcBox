using Godot;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Draws the items of a <see cref="LitBoard"/> on any canvas item, and holds the pack's textures. A board and a block of the world paint
/// alike and differ in their light. A board reads it at each draw, from the lighting passed to <see cref="Paint"/>. A block is drawn once,
/// so <see cref="Prepare(LitItem, BoardLighting?)"/> bakes each item at its mean from the lighting of the whole region, and the block paints
/// with no lighting at all. <see cref="Prepare(LitItem, BoardLighting?)"/> makes only arrays and is safe off the main thread; textures
/// are attached by <see cref="Bind"/>, on it.
/// </summary>
internal sealed class BoardPainter
{
    private sealed class DrawnFace(LitFace lit, Vector2[] points, Vector2[]? outline, Vector2[] uvs, Color[] colours)
    {
        public LitFace Lit { get; } = lit;
        public Vector2[] Points { get; } = points;
        public Vector2[]? Outline { get; } = outline;
        public Vector2[] Uvs { get; } = uvs;
        public Vector2[] FrameUvs { get; } = new Vector2[uvs.Length];
        public Color[] Colours { get; } = colours;
        public Texture2D? Texture { get; set; }
        public Texture2D? Glow { get; set; }
    }

    private sealed class DrawnSprite(LitSprite lit, Rect2 target, Vector2 size, Color baked)
    {
        public LitSprite Lit { get; } = lit;
        public Rect2 Target { get; } = target;
        public Vector2 Size { get; } = size;
        public Color Baked { get; } = baked;
        public Texture2D? Texture { get; set; }
        public Texture2D? Glow { get; set; }
    }

    private static readonly Color GridLine = new(0, 0, 0, 0.22f);
    private static readonly Color[] Unlit = [Colors.White, Colors.White, Colors.White, Colors.White];

    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly Dictionary<string, Texture2D?> _glows = [];

    /// <summary>The pack's root, which files are read from.</summary>
    public string Root { get; set; } = "";

    /// <summary>
    /// What <see cref="Paint"/> needs for <paramref name="item"/>, before its textures. With <paramref name="baked"/>, the lighting of the whole
    /// region, its colours are frozen at each light's mean here, which needs nothing from the main thread and doesn't care about any <c>Step</c>.
    /// </summary>
    public static object Prepare(LitItem item, BoardLighting? baked = null) => item is LitFace face ? Prepare(face, baked) : Prepare((LitSprite)item, baked);

    /// <summary>Attaches the textures of something from <see cref="Prepare(LitItem, BoardLighting?)"/>. Only on the main thread: this may load files.</summary>
    public void Bind(object drawn)
    {
        if (drawn is DrawnFace face)
        {
            var lit = face.Lit;
            face.Texture = lit.Art ? Load(lit.Face.Image?.Path) : null;
            face.Glow = lit.Glow && lit.Face.Image is { Glow: { } rule } image ? Glow(image.Path, null, rule.Colour) : null;
        }
        else if (drawn is DrawnSprite sprite)
        {
            var lit = sprite.Lit;
            sprite.Texture = Load(lit.Sprite.Image.Path);
            sprite.Glow = lit.Glow && lit.Sprite.Light is { } light ? Glow(lit.Sprite.Image.Path, light.Mask, light.Colour) : null;
        }
    }

    /// <summary>Paints one item on <paramref name="canvas"/> as things stand at <paramref name="clock"/>. With no <paramref name="now"/>, its colours are as prepared.</summary>
    public void Paint(CanvasItem canvas, object drawn, BoardLighting? now, double clock)
    {
        if (drawn is DrawnFace face)
        {
            PaintFace(canvas, face, now, clock);
        }
        else if (drawn is DrawnSprite sprite)
        {
            PaintSprite(canvas, sprite, now, clock);
        }
    }

    /// <summary>A file of the pack, relative to its root, or null if there's none or it can't be read.</summary>
    public Texture2D? Load(string? file)
    {
        if (file is null)
        {
            return null;
        }
        if (_textures.TryGetValue(file, out var known))
        {
            return known;
        }

        var path = System.IO.Path.Combine(Root, file);
        var loaded = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (loaded is null || loaded.IsEmpty())
        {
            GD.PushWarning($"Tactics board: can't load {path}");
            return _textures[file] = null;
        }
        return _textures[file] = ImageTexture.CreateFromImage(loaded);
    }

    private static DrawnFace Prepare(LitFace lit, BoardLighting? baked)
    {
        var face = lit.Face;
        Vector2[] points = [.. face.Points.Select(p => new Vector2(p.X, p.Y))];
        Vector2[] uvs = [.. face.Uvs.Select(p => new Vector2(p.X, p.Y))];
        var colour = Colour(face.Tint, face.Alpha);
        Color[] colours = [colour, colour, colour, colour];
        if (baked is not null)
        {
            for (var i = 0; i < colours.Length; i++)
            {
                colours[i] = Colour(lit.MeanColourAt(i, baked), face.Alpha);
            }
        }
        return new DrawnFace(lit, points, face.Grid ? [.. points, points[0]] : null, uvs, colours);
    }

    private static DrawnSprite Prepare(LitSprite lit, BoardLighting? baked)
    {
        var sprite = lit.Sprite;
        var size = new Vector2(sprite.Size.X, sprite.Size.Y);
        return new DrawnSprite(lit, new Rect2(new Vector2(sprite.TopLeft.X, sprite.TopLeft.Y), size), size, baked is null ? Colors.White : Colour(lit.MeanLight(baked)));
    }

    private static void PaintFace(CanvasItem canvas, DrawnFace drawn, BoardLighting? now, double clock)
    {
        LightUp(drawn, now);
        var uvs = FrameUvs(drawn, clock);
        if (drawn.Lit.Art)
        {
            if (drawn.Texture is null)
            {
                canvas.DrawColoredPolygon(drawn.Points, drawn.Colours[0]);
            }
            else
            {
                canvas.DrawPrimitive(drawn.Points, drawn.Colours, uvs, drawn.Texture);
            }
        }
        if (drawn.Glow is not null)
        {
            canvas.DrawPrimitive(drawn.Points, Unlit, uvs, drawn.Glow);
        }
        if (drawn.Outline is not null)
        {
            canvas.DrawPolyline(drawn.Outline, GridLine, 0.5f);
        }
    }

    private static void LightUp(DrawnFace drawn, BoardLighting? now)
    {
        if (drawn.Lit.Probes.IsEmpty || now is null)
        {
            return;
        }
        for (var i = 0; i < drawn.Colours.Length; i++)
        {
            drawn.Colours[i] = Colour(drawn.Lit.ColourAt(i, now), drawn.Lit.Face.Alpha);
        }
    }

    private static Color Colour(Rgb colour, float alpha = 1) => new(colour.R, colour.G, colour.B, alpha);

    private static Vector2[] FrameUvs(DrawnFace drawn, double clock)
    {
        if (drawn.Lit.Face.Image is not { Frames: > 1 } image)
        {
            return drawn.Uvs;
        }
        var frame = image.FrameAt(clock);
        for (var i = 0; i < drawn.Uvs.Length; i++)
        {
            drawn.FrameUvs[i] = new Vector2(image.FrameU(drawn.Uvs[i].X, frame), drawn.Uvs[i].Y);
        }
        return drawn.FrameUvs;
    }

    private static void PaintSprite(CanvasItem canvas, DrawnSprite drawn, BoardLighting? now, double clock)
    {
        if (drawn.Texture is not null)
        {
            var source = new Rect2(new Vector2(drawn.Lit.Sprite.Image.FrameAt(clock) * drawn.Size.X, 0), drawn.Size);
            var light = now is null ? drawn.Baked : Colour(drawn.Lit.LightNow(now));
            canvas.DrawTextureRectRegion(drawn.Texture, drawn.Target, source, light);
            if (drawn.Glow is not null)
            {
                canvas.DrawTextureRectRegion(drawn.Glow, drawn.Target, source);
            }
        }
    }

    /// <summary>What of a strip of the pack glows by itself, as a texture of its own: once each, as it's work over every pixel.</summary>
    private Texture2D? Glow(string file, string? mask, Rgb colour)
    {
        if (_glows.TryGetValue(file, out var known))
        {
            return known;
        }
        return _glows[file] = Bake(file, mask, colour);
    }

    private Texture2D? Bake(string file, string? mask, Rgb colour)
    {
        if (Pixels(file) is not { } art)
        {
            return null;
        }
        var pixels = art.GetData();
        byte[] glow;
        if (mask is null)
        {
            glow = GlowMask.FromColour(pixels, colour);
        }
        else if (Pixels(mask, Image.Format.L8) is { } masked && masked.GetSize() == art.GetSize())
        {
            glow = GlowMask.FromMask(pixels, masked.GetData());
        }
        else
        {
            return null;
        }
        return ImageTexture.CreateFromImage(Image.CreateFromData(art.GetWidth(), art.GetHeight(), false, Image.Format.Rgba8, glow));
    }

    private Image? Pixels(string file, Image.Format format = Image.Format.Rgba8)
    {
        var path = System.IO.Path.Combine(Root, file);
        var image = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image is null || image.IsEmpty())
        {
            return null;
        }
        image.Convert(format);
        return image;
    }
}
