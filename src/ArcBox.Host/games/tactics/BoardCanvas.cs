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
/// <para>
/// Over a world it paints no board of its own: <see cref="SetDrawn"/> gives it, frame by frame, only what stands in front of the units, already
/// baked by <see cref="WorldCanvas"/>, and the units, their marks and the halos in view go through here as they do in a battle.
/// </para>
/// </summary>
public partial class BoardCanvas : Node2D
{
    /// <summary>How often animated art redraws: fast enough for its fastest strip (10 fps).</summary>
    private const double TicksPerSecond = 20;

    private const int RingPoints = 28;
    private const float RingWidth = 1.5f;
    private const float ActiveRingWidth = 2.5f;
    private const float OutlinedRingWidth = 2f;       // a walker's ring, over a dark one, so the party's green reads on grass
    private const float RingOutlineWidth = 4f;
    private const float RingOutlineAlpha = 0.8f;

    private static readonly Color PartyRing = new("63d36f");    // ui/manifest.json's palette: hp, enemy, gold
    private static readonly Color EnemyRing = new("e2534c");
    private static readonly Color NeutralRing = new("dbceb7");
    private static readonly Color ActiveRing = new("ffe794");
    private static readonly Color AwayShade = new(0.68f, 0.68f, 0.74f, 0.82f);     // a walker whose phone has gone: still one of the party

    private readonly BoardPainter _painter;
    private readonly List<object> _items = [];
    private readonly List<long> _orders = [];
    private readonly List<float> _alphas = [];
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
    private bool _baked;
    private (System.Numerics.Vector2 Min, System.Numerics.Vector2 Max)? _view;
    private float _gridAlpha = 1;
    private bool _clocked;
    private readonly bool _ownsPainter;

    public BoardCanvas()
        : this(new BoardPainter())
    {
        _ownsPainter = true;
    }

    /// <summary>A board that shares <paramref name="painter"/>'s textures, as one over a world does.</summary>
    internal BoardCanvas(BoardPainter painter)
    {
        _painter = painter;
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
        Planned = true;
        _painter.Root = packRoot;
        _lighting = lit.Lighting.Active ? lit.Lighting : null;
        _items.Clear();
        _orders.Clear();
        _alphas.Clear();
        foreach (var item in lit.Items)
        {
            var drawn = BoardPainter.Prepare(item);
            _painter.Bind(drawn);
            _items.Add(drawn);
            _orders.Add(item.Order);
            _alphas.Add(1);
        }
        _halos.Show(HalosNow());
        _animated = plan.Animated;
        _origin = new Vector2(fit.Origin.X, fit.Origin.Y);
        Position = _origin;
        Scale = new Vector2(fit.Scale, fit.Scale);
        Merge();
    }

    /// <summary>It has a plan from <see cref="SetPlan"/>, and the textures for it, since it was made or last let go (<see cref="LetGo"/>).</summary>
    public bool Planned { get; private set; }

    /// <summary>
    /// Lets go of the plan, the units, the effects and every texture, at once. For the plain board once its fight has gone onto the world, which
    /// has its own copy of the same art: kept, they'd sit in video memory beside the world's for as long as the scene lasts (the hardware check's
    /// 55.6 MB against 38.2 MB, after a TV came back mid-fight). Nothing's drawn until the next <see cref="SetPlan"/>, which loads what it needs.
    /// Only a board with a painter of its own: a world's board shares the world's.
    /// </summary>
    public void LetGo()
    {
        if (!_ownsPainter || !Planned)
        {
            return;
        }
        Planned = false;
        _items.Clear();
        _orders.Clear();
        _alphas.Clear();
        (_units, _sequence, _lighting) = ([], [], null);
        _marks.Clear();
        _halos.Show([]);
        _light.Show([]);
        _over.Show([]);
        // What was drawn last is still recorded, textures and all, and a hidden node isn't drawn again to replace it: clear it now.
        foreach (var item in new CanvasItem[] { this, _halos, _marks, _light, _over })
        {
            RenderingServer.CanvasItemClear(item.GetCanvasItem());
        }
        _painter.Release();
    }

    /// <summary>Moves the board to <paramref name="fit"/> without building anything again: a fight in the world goes where the stage is.</summary>
    public void SetFit(BoardFit fit)
    {
        _origin = new Vector2(fit.Origin.X, fit.Origin.Y);
        Position = _origin;
        Scale = new Vector2(fit.Scale, fit.Scale);
    }

