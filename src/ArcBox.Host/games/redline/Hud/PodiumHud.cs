using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Ceremony;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The podium HUD (podium spec §5): over each step a label with the place, the player's name, the car code and number in
/// the team colour, and the bottle's live pressure gauge; along the bottom the "NATIONAL ANTHEM · {Country}" caption
/// with "for {Name} · {Driver}, {Team}" during the Anthem, and the "CHAMPAGNE!" banner during the Champagne. Wording
/// comes from <see cref="PodiumText"/>; the labels follow the steps through the live camera every frame.
/// </summary>
public partial class PodiumHud : Control
{
    private const float LabelWidth = 262;
    private const float LabelHeight = 70;
    private const float GaugeHeight = 26;
    private const float CaptionHeight = 168;
    /// <summary>The label row's bottom during the champagne, under the brand slab's line.</summary>
    private const float ChampagneRowBottom = 102 + LabelHeight + GaugeHeight;

    private static readonly Color[] PlaceColours = [new("d4a52c"), new("c3c8cf"), new("b8733a")];

    private PodiumCaption? _caption;
    private IReadOnlyList<PodiumLabel> _labels = [];
    private readonly Dictionary<int, Vector2?> _anchors = [];
    private readonly Dictionary<int, double> _shown = [];
    private RacePhase _phase;
    private double _time;
    private double _phaseStart;

