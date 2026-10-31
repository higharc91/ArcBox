using System.Collections.Immutable;
using Godot;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Draws a <see cref="BoardPlan"/> with its units merged in (<see cref="BoardOrder.Merge"/>), each item in order, in art pixels, with
/// the node's position and scale from <see cref="BoardFit"/>, then over that the sources' halos, the units' marks (<see cref="UnitMarks"/>)
/// and the beat's effects. Everything it draws is worked out in <c>Tactics.Presentation.Board</c>. The textures live in the downloaded
/// pack, outside <c>res://</c>, so they're loaded from files, once each.
/// <para>
/// Light is in the vertex colours, read from <see cref="BoardLighting"/>: art x light, as the pack's recipe has it. So it costs no extra
/// pass and no shader, nothing is capped by how many lights reach one canvas item, and what's drawn unmodulated, like rings, is simply
/// unlit. Why not Light2D is in docs/tv-host.md. What glows by itself is drawn again, unlit, in its
/// place in the painter's order, so what stands in front of it still hides it.
/// </para>
/// </summary>
public partial class BoardCanvas : Node2D
{
    /// <summary>How often animated art redraws: fast enough for its fastest strip (10 fps).</summary>
    private const double TicksPerSecond = 20;

    private sealed record DrawnFace(Face Face, Vector2[] Points, Vector2[]? Outline, Vector2[] Uvs, Vector2[] FrameUvs, Color Colour, Color[] Colours,
        Texture2D? Texture, Texture2D? Glow, LightProbe[]? Probes);

    private sealed record DrawnSprite(Sprite Sprite, Rect2 Target, Vector2 Size, Texture2D? Texture, Texture2D? Glow, LightProbe? Probe);

    /// <summary>How far up a figure its light is read, as a share of its sprite: its middle, more or less.</summary>
    private const float BodyHeight = 0.3f;

    private const int RingPoints = 28;
    private const float RingWidth = 1.5f;
    private const float ActiveRingWidth = 2.5f;

    private static readonly Color GridLine = new(0, 0, 0, 0.22f);
    private static readonly Color PartyRing = new("63d36f");    // ui/manifest.json's palette: hp, enemy, gold
    private static readonly Color EnemyRing = new("e2534c");
    private static readonly Color NeutralRing = new("dbceb7");
    private static readonly Color ActiveRing = new("ffe794");
    private static readonly Color[] Unlit = [Colors.White, Colors.White, Colors.White, Colors.White];

    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly Dictionary<string, Texture2D?> _glows = [];
    private readonly List<object> _items = [];
    private readonly List<long> _orders = [];
    private ImmutableArray<BoardUnit> _units = [];
    private int[] _sequence = [];
    private string _root = "";
    private bool _animated;
    private double _clock;
    private long _tick;
    private readonly FxLayer _halos;
    private readonly UnitMarks _marks;
    private readonly FxLayer _light;
    private readonly FxLayer _over;
    private BoardLighting? _lighting;
    private Vector2 _origin;