    /// <summary>How much of the grid shows, 0 to 1 (<see cref="Tactics.Presentation.World.StageDirector.GridAlpha"/>).</summary>
    public float GridAlpha
    {
        get => _gridAlpha;
        set
        {
            if (value != _gridAlpha)
            {
                _gridAlpha = value;
                QueueRedraw();
            }
        }
    }

    /// <summary>The board's clock, in seconds: what its units bob and its art steps by. Over a world it's the world's, so nothing steps at the seam.</summary>
    public double Clock
    {
        get => _clock;
        set => (_clock, _clocked) = (value, true);
    }

    /// <summary>How long the last drawing took, in milliseconds.</summary>
    public double LastDrawMs { get; private set; }

    /// <summary>Draws over a world lit by <paramref name="lighting"/>, with items whose colours are baked. Units still read the light as it flickers.</summary>
    internal void JoinWorld(BoardLighting? lighting)
    {
        _lighting = lighting;
        _baked = true;
    }

    /// <summary>
    /// Paints <paramref name="items"/>, prepared by <see cref="BoardPainter.Prepare(LitItem, BoardLighting?)"/> and bound, in place of a plan. Their <paramref name="orders"/> are the
    /// painter's order, and <paramref name="alphas"/>, if any, what each is painted at.
    /// </summary>
    internal void SetDrawn(IEnumerable<object> items, IEnumerable<long> orders, IEnumerable<float>? alphas = null)
    {
        _items.Clear();
        _items.AddRange(items);
        _orders.Clear();
        _orders.AddRange(orders);
        _alphas.Clear();
        if (alphas is null)
        {
            _alphas.AddRange(Enumerable.Repeat(1f, _items.Count));
        }
        else
        {
            _alphas.AddRange(alphas);
        }
        Merge();
    }

    /// <summary>Limits the halos to a view of the board, in art pixels, as a region has far more of them than a screen.</summary>
    internal void SetView(System.Numerics.Vector2 min, System.Numerics.Vector2 max) => _view = (min, max);

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
        if (!_clocked)
        {
            _clock += delta;
        }
        var tick = (long)(_clock * TicksPerSecond);
        if (tick != _tick)
        {
            _tick = tick;
            _marks.Tick(_clock);
            _halos.Show(HalosNow());
            QueueRedraw();
        }
    }

    private ImmutableArray<FxDraw> HalosNow()
    {
        if (_lighting is null)
        {
            return [];
        }
        return _view is { } view ? _lighting.Halos(_clock, view.Min, view.Max) : _lighting.Halos(_clock);
    }

    public override void _Draw()
    {
        var clock = System.Diagnostics.Stopwatch.GetTimestamp();
        _lighting?.Step(_clock);
        var light = _baked ? null : _lighting;
        BoardPainter.GridAlpha = _gridAlpha;
        foreach (var step in _sequence)
        {
            if (step < 0)
            {
                DrawUnit(_units[~step]);
            }
            else
            {
                _painter.Paint(this, _items[step], light, _clock, _alphas[step]);
            }
        }
        BoardPainter.GridAlpha = 1;
        LastDrawMs = System.Diagnostics.Stopwatch.GetElapsedTime(clock).TotalMilliseconds;
    }

    private static Color Colour(Rgb colour, float alpha = 1) => new(colour.R, colour.G, colour.B, alpha);

    private void DrawUnit(BoardUnit unit)
    {
        if (unit.MarksOnly)
        {
            return;     // only its emote, which UnitMarks draws
        }
        var feet = new Vector2(unit.Feet.X, unit.Feet.Y);
        var clock = _clock + unit.Phase;
        if (unit.Ring > 0)
        {
            DrawRing(feet, unit);
        }
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
            light = unit.Away ? light * AwayShade : light;
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
        if (unit.Outlined)
        {
            DrawPolyline(points, Colour(UnitPlan.RingOutline, RingOutlineAlpha), RingOutlineWidth);
            DrawPolyline(points, unit.Away ? ToneColour(unit.Tone) * AwayShade : ToneColour(unit.Tone), OutlinedRingWidth);
            return;
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