    public PodiumHud()
    {
        Name = "PodiumHud";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>Panels in canvas pixels (for the car name tags, which are off during the ceremony anyway).</summary>
    public Rect2 CaptionRect => new(0, Size.Y - CaptionHeight, Mathf.Min(Size.X, 1240), CaptionHeight);

    public void Show(RedlineHostView view)
    {
        if (view.Phase != _phase)
        {
            _phase = view.Phase;
            _phaseStart = _time;
        }
        _caption = PodiumText.Caption(view);
        _labels = PodiumText.Labels(view);
        QueueRedraw();
    }

    /// <summary>Projects each step's label anchor through <paramref name="camera"/>.</summary>
    public void Track(Camera3D camera, PodiumLayout layout)
    {
        _anchors.Clear();
        foreach (var label in _labels)
        {
            var a = layout.World(PodiumLayout.NameAnchor(label.Step));
            var p = new Vector3(a.X, a.Y, a.Z);
            _anchors[label.Step] = camera.IsPositionBehind(p) ? null : camera.UnprojectPosition(p);
        }
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        // The gauges ease towards the latest charge, so a jump of 0.05 reads as a fill, not a flicker.
        foreach (var label in _labels)
        {
            var now = _shown.GetValueOrDefault(label.Step, label.Charge);
            _shown[label.Step] = now + ((label.Charge - now) * Math.Min(1, delta * 8));
        }
        if (Visible)
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        // One row, each label as near its driver as the others allow: just above the tallest head during the anthem,
        // up at the top of the picture during the champagne so the sprays stay in the clear.
        var shown = _labels.Where(l => _anchors.GetValueOrDefault(l.Step) is not null).ToList();
        if (shown.Count > 0)
        {
            var anchors = shown.Select(l => _anchors[l.Step]!.Value).ToList();
            var xs = PodiumLabelLayout.Spread([.. anchors.Select(a => (double)a.X)], LabelWidth, 14, 0, Size.X);
            var bottom = _phase == RacePhase.Champagne
                ? Mathf.Min(ChampagneRowBottom, anchors.Min(a => a.Y) - 26)
                : anchors.Min(a => a.Y) - 26;
            for (var i = 0; i < shown.Count; i++)
            {
                DrawLabel(shown[i], anchors[i], (float)xs[i], bottom);
            }
        }
        if (_caption is { } caption)
        {
            if (_phase == RacePhase.Champagne)
            {
                DrawBanner(caption);
            }
            else
            {
                DrawCaption(caption);
            }
        }
    }

    private float Intro(double delay = 0, double seconds = 0.45) =>
        (float)global::Redline.Presentation.RevealMotion.SmoothStep((_time - _phaseStart - delay) / seconds);

    /// <param name="anchor">Above the driver's head, on screen.</param>
    /// <param name="centreX">Where the label sits (spread so labels never overlap).</param>
    /// <param name="bottom">The bottom of the label row.</param>
    private void DrawLabel(PodiumLabel label, Vector2 anchor, float centreX, float bottom)
    {
        var k = Intro(0.2 + (0.12 * label.Step));
        var r = new Rect2(centreX - (LabelWidth / 2), bottom - LabelHeight - GaugeHeight + ((1 - k) * 20), LabelWidth, LabelHeight);
        var alpha = k;
        var accent = HudStyle.Hex(label.Accent);
        DrawLine(new Vector2(centreX, r.End.Y + GaugeHeight), anchor + new Vector2(0, -2), new Color(1, 1, 1, 0.55f * alpha), 1.5f);
        DrawCircle(anchor + new Vector2(0, -2), 3.5f, new Color(accent, alpha));

        // Place box, then the name and the car.
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y + GaugeHeight)), new Color(14 / 255f, 14 / 255f, 20 / 255f, 0.9f * alpha));
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X, r.Size.Y + GaugeHeight)), new Color(1, 1, 1, 0.16f * alpha), false, 1);
        var place = new Rect2(r.Position, new Vector2(64, r.Size.Y));
        DrawRect(place, new Color(PlaceColours[label.Step - 1], alpha));
        HudStyle.DrawCentered(this, HudStyle.BlackItalic, PodiumText.Place(label.Step), place, 34, new Color(HudStyle.Dark, alpha));
        DrawRect(new Rect2(place.End.X, r.Position.Y, 5, r.Size.Y), new Color(accent, alpha));

        var x = place.End.X + 16;
        var nameSize = HudStyle.FitSize(label.Name, HudStyle.Black, 32, 18, r.End.X - x - 10);
        HudStyle.DrawLeft(this, HudStyle.Black, label.Name, x, r.Position.Y + 25, nameSize, new Color(HudStyle.Player, alpha));
        var car = $"{label.Number}  {label.Code}  ·  {HudText.Seat(label.Code)?.Team.Name.ToUpperInvariant() ?? ""}";
        HudStyle.DrawLeft(this, ArcTheme.Spaced("Bold", 2), HudStyle.Fit(car, ArcTheme.Spaced("Bold", 2), 17, r.End.X - x - 10), x, r.Position.Y + 53, 17,
            new Color(0.82f, 0.82f, 0.88f, alpha));

        // The pressure gauge: a segmented bar that turns amber then red as the bottle fills, and the spray count.
        var charge = (float)_shown.GetValueOrDefault(label.Step, label.Charge);
        var g = new Rect2(r.Position.X + 10, r.End.Y + 5, r.Size.X - 20, GaugeHeight - 12);
        var bar = new Rect2(g.Position.X + 92, g.Position.Y, g.Size.X - 92 - 40, g.Size.Y);
        HudStyle.DrawLeft(this, ArcTheme.Spaced("Black", 2), "PRESSURE", g.Position.X, g.GetCenter().Y, 13, new Color(HudStyle.Dim, alpha));
        DrawRect(bar, new Color(1, 1, 1, 0.1f * alpha));
        const int segments = 20;
        var segW = bar.Size.X / segments;
        var lit = (int)Math.Round(charge * segments);
        for (var i = 0; i < lit; i++)
        {
            var c = HudStyle.Hex(PodiumText.GaugeColour((i + 1) / (double)segments));
            DrawRect(new Rect2(bar.Position.X + (i * segW) + 1, bar.Position.Y, segW - 2, bar.Size.Y), new Color(c, alpha));
        }
        if (label.Sprays > 0)
        {
            HudStyle.DrawRight(this, HudStyle.BlackItalic, $"×{label.Sprays}", g.End.X, g.GetCenter().Y, 17, new Color(HudStyle.Player, alpha));
        }
    }

    /// <summary>The Anthem lower-third.</summary>
    private void DrawCaption(PodiumCaption caption)
    {
        var k = Intro(0.6);
        var rect = CaptionRect;
        var title = HudStyle.FitSize(caption.Title, HudStyle.BlackItalic, 66, 40, rect.Size.X - 80);
        var width = Mathf.Min(rect.Size.X, HudStyle.Width(HudStyle.BlackItalic, caption.Title, title) + 90);
        var r = new Rect2(rect.Position.X - ((1 - k) * 60), rect.Position.Y, width, rect.Size.Y);
        DrawRect(r, new Color(14 / 255f, 14 / 255f, 20 / 255f, 0.9f * k));
        DrawRect(new Rect2(r.Position, new Vector2(8, r.Size.Y)), new Color(HudStyle.Red, k));
        HudStyle.DrawLeft(this, ArcTheme.Spaced("Bold", 4), caption.Kicker, r.Position.X + 38, r.Position.Y + 34, 19, new Color(HudStyle.Dim, k));
        // The black italic's middle dot reads as a dash at this size: draw a round one between the parts.
        var x = r.Position.X + 36;
        var parts = caption.Title.Split(" · ");
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
            {
                DrawCircle(new Vector2(x + (title * 0.36f), r.Position.Y + 86), title * 0.09f, new Color(HudStyle.Red, k));
                x += title * 0.72f;
            }
            HudStyle.DrawLeft(this, HudStyle.BlackItalic, parts[i], x, r.Position.Y + 86, title, new Color(1, 1, 1, k));
            x += HudStyle.Width(HudStyle.BlackItalic, parts[i], title);
        }
        HudStyle.DrawLeft(this, HudStyle.SemiBold, caption.Subtitle, r.Position.X + 40, r.Position.Y + 137, 30, new Color(0.85f, 0.85f, 0.9f, k));
    }

    /// <summary>The Champagne banner: a red slab that punches in, gold-edged, with the line under it.</summary>
    private void DrawBanner(PodiumCaption caption)
    {
        var t = _time - _phaseStart;
        var k = Intro(0.15, 0.3);
        var punch = 1 + (0.12f * (float)Math.Max(0, Math.Sin(Math.Min(1, t / 0.5) * Math.PI)));
        const int size = 104;
        var textW = HudStyle.Width(HudStyle.BlackItalic, caption.Title, size);
        var w = (textW + 120) * punch;
        var h = 150 * punch;
        var x = 0f;
        var y = Size.Y - CaptionHeight + ((CaptionHeight - h) / 2) - 10;
        const float slant = 30;
        DrawColoredPolygon([new(x, y), new(x + w, y), new(x + w - slant, y + h), new(x, y + h)], new Color(HudStyle.Red, k));
        DrawRect(new Rect2(x, y + h, w - slant, 6), new Color(new Color("ffd400"), k));
        var fontSize = (int)(size * punch);
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, caption.Title, x + (44 * punch), y + (h / 2), fontSize, new Color(1, 1, 1, k));
        HudStyle.DrawLeft(this, HudStyle.SemiBold, caption.Subtitle, x + w + 26, y + (h / 2) + 4, 30, new Color(1, 1, 1, 0.92f * k));
    }
}
