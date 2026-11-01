using Godot;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Draws the items of a <see cref="LitBoard"/> on any canvas item, and holds the pack's textures. A board and a block of the world paint
/// alike and differ in their light. A board reads it at each draw, from the lighting passed to <see cref="Paint"/>. A block is drawn once,
/// so <see cref="Prepare(LitItem, BoardLighting?)"/> bakes each item at its mean from the lighting of the whole region, and the block paints
/// with no lighting at all. <see cref="Prepare(LitItem, BoardLighting?)"/> makes only arrays and is safe off the main thread; textures
/// are attached by <see cref="Bind"/>, on it. With <see cref="Frames"/>, what's animated is drawn from a single frame that changes in place,
/// so it goes on moving where it's recorded once.
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
        public bool OneFrame { get; set; }
    }

    private sealed class DrawnSprite(LitSprite lit, Rect2 target, Vector2 size, Color baked)
    {
        public LitSprite Lit { get; } = lit;
        public Rect2 Target { get; } = target;
        public Vector2 Size { get; } = size;
        public Color Baked { get; } = baked;
        public Texture2D? Texture { get; set; }
        public Texture2D? Glow { get; set; }
        public bool OneFrame { get; set; }
    }

    private static readonly Color GridLine = new(0, 0, 0, 0.22f);
    private static readonly Color[] Unlit = [Colors.White, Colors.White, Colors.White, Colors.White];

    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly Dictionary<string, Texture2D?> _tops = [];
    private readonly Dictionary<string, Texture2D?> _glows = [];

    /// <summary>The pack's root, which files are read from.</summary>
    public string Root { get; set; } = "";

    /// <summary>How much of a tile's outline shows, 0 to 1: a fight in the world fades its grid in and out (overworld 5c §5). Set while painting, on the main thread, by the board being painted.</summary>
    public static float GridAlpha { get; set; } = 1;

    /// <summary>Where animated art goes, for drawing that's recorded once; null to step through its strip at each draw.</summary>
    public FrameTextures? Frames { get; set; }

    /// <summary>Lets go of every texture, now rather than when the collector comes round. Nothing may paint with this painter afterwards.</summary>
    public void Release()
    {
        foreach (var texture in _textures.Values.Concat(_tops.Values).Concat(_glows.Values).OfType<Texture2D>().Distinct())
        {
            texture.Dispose();
        }
        _textures.Clear();
        _tops.Clear();
        _glows.Clear();
    }

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
            face.Texture = lit.Art ? Texture(lit.Face.Image, lit.Face.Facing == Facing.Up) : null;
            face.Glow = lit.Glow && lit.Face.Image is { Glow: { } rule } image ? Glow(image, null, rule.Colour) : null;
            face.OneFrame = Moves(lit.Face.Image);
        }
        else if (drawn is DrawnSprite sprite)
        {
            var lit = sprite.Lit;
            sprite.Texture = Texture(lit.Sprite.Image, false);
            sprite.Glow = lit.Glow && lit.Sprite.Light is { } light ? Glow(lit.Sprite.Image, light.Mask, light.Colour) : null;
            sprite.OneFrame = Moves(lit.Sprite.Image);
        }
    }

    /// <summary>Loads an image the way items that lie <paramref name="flat"/> or stand will draw it. Only on the main thread: this reads a file.</summary>
    public void Preload(ArtImage image, bool flat) => Texture(image, flat);

    /// <summary>True when <paramref name="drawn"/> is a prepared tall prop.</summary>
    public static bool IsTallProp(object drawn) => drawn is DrawnSprite { Lit.IsTallProp: true };

    /// <summary>The painter's order key of <paramref name="drawn"/>.</summary>
    public static long OrderOf(object drawn) => drawn is DrawnSprite s ? s.Lit.Order : drawn is DrawnFace f ? f.Lit.Order : 0;

    /// <summary>The screen target rectangle of <paramref name="drawn"/>.</summary>
    public static Rect2 TargetOf(object drawn) => drawn is DrawnSprite s ? s.Target : default;

    /// <summary>The tile of <paramref name="drawn"/>.</summary>
    public static Tile TileOf(object drawn) => drawn is DrawnSprite s ? s.Lit.Sprite.Tile : default;

    /// <summary>Paints one item on <paramref name="canvas"/> as things stand at <paramref name="clock"/>. With no <paramref name="now"/>, its colours are as prepared.</summary>
    public void Paint(CanvasItem canvas, object drawn, BoardLighting? now, double clock) => Paint(canvas, drawn, now, clock, 1f);

    /// <summary>Paints one item on <paramref name="canvas"/> with <paramref name="alpha"/> modulation.</summary>
    public void Paint(CanvasItem canvas, object drawn, BoardLighting? now, double clock, float alpha)
    {
        if (drawn is DrawnFace face)
        {
            PaintFace(canvas, face, now, clock);
        }
        else if (drawn is DrawnSprite sprite)
        {
            PaintSprite(canvas, sprite, now, clock, alpha);
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
        return _textures[file] = Upload(loaded);
    }

    /// <summary>A texture of <paramref name="pixels"/>, which are done with. An Image's memory is native, so the collector doesn't know to hurry.</summary>
    private static ImageTexture Upload(Image pixels)
    {
        var texture = ImageTexture.CreateFromImage(pixels);
        pixels.Dispose();
        return texture;
    }

    /// <summary>Cuts <paramref name="pixels"/> into <see cref="Frames"/>, and they're done with.</summary>
    private Texture2D Strip(string key, ArtImage image, Image pixels)
    {
        var texture = Frames!.Add(key, image, pixels);
        pixels.Dispose();
        return texture;
    }

    /// <summary>What an image is drawn with. A top lies turned on its tile, so it goes through <see cref="DitherTexels"/>.</summary>
    private Texture2D? Texture(ArtImage? image, bool flat)
    {
        if (image is null)
        {
            return null;
        }
        if (Moves(image))
        {
            return Frames!.Of(image.Path) ?? ((flat ? Turned(image) : Pixels(image.Path)) is { } strip ? Strip(image.Path, image, strip) : Load(image.Path));
        }
        return flat ? LoadTop(image) : Load(image.Path);
    }

    private bool Moves(ArtImage? image) => Frames is not null && image is { Frames: > 1 };

    private Texture2D? LoadTop(ArtImage image)
    {
        if (_tops.TryGetValue(image.Path, out var known))
        {
            return known;
        }
        return _tops[image.Path] = Turned(image) is { } top ? Upload(top) : Load(image.Path);   // which warns
    }

    /// <summary>A top's pixels as they go to the TV: doubled if it has dither, and as they are if not.</summary>
    private Image? Turned(ArtImage image)
    {
        if (Pixels(image.Path) is not { } art)
        {
            return null;
        }
        var doubled = DitherTexels.Doubled(art.GetData(), art.GetWidth(), art.GetHeight(), image.Frames);
        if (doubled is null)
        {
            return art;
        }
        var (width, height) = (art.GetWidth(), art.GetHeight());
        art.Dispose();
        return Image.CreateFromData(width * DitherTexels.Scale, height * DitherTexels.Scale, false, Image.Format.Rgba8, doubled);
    }

    private static DrawnFace Prepare(LitFace lit, BoardLighting? baked)
    {
        var face = lit.Face;
        Vector2[] points = [.. face.Points.Select(p => new Vector2(p.X, p.Y))];
        Vector2[] uvs = [.. face.Uvs.Select(p => new Vector2(p.X, p.Y))];
        var colour = Colour(face.Tint, face.Alpha);
        Color[] colours = face.VertexTints.IsDefaultOrEmpty
            ? [colour, colour, colour, colour]
            : [.. face.VertexTints.Select(t => Colour(t, face.Alpha))];
        if (baked is not null)
        {
            for (var i = 0; i < colours.Length; i++)
            {
                colours[i] = Colour(lit.MeanColourAt(i, baked), face.Alpha);
            }
        }
        return new DrawnFace(baked is null ? lit : lit with { Probes = [] }, points, face.Grid ? [.. points, points[0]] : null, uvs, colours);
    }

    private static DrawnSprite Prepare(LitSprite lit, BoardLighting? baked)
    {
        var sprite = lit.Sprite;
        var size = new Vector2(sprite.Size.X, sprite.Size.Y);
        return new DrawnSprite(baked is null ? lit : lit with { Probe = null }, new Rect2(new Vector2(sprite.TopLeft.X, sprite.TopLeft.Y), size), size,
            baked is null ? Colors.White : Colour(lit.MeanLight(baked)));
    }

    private static void PaintFace(CanvasItem canvas, DrawnFace drawn, BoardLighting? now, double clock)
    {
        LightUp(drawn, now);
        var uvs = drawn.OneFrame ? drawn.Uvs : FrameUvs(drawn, clock);
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
        if (drawn.Outline is not null && GridAlpha > 0)
        {
            canvas.DrawPolyline(drawn.Outline, GridLine with { A = GridLine.A * GridAlpha }, 0.5f);
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

    private static void PaintSprite(CanvasItem canvas, DrawnSprite drawn, BoardLighting? now, double clock, float alpha = 1f)
    {
        if (drawn.Texture is not null)
        {
            var source = new Rect2(new Vector2(drawn.OneFrame ? 0 : drawn.Lit.Sprite.Image.FrameAt(clock) * drawn.Size.X, 0), drawn.Size);
            var light = now is null ? drawn.Baked : Colour(drawn.Lit.LightNow(now));
            if (alpha < 1f)
            {
                light = new Color(light.R, light.G, light.B, light.A * alpha);
            }
            canvas.DrawTextureRectRegion(drawn.Texture, drawn.Target, source, light);
            if (drawn.Glow is not null)
            {
                var glow = alpha < 1f ? new Color(1, 1, 1, alpha) : Colors.White;
                canvas.DrawTextureRectRegion(drawn.Glow, drawn.Target, source, glow);
            }
        }
    }

    /// <summary>What of a strip of the pack glows by itself, as a texture of its own: once each, as it's work over every pixel.</summary>
    private Texture2D? Glow(ArtImage image, string? mask, Rgb colour)
    {
        if (_glows.TryGetValue(image.Path, out var known))
        {
            return known;
        }
        if (Bake(image.Path, mask, colour) is not { } glow)
        {
            return _glows[image.Path] = null;
        }
        return _glows[image.Path] = Moves(image) ? Strip($"glow:{image.Path}", image, glow) : Upload(glow);
    }

    private Image? Bake(string file, string? mask, Rgb colour)
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
            masked.Dispose();
        }
        else
        {
            art.Dispose();
            return null;
        }
        var (width, height) = (art.GetWidth(), art.GetHeight());
        art.Dispose();
        return Image.CreateFromData(width, height, false, Image.Format.Rgba8, glow);
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