    public BoardCanvas()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        TextureRepeat = TextureRepeatEnum.Enabled;   // a side deeper than its texture repeats
        _halos = new FxLayer(Load, additive: true);
        _marks = new UnitMarks(Load);
        _light = new FxLayer(Load, additive: true);
        _over = new FxLayer(Load, additive: false);
        AddChild(_halos);
        AddChild(_marks);
        AddChild(_light);
        AddChild(_over);
    }

    /// <summary>Shows <paramref name="plan"/> in <paramref name="lighting"/>, loading its art from <paramref name="packRoot"/>, placed by <paramref name="fit"/>.</summary>
    public void SetPlan(BoardPlan plan, BoardLighting lighting, BoardFit fit, string packRoot)
    {
        _root = packRoot;
        _lighting = lighting.Active ? lighting : null;
        _items.Clear();
        _orders.Clear();
        foreach (var item in plan.Items)
        {
            switch (item)
            {
                case Face face:
                    AddFace(face);
                    break;
                case Sprite sprite:
                    Add(Prepare(sprite), sprite.Order);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown board item {item.GetType().Name}.");
            }
        }
        _halos.Show(_lighting?.Halos(_clock) ?? []);
        _animated = plan.Animated;
        _origin = new Vector2(fit.Origin.X, fit.Origin.Y);
        Position = _origin;
        Scale = new Vector2(fit.Scale, fit.Scale);
        Merge();
    }

    private void Add(object drawn, long order)
    {
        _items.Add(drawn);
        _orders.Add(order);
    }

    /// <summary>A top that light reaches is drawn as cells, so a pool can peak inside a tile; its outline goes last, over all of them.</summary>
    private void AddFace(Face face)
    {
        if (_lighting is not { } lighting || face.Kind != FaceKind.Top || !face.Lit || !Reached(face, lighting))
        {
            Add(Prepare(face, face.Grid), face.Order);
            return;
        }
        var cells = face.Cells();
        for (var i = 0; i < cells.Length; i++)
        {
            var last = i == cells.Length - 1;
            Add(Prepare(cells[i], face.Grid && last, last ? face.Points : null), face.Order);
        }
    }

    private static bool Reached(Face face, BoardLighting lighting)
        => face.Points.Append((face.Points[0] + face.Points[2]) / 2).Any(p => lighting.Probe(p).Count > 0);

    /// <summary>Shows <paramref name="units"/> over the current plan, which stays as it is, so this can run on every frame.</summary>
    public void SetUnits(ImmutableArray<BoardUnit> units, UnitArt art)
    {
        _units = units;
        _marks.Show(units, art);
        Merge();
    }

    /// <summary>Shows a beat's effects, numbers and badges over everything, with the board <paramref name="shake"/> art pixels off its place.</summary>
    public void SetEffects(ImmutableArray<FxDraw> draws, System.Numerics.Vector2 shake)
    {
        _light.Show(draws);
        _over.Show(draws);
        Position = _origin + new Vector2(shake.X, shake.Y) * Scale;
    }

    private void Merge()
    {
        _sequence = BoardOrder.Merge(_orders, [.. _units.Select(u => u.Order)]);
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (!_animated && _units.IsEmpty && _lighting is null)
        {
            return;
        }
        _clock += delta;
        var tick = (long)(_clock * TicksPerSecond);
        if (tick != _tick)
        {
            _tick = tick;
            _marks.Tick(_clock);
            _halos.Show(_lighting?.Halos(_clock) ?? []);
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        _lighting?.Step(_clock);
        foreach (var step in _sequence)
        {
            if (step < 0)
            {
                DrawUnit(_units[~step]);
            }
            else if (_items[step] is DrawnFace face)
            {
                DrawFace(face);
            }
            else if (_items[step] is DrawnSprite sprite)
            {
                DrawSprite(sprite);
            }
        }
    }

    private void DrawFace(DrawnFace drawn)
    {
        LightUp(drawn);
        if (drawn.Texture is null)
        {
            DrawColoredPolygon(drawn.Points, drawn.Colours[0]);
        }
        else
        {
            var uvs = FrameUvs(drawn);
            DrawPrimitive(drawn.Points, drawn.Colours, uvs, drawn.Texture);
            if (drawn.Glow is not null)
            {
                DrawPrimitive(drawn.Points, Unlit, uvs, drawn.Glow);
            }
        }
        if (drawn.Outline is not null)
        {
            DrawPolyline(drawn.Outline, GridLine, 0.5f);
        }
    }

    private void LightUp(DrawnFace drawn)
    {
        if (drawn.Probes is not { } probes || _lighting is not { } lighting)
        {
            return;
        }
        var colour = drawn.Colour;
        for (var i = 0; i < probes.Length; i++)
        {
            drawn.Colours[i] = Lit(colour, lighting.At(probes[i]));
        }
    }

    private static Color Lit(Color colour, Rgb light) => new(colour.R * light.R, colour.G * light.G, colour.B * light.B, colour.A);

    private Vector2[] FrameUvs(DrawnFace drawn)
    {
        if (drawn.Face.Image is not { Frames: > 1 } image)
        {
            return drawn.Uvs;
        }
        var frame = image.FrameAt(_clock);
        for (var i = 0; i < drawn.Uvs.Length; i++)
        {
            drawn.FrameUvs[i] = new Vector2(image.FrameU(drawn.Uvs[i].X, frame), drawn.Uvs[i].Y);
        }
        return drawn.FrameUvs;
    }

    private void DrawSprite(DrawnSprite drawn)
    {
        if (drawn.Texture is not null)
        {
            var source = new Rect2(new Vector2(drawn.Sprite.Image.FrameAt(_clock) * drawn.Size.X, 0), drawn.Size);
            var light = drawn.Probe is not null && _lighting is { } lighting ? Lit(Colors.White, lighting.At(drawn.Probe)) : Colors.White;
            DrawTextureRectRegion(drawn.Texture, drawn.Target, source, light);
            if (drawn.Glow is not null)
            {
                DrawTextureRectRegion(drawn.Glow, drawn.Target, source);
            }
        }
    }

    private void DrawUnit(BoardUnit unit)
    {
        var feet = new Vector2(unit.Feet.X, unit.Feet.Y);
        var clock = _clock + unit.Phase;
        DrawRing(feet, unit);
        foreach (var effect in unit.Under)
        {
            DrawEffect(effect, feet, clock);
        }

        var sprite = unit.Sprite;
        var size = new Vector2(sprite.Width, sprite.Height);
        var corner = feet - new Vector2(unit.Mirror ? sprite.Width - sprite.AnchorX : sprite.AnchorX, sprite.AnchorY);
        if (Load(sprite.Path) is { } texture)
        {
            var source = sprite.Source(unit.Anim.FrameAt(clock), unit.Row);
            var target = new Rect2(corner, unit.Mirror ? new Vector2(-size.X, size.Y) : size);   // a negative width flips, in the same place
            var light = _lighting is { } lighting ? Lit(Colors.White, lighting.ForFigure(new(feet.X, feet.Y - sprite.Height * BodyHeight))) : Colors.White;
            DrawTextureRectRegion(texture, target, new Rect2(source.X, source.Y, source.Width, source.Height), light);
        }
        else
        {
            DrawTokenFor(unit, feet);
        }

        var shift = new Vector2(0, sprite.EffectShift);
        foreach (var effect in unit.Over)
        {
            DrawEffect(effect, feet + shift, clock);
        }
    }

    /// <summary>The ring at a unit's feet, on the slant of the tile.</summary>
    private void DrawRing(Vector2 feet, BoardUnit unit)
    {
        var points = new Vector2[RingPoints + 1];
        for (var i = 0; i <= RingPoints; i++)
        {
            var angle = Mathf.Tau * i / RingPoints;
            points[i] = feet + new Vector2(Mathf.Cos(angle) * unit.Ring, Mathf.Sin(angle) * unit.Ring / 2);
        }
        DrawPolyline(points, unit.Active ? ActiveRing : ToneColour(unit.Tone), unit.Active ? ActiveRingWidth : RingWidth);
    }

    /// <summary>What's left when a sprite's file is missing: a marker in the unit's colour, so it's still on the board.</summary>
    private void DrawTokenFor(BoardUnit unit, Vector2 feet)
    {
        var (halfWidth, height) = (unit.Ring * 0.7f, 26f);
        DrawColoredPolygon([feet, feet + new Vector2(halfWidth, -height / 2), feet + new Vector2(0, -height), feet + new Vector2(-halfWidth, -height / 2)], ToneColour(unit.Tone));
    }

    private static Color ToneColour(UnitTone tone) => tone switch
    {
        UnitTone.Party => PartyRing,
        UnitTone.Enemy => EnemyRing,
        _ => NeutralRing,
    };

    private void DrawEffect(StatusEffect effect, Vector2 anchor, double clock)
    {
        if (Load(effect.Path) is not { } texture)
        {
            return;
        }
        var frame = (int)(clock * effect.Fps) % effect.Frames;
        var size = new Vector2(UnitArt.EffectWidth, UnitArt.EffectHeight);
        var corner = anchor - new Vector2(UnitArt.EffectAnchorX, UnitArt.EffectAnchorY);
        DrawTextureRectRegion(texture, new Rect2(corner, size), new Rect2(new Vector2(frame * size.X, 0), size));
    }

    private DrawnFace Prepare(Face face, bool outlined, ImmutableArray<System.Numerics.Vector2>? outline = null)
    {
        var fill = face.Fill ?? new Rgb(1, 1, 1);
        var colour = new Color(fill.R * face.Shade, fill.G * face.Shade, fill.B * face.Shade, face.Alpha);
        Vector2[] points = [.. face.Points.Select(p => new Vector2(p.X, p.Y))];
        Vector2[] uvs = [.. face.Uvs.Select(p => new Vector2(p.X, p.Y))];
        Vector2[] ring = [.. (outline ?? face.Points).Select(p => new Vector2(p.X, p.Y))];
        var probes = _lighting is { } lighting && face.Lit ? face.Points.Select(lighting.Probe).ToArray() : null;
        var glow = _lighting is not null && face.Image is { Glow: { } rule } image ? Glow(image.Path, null, rule.Colour) : null;
        return new DrawnFace(face, points, outlined ? [.. ring, ring[0]] : null, uvs, new Vector2[uvs.Length], colour,
            [colour, colour, colour, colour], Load(face.Image?.Path), glow, probes);
    }

    private DrawnSprite Prepare(Sprite sprite)
    {
        var size = new Vector2(sprite.Size.X, sprite.Size.Y);
        var probe = _lighting is { } lighting && sprite.Lit ? lighting.Probe(sprite.TopLeft + sprite.Size / 2) : null;
        var glow = sprite.Light is { } light ? Glow(sprite.Image.Path, light.Mask, light.Colour) : null;
        return new DrawnSprite(sprite, new Rect2(new Vector2(sprite.TopLeft.X, sprite.TopLeft.Y), size), size, Load(sprite.Image.Path), glow, probe);
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
        var path = System.IO.Path.Combine(_root, file);
        var image = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image is null || image.IsEmpty())
        {
            return null;
        }
        image.Convert(format);
        return image;
    }

    /// <summary>A file of the pack, relative to its root, or null if there's none or it can't be read.</summary>
    private Texture2D? Load(string? file)
    {
        if (file is null)
        {
            return null;
        }
        if (_textures.TryGetValue(file, out var known))
        {
            return known;
        }

        var path = System.IO.Path.Combine(_root, file);
        var loaded = System.IO.File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (loaded is null || loaded.IsEmpty())
        {
            GD.PushWarning($"Tactics board: can't load {path}");
            return _textures[file] = null;
        }
        return _textures[file] = ImageTexture.CreateFromImage(loaded);
    }
}
