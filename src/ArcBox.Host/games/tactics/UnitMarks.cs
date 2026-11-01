using System.Collections.Immutable;
using Godot;
using Tactics.Presentation.Board;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// What's drawn over the units for the people watching, not as part of the scene: HP bars, Downed counts, condition icons and the turn
/// arrow. It's its own canvas item, above the board and its light, so these are never lit or glowed over.
/// </summary>
public partial class UnitMarks : Node2D
{
    private readonly Func<string?, Texture2D?> _load;
    private ImmutableArray<BoardUnit> _units = [];
    private UnitArt? _art;
    private double _clock;

    public UnitMarks()
    {
        _load = _ => null;
    }

    public UnitMarks(Func<string?, Texture2D?> load)
    {
        _load = load;
    }

    /// <summary>Shows the marks of <paramref name="units"/>.</summary>
    public void Show(ImmutableArray<BoardUnit> units, UnitArt art)
    {
        _units = units;
        _art = art;
        QueueRedraw();
    }

    /// <summary>Moves what's animated on to a moment.</summary>
    public void Tick(double clock)
    {
        _clock = clock;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_art is not { } art)
        {
            return;
        }
        foreach (var unit in _units)
        {
            DrawMarks(unit, art);
        }
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
        if (_load(UnitArt.IconsPath) is { } icons)
        {
            for (var i = 0; i < unit.Icons.Length; i++)
            {
                var icon = unit.Icons[i];
                var at = new Vector2(unit.IconsAt.X + i * UnitPlan.IconStep, unit.IconsAt.Y);
                DrawTextureRectRegion(icons, new Rect2(at, new Vector2(icon.Width, icon.Height)), Region(icon));
            }
        }
        if (unit.ArrowAt is { } arrow && _load(art.Arrow.Path) is { } cursor)
        {
            var size = new Vector2(art.ArrowWidth, art.Arrow.Height);
            var corner = new Vector2(arrow.X - art.ArrowAnchorX, arrow.Y - art.ArrowAnchorY);
            DrawTextureRectRegion(cursor, new Rect2(corner, size), new Rect2(new Vector2(art.Arrow.FrameAt(_clock) * size.X, 0), size));
        }
    }

    /// <summary>ui/manifest.json's recipe: the frame stretched between its ends, then the fill with its bright leading edge.</summary>
    private void DrawBar(UnitBar bar, BarArt art)
    {
        if (_load(UnitArt.BarsPath) is not { } texture)
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
        if (_load(UnitArt.NumbersPath) is not { } texture)
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
}
