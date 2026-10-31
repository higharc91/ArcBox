using System.Collections.Immutable;
using Godot;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// Draws a <see cref="BoardPlan"/> with its units merged in (<see cref="BoardOrder.Merge"/>), then the units' marks on top of
/// everything: each item in order, in art pixels, with the node's position and scale from <see cref="BoardFit"/>. Everything
/// it draws is worked out in <c>Tactics.Presentation.Board</c>. The textures live in the downloaded pack, outside
/// <c>res://</c>, so they're loaded from files, once each.
/// </summary>
public partial class BoardCanvas : Node2D
{
    /// <summary>How often animated art redraws: fast enough for its fastest strip (10 fps).</summary>
    private const double TicksPerSecond = 20;

    private sealed record DrawnFace(Face Face, Vector2[] Points, Vector2[] Outline, Vector2[] Uvs, Vector2[] FrameUvs, Color[] Colours, Texture2D? Texture);

    private sealed record DrawnSprite(Sprite Sprite, Rect2 Target, Vector2 Size, Texture2D? Texture);

    private const int RingPoints = 28;
    private const float RingWidth = 1.5f;
    private const float ActiveRingWidth = 2.5f;

    private static readonly Color GridLine = new(0, 0, 0, 0.22f);
    private static readonly Color PartyRing = new("63d36f");    // ui/manifest.json's palette: hp, enemy, gold
    private static readonly Color EnemyRing = new("e2534c");
    private static readonly Color NeutralRing = new("dbceb7");
    private static readonly Color ActiveRing = new("ffe794");

    private readonly Dictionary<string, Texture2D?> _textures = [];
    private readonly List<object> _items = [];
    private readonly List<long> _orders = [];
    private ImmutableArray<BoardUnit> _units = [];
    private UnitArt? _art;
    private int[] _sequence = [];
    private string _root = "";
    private bool _animated;
    private double _clock;
    private long _tick;
    private readonly FxLayer _light;
    private readonly FxLayer _over;
    private Vector2 _origin;

