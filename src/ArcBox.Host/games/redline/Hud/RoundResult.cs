using ArcBox.Host.Shell;
using Godot;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The Reveal lower-third (mockup 10): [kicker + surname] [caption + gain on red] [POSITION P5 → P1 ▲4 on white], for the
/// camera's subject. It slides in shortly after the cut and out when the camera leaves for the follow-up shot.
/// </summary>
public partial class RoundResult : Control
{
    public const float Height = 116;
    private const float SlideSeconds = 0.3f;

    private RoundResultCard? _card;
    private double _shownAt = double.NaN;
    private double _hideAt = double.NaN;
    private double _time;

    public RoundResult()
    {
        Name = "RoundResult";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Shows <paramref name="card"/> from <paramref name="inSeconds"/> to <paramref name="outSeconds"/> from now.</summary>
    public void Play(RoundResultCard? card, double inSeconds, double outSeconds)
    {
        _card = card;
        _shownAt = _time + inSeconds;
        _hideAt = _time + outSeconds;
        QueueRedraw();
    }

    public void Stop()
    {
        _card = null;
        QueueRedraw();
    }

    /// <summary>The panel's current on-screen rect (empty while hidden), for name-tag culling.</summary>
    public Rect2 Footprint => _card is not null && Visible && Visibility() > 0.01f ? GetGlobalRect() : default;

    public override void _Process(double delta)
    {
        _time += delta;
        if (_card is not null)
        {
            QueueRedraw();
        }
    }

    private float Visibility()
    {
        if (_card is null || double.IsNaN(_shownAt))
        {
            return 0;
        }
        var t_in = (float)Mathf.Clamp((_time - _shownAt) / SlideSeconds, 0, 1);
        var t_out = (float)Mathf.Clamp((_time - _hideAt) / SlideSeconds, 0, 1);
        return Mathf.Min(1 - Mathf.Pow(1 - t_in, 3), 1 - t_out);
    }

    public override void _Draw()
    {
        var v = Visibility();
        if (_card is not { } c || v <= 0)
        {
            return;
        }
        var dx = (1 - v) * -40;
        var h = Size.Y;
        var kickerFont = ArcTheme.Spaced("Bold", 3);
        var alpha = v;
        Color A(Color col) => HudStyle.Faded(col, alpha);

        // [accent | kicker / SURNAME]
        var nameW = Mathf.Max(HudStyle.Width(HudStyle.BlackItalic, c.Surname, 56), HudStyle.Width(kickerFont, c.Kicker, 18)) + 64;
        var a = new Rect2(dx, 0, nameW, h);
        DrawRect(a, A(new Color("111118")));
        DrawRect(new Rect2(a.Position, new Vector2(7, h)), A(HudStyle.Hex(c.Accent)));
        HudStyle.DrawLeft(this, kickerFont, c.Kicker, a.Position.X + 34, 34, 18, A(HudStyle.Dim));
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, c.Surname, a.Position.X + 32, 78, 56, A(Colors.White));

        // [CAPTION / +4] on brand red.
        var capW = Mathf.Max(HudStyle.Width(kickerFont, c.Caption, 18), HudStyle.Width(HudStyle.BlackItalic, c.Delta, 54)) + 56;
        var b = new Rect2(a.End.X, 0, capW, h);
        DrawRect(b, A(c.Good ? HudStyle.Red : new Color("a10400")));
        HudStyle.DrawLeft(this, kickerFont, c.Caption, b.Position.X + 28, 34, 18, A(Colors.White));
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, c.Delta, b.Position.X + 26, 78, 54, A(Colors.White));

        // [POSITION / P5 → P1 ▲4] on white.
        var from = $"P{c.From}";
        var to = $"P{c.To}";
        var change = Math.Abs(c.Change).ToString();
        var posFont = HudStyle.Black;
        var rowW = HudStyle.Width(posFont, from, 46) + 64 + HudStyle.Width(posFont, to, 46) + (c.Change != 0 ? 22 + 24 + HudStyle.Width(posFont, change, 40) : 0);
        var p = new Rect2(b.End.X, 0, rowW + 56, h);
        DrawRect(p, A(Colors.White));
        HudStyle.DrawLeft(this, kickerFont, "POSITION", p.Position.X + 28, 34, 18, A(new Color("444450")));
        var x = p.Position.X + 26;
        HudStyle.DrawLeft(this, posFont, from, x, 78, 46, A(HudStyle.Dark));
        x += HudStyle.Width(posFont, from, 46) + 14;
        HudStyle.DrawArrow(this, new Vector2(x, 79), 36, A(HudStyle.Dark), 4);
        x += 50;
        HudStyle.DrawLeft(this, posFont, to, x, 78, 46, A(HudStyle.Dark));
        if (c.Change != 0)
        {
            x += HudStyle.Width(posFont, to, 46) + 22;
            var up = c.Change > 0;
            var col = up ? new Color("13a04a") : HudStyle.Bad;
            HudStyle.DrawTriangle(this, new Vector2(x + 11, 79), 24, up, A(col));
            HudStyle.DrawLeft(this, posFont, change, x + 26, 78, 40, A(col));
        }
    }
}
