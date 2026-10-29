using ArcBox.Host.Shell;
using Godot;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// Qualifying's lower-thirds (qualifying spec §6), in the Reveal lower-third's place and style: at a lap's Result when the
/// leader changed, [QUALIFYING / PROVISIONAL POLE] on red and [team / NAME] with the team bar, for 3 s; after the last lap,
/// [QUALIFYING / POLE POSITION] on red, [team / NAME] behind a wide team-colour bar and [TOTAL / 6.82s] on white, for the
/// rest of the Result. The wording comes from <c>QualifyingText</c>; this only draws.
/// </summary>
public partial class PoleCard : Control
{
    public const float Height = RoundResult.Height;
    /// <summary>How long the provisional pole card stays up.</summary>
    public const double ProvisionalSeconds = 3;
    private const float SlideSeconds = 0.3f;

    private sealed record Card(bool Pole, string Name, string Team, Color Accent, string? Total);

    private Card? _card;
    private double _shownAt = double.NaN;
    private double _hideAt = double.NaN;
    private double _time;

    public PoleCard()
    {
        Name = "PoleCard";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>"PROVISIONAL POLE · <paramref name="name"/>" from <paramref name="inSeconds"/> for <see cref="ProvisionalSeconds"/>.</summary>
    public void PlayProvisional(string name, string team, Color accent, double inSeconds) =>
        Play(new Card(false, name, team, accent, null), inSeconds, inSeconds + ProvisionalSeconds);

    /// <summary>"POLE POSITION" for <paramref name="name"/> with the team bar and the total, from <paramref name="inSeconds"/> to <paramref name="outSeconds"/>.</summary>
    public void PlayPole(string name, string team, Color accent, string total, double inSeconds, double outSeconds) =>
        Play(new Card(true, name, team, accent, total), inSeconds, outSeconds);

    public void Stop()
    {
        _card = null;
        QueueRedraw();
    }

    /// <summary>The card's current on-screen rect (empty while hidden), for name-tag culling.</summary>
    public Rect2 Footprint => _card is not null && Visible && Visibility() > 0.01f ? GetGlobalRect() : default;

    private void Play(Card card, double inSeconds, double outSeconds)
    {
        _card = card;
        _shownAt = _time + inSeconds;
        _hideAt = _time + outSeconds;
        QueueRedraw();
    }

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
        var tIn = (float)Mathf.Clamp((_time - _shownAt) / SlideSeconds, 0, 1);
        var tOut = (float)Mathf.Clamp((_time - _hideAt) / SlideSeconds, 0, 1);
        return Mathf.Min(1 - Mathf.Pow(1 - tIn, 3), 1 - tOut);
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
        var kicker = ArcTheme.Spaced("Bold", 3);
        Color A(Color col) => HudStyle.Faded(col, v);

        // [QUALIFYING / PROVISIONAL POLE | POLE POSITION] on brand red.
        var title = c.Pole ? "POLE POSITION" : "PROVISIONAL POLE";
        var a = new Rect2(dx, 0, HudStyle.Width(HudStyle.BlackItalic, title, 46) + 64, h);
        DrawRect(a, A(HudStyle.Red));
        HudStyle.DrawLeft(this, kicker, "QUALIFYING", a.Position.X + 30, 34, 18, A(new Color(1, 1, 1, 0.8f)));
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, title, a.Position.X + 28, 78, 46, A(Colors.White));

        // [team / NAME] behind the team-colour bar (wide on the pole card).
        var bar = c.Pole ? 14f : 7f;
        var maxName = Size.X - a.Size.X - (c.Pole ? 260 : 40);
        var nameSize = HudStyle.FitSize(c.Name, HudStyle.BlackItalic, 56, 30, Mathf.Max(120, maxName - bar - 64));
        var nameW = Mathf.Max(HudStyle.Width(HudStyle.BlackItalic, c.Name, nameSize), HudStyle.Width(kicker, c.Team, 18)) + bar + 60;
        var b = new Rect2(a.End.X, 0, nameW, h);
        DrawRect(b, A(new Color("111118")));
        DrawRect(new Rect2(b.Position, new Vector2(bar, h)), A(c.Accent));
        HudStyle.DrawLeft(this, kicker, c.Team, b.Position.X + bar + 26, 34, 18, A(HudStyle.Dim));
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, c.Name, b.Position.X + bar + 24, 78, nameSize, A(Colors.White));

        // [TOTAL / 6.82s] on white.
        if (c.Total is { } total)
        {
            var value = $"{total}s";
            var t = new Rect2(b.End.X, 0, Mathf.Max(HudStyle.Width(HudStyle.Black, value, 46), HudStyle.Width(kicker, "TOTAL", 18)) + 56, h);
            DrawRect(t, A(Colors.White));
            HudStyle.DrawLeft(this, kicker, "TOTAL", t.Position.X + 28, 34, 18, A(new Color("444450")));
            HudStyle.DrawLeft(this, HudStyle.Black, value, t.Position.X + 26, 78, 46, A(HudStyle.Dark));
        }
    }
}