    public BoardCanvas()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        TextureRepeat = TextureRepeatEnum.Enabled;   // a side deeper than its texture repeats
        _light = new FxLayer(Load, additive: true);
        _over = new FxLayer(Load, additive: false);
        AddChild(_light);
        AddChild(_over);
    }

    /// <summary>Shows <paramref name="plan"/>, loading its art from <paramref name="packRoot"/>, placed by <paramref name="fit"/>.</summary>
    public void SetPlan(BoardPlan plan, BoardFit fit, string packRoot)
    {
        _root = packRoot;
        _items.Clear();
        _orders.Clear();
        foreach (var item in plan.Items)
        {
            _items.Add(item switch
            {
                Face face => Prepare(face),
                Sprite sprite => Prepare(sprite),
                _ => throw new InvalidOperationException($"Unknown board item {item.GetType().Name}."),
            });
            _orders.Add(item.Order);
        }
        _animated = plan.Animated;
        _origin = new Vector2(fit.Origin.X, fit.Origin.Y);
        Position = _origin;
        Scale = new Vector2(fit.Scale, fit.Scale);
        Merge();
    }

    /// <summary>Shows <paramref name="units"/> over the current plan, which stays as it is, so this can run on every frame.</summary>
    public void SetUnits(ImmutableArray<BoardUnit> units, UnitArt art)
    {
        _units = units;
        _art = art;
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
        if (!_animated && _units.IsEmpty)
        {
            return;
        }
        _clock += delta;
        var tick = (long)(_clock * TicksPerSecond);
        if (tick != _tick)
        {
            _tick = tick;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
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
        if (_art is { } art)
        {
            foreach (var unit in _units)
            {
                DrawMarks(unit, art);
            }
        }
    }

    private void DrawFace(DrawnFace drawn)
    {
        if (drawn.Texture is null)
        {
            DrawColoredPolygon(drawn.Points, drawn.Colours[0]);
        }
        else
        {
            DrawPrimitive(drawn.Points, drawn.Colours, FrameUvs(drawn), drawn.Texture);
        }
        if (drawn.Face.Grid)
        {
            DrawPolyline(drawn.Outline, GridLine, 0.5f);
        }
    }

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
            DrawTextureRectRegion(drawn.Texture, drawn.Target, source);
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
            DrawTextureRectRegion(texture, target, new Rect2(source.X, source.Y, source.Width, source.Height));
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

    private void DrawMarks(BoardUnit unit, UnitArt art)
    {
        if (unit.Bar is { } bar)
        {
            DrawBar(bar, art.Bar(unit.Tone));
        }
        if (unit.Countdown is { } countdown)
        {
            DrawDigits(countdown, art);
        }
        if (Load(UnitArt.IconsPath) is { } icons)
        {
            for (var i = 0; i < unit.Icons.Length; i++)
            {
                var icon = unit.Icons[i];
                var at = new Vector2(unit.IconsAt.X + i * UnitPlan.IconStep, unit.IconsAt.Y);
                DrawTextureRectRegion(icons, new Rect2(at, new Vector2(icon.Width, icon.Height)), Region(icon));
            }
        }
        if (unit.ArrowAt is { } arrow && Load(art.Arrow.Path) is { } cursor)
        {
            var size = new Vector2(art.ArrowWidth, art.Arrow.Height);
            var corner = new Vector2(arrow.X - art.ArrowAnchorX, arrow.Y - art.ArrowAnchorY);
            DrawTextureRectRegion(cursor, new Rect2(corner, size), new Rect2(new Vector2(art.Arrow.FrameAt(_clock) * size.X, 0), size));
        }
    }

    /// <summary>ui/manifest.json's recipe: the frame stretched between its ends, then the fill with its bright leading edge.</summary>
    private void DrawBar(UnitBar bar, BarArt art)
    {
        if (Load(UnitArt.BarsPath) is not { } texture)
        {
            return;
        }
        var (x, y) = (bar.TopLeft.X, bar.TopLeft.Y);
        var frame = art.Frame;
        var (left, right, height) = (art.SliceLeft, art.SliceRight, frame.Height);
        DrawTextureRectRegion(texture, new Rect2(x, y, left, height), new Rect2(frame.X, frame.Y, left, height));
        DrawTextureRectRegion(texture, new Rect2(x + left, y, bar.Width - left - right, height),
            new Rect2(frame.X + left, frame.Y, frame.Width - left - right, height));
        DrawTextureRectRegion(texture, new Rect2(x + bar.Width - right, y, right, height),
            new Rect2(frame.X + frame.Width - right, frame.Y, right, height));

        if (bar.Fill <= 0)
        {
            return;
        }
        var (fillX, fillY) = (x + art.FillX, y + art.FillY);
        DrawTextureRectRegion(texture, new Rect2(fillX, fillY, bar.Fill, art.FillHeight), Region(art.Mid));
        DrawTextureRectRegion(texture, new Rect2(fillX, fillY, art.Left.Width, art.FillHeight), Region(art.Left));
        DrawTextureRectRegion(texture, new Rect2(fillX + bar.Fill - art.Right.Width, fillY, art.Right.Width, art.FillHeight), Region(art.Right));
    }

    private void DrawDigits(UnitCountdown countdown, UnitArt art)
    {
        if (Load(UnitArt.NumbersPath) is not { } texture)
        {
            return;
        }
        var glyphs = countdown.Text.Select(art.Digit).ToArray();
        var width = glyphs.Sum(g => g.Advance) * UnitPlan.CountdownScale;
        var pen = countdown.Centre.X - width / 2f;
        foreach (var (rect, advance) in glyphs)
        {
            var size = new Vector2(rect.Width, rect.Height) * UnitPlan.CountdownScale;
            DrawTextureRectRegion(texture, new Rect2(new Vector2(pen, countdown.Centre.Y - size.Y / 2), size), Region(rect));
            pen += advance * UnitPlan.CountdownScale;
        }
    }

    private static Rect2 Region(ArtRect rect) => new(rect.X, rect.Y, rect.Width, rect.Height);

    private DrawnFace Prepare(Face face)
    {
        var fill = face.Fill ?? new Rgb(1, 1, 1);
        var colour = new Color(fill.R * face.Shade, fill.G * face.Shade, fill.B * face.Shade, face.Alpha);
        Vector2[] points = [.. face.Points.Select(p => new Vector2(p.X, p.Y))];
        Vector2[] uvs = [.. face.Uvs.Select(p => new Vector2(p.X, p.Y))];
        return new DrawnFace(face, points, [.. points, points[0]], uvs, new Vector2[uvs.Length], [colour, colour, colour, colour], Load(face.Image?.Path));
    }

    private DrawnSprite Prepare(Sprite sprite)
    {
        var size = new Vector2(sprite.Size.X, sprite.Size.Y);
        return new DrawnSprite(sprite, new Rect2(new Vector2(sprite.TopLeft.X, sprite.TopLeft.Y), size), size, Load(sprite.Image.Path));
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
