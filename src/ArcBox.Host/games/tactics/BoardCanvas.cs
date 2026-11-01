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
/// Light is in the vertex colours, read from <see cref="BoardLighting"/>: art x light, as the pack's recipe has it, and what light does to
/// each item is worked out in <see cref="LitBoard"/>. So there's no extra pass and no shader, and what's drawn unmodulated, like rings, is
/// simply unlit. Why not Light2D is in docs/tv-host.md. What glows by itself is drawn again, unlit, in its place in the painter's order,
/// so what stands in front of it still hides it. The items themselves are painted by <see cref="BoardPainter"/>, which a world's blocks share.
/// </para>
/// </summary>
public partial class BoardCanvas : Node2D
{
    /// <summary>How often animated art redraws: fast enough for its fastest strip (10 fps).</summary>
    private const double TicksPerSecond = 20;

    private const int RingPoints = 28;
    private const float RingWidth = 1.5f;
    private const float ActiveRingWidth = 2.5f;

    private static readonly Color PartyRing = new("63d36f");    // ui/manifest.json's palette: hp, enemy, gold
    private static readonly Color EnemyRing = new("e2534c");
    private static readonly Color NeutralRing = new("dbceb7");
    private static readonly Color ActiveRing = new("ffe794");

    private readonly BoardPainter _painter = new();
    private readonly List<object> _items = [];
    private readonly List<long> _orders = [];
    private ImmutableArray<BoardUnit> _units = [];
    private int[] _sequence = [];
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

    /// <summary>Shows <paramref name="plan"/> in its light <paramref name="lit"/>, loading its art from <paramref name="packRoot"/>, placed by <paramref name="fit"/>.</summary>
    public void SetPlan(BoardPlan plan, LitBoard lit, BoardFit fit, string packRoot)
    {
        _painter.Root = packRoot;
        _lighting = lit.Lighting.Active ? lit.Lighting : null;
        _items.Clear();
        _orders.Clear();
        foreach (var item in lit.Items)
        {
            var drawn = BoardPainter.Prepare(item);
            _painter.Bind(drawn);
            _items.Add(drawn);
            _orders.Add(item.Order);
        }
        _halos.Show(_lighting?.Halos(_clock) ?? []);
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
            else
            {
                _painter.Paint(this, _items[step], _lighting, _clock);
            }
        }
    }

    private static Color Colour(Rgb colour, float alpha = 1) => new(colour.R, colour.G, colour.B, alpha);

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
            var light = _lighting is { } lighting ? Colour(lighting.ForFigure(BoardLighting.BodyOf(unit.Feet, sprite.Height))) : Colors.White;
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

    private Texture2D? Load(string? file) => _painter.Load(file);
}
