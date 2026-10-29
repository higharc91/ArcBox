using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The header strip (broadcast.css <c>.head</c>): the REDLINE slab, the question panel (meta line with difficulty pips and
/// the big title / question) and the clock box (time + bar, "LISTEN" + bar while the question is read out, or the five
/// start lights during Grid).
/// </summary>
public partial class QuestionPanel : HBoxContainer
{
    public const float ClockWidth = 180;
    private const int TitleMax = 44;
    private static readonly int[] TwoLineSizes = [30, 28, 26];

    private readonly HBoxContainer _meta;
    private readonly DifficultyPips _pips = new();
    private readonly Label _difficulty;
    private readonly HBoxContainer _diffBox;
    private readonly Label _title;
    private readonly Label _caption;
    private readonly Label _time;
    private readonly Control _bar;
    private readonly ColorRect _barFill;
    private readonly StartLightsView _lights = new();
    private string _titleText = "";
    private string? _clockText;

    public QuestionPanel()
    {
        Name = "QuestionPanel";
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", (int)HudStyle.Gap);
        CustomMinimumSize = new Vector2(0, HudStyle.HeaderHeight);

        var slab = new BrandSlab("REDLINE", 46) { CustomMinimumSize = new Vector2(0, HudStyle.HeaderHeight) };
        AddChild(slab);

        _meta = ArcTheme.HBox(20);
        _difficulty = ArcTheme.Label("", MetaFont, 19, Colors.White);
        _diffBox = ArcTheme.HBox(8, _difficulty, _pips);
        _title = ArcTheme.Label("", TitleFont(TitleMax), TitleMax);
        _title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _title.MaxLinesVisible = 2;
        _title.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _title.Resized += FitTitle;
        var text = ArcTheme.VBox(0, _meta, _title);
        text.Alignment = BoxContainer.AlignmentMode.Center;
        var panel = ArcTheme.Panel(HudStyle.Panel(HudStyle.Over3D, HudStyle.Red, 5, 24, 6), text);
        panel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        AddChild(panel);

        _caption = ArcTheme.Label("TIME", ArcTheme.Display("Bold", 3, 4, 3), 17, HudStyle.Dim);
        _caption.HorizontalAlignment = HorizontalAlignment.Center;
        _time = ArcTheme.Label("", ArcTheme.Display("Black", 0, 20, 17), 58);
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        (_bar, _barFill) = ShellLayout.Bar(6);
        _bar.CustomMinimumSize = new Vector2(ClockWidth * 0.8f, 6);
        _bar.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        _lights.Visible = false;
        _lights.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
        var clock = ArcTheme.VBox(4, _caption, _time, _lights, _bar);
        clock.Alignment = BoxContainer.AlignmentMode.Center;
        var clockPanel = ArcTheme.Panel(HudStyle.Panel(HudStyle.Over3D, null, 0, 10, 6), clock);
        clockPanel.CustomMinimumSize = new Vector2(ClockWidth, 0);
        AddChild(clockPanel);
    }

    public void Show(HeaderText header, bool lights)
    {
        foreach (var c in _meta.GetChildren())
        {
            _meta.RemoveChild(c);
            if (c != _diffBox)
            {
                c.QueueFree();
            }
        }
        foreach (var m in header.Meta)
        {
            _meta.AddChild(ArcTheme.Label(m, MetaFont, 19, HudStyle.Dim));
        }
        if (header.Difficulty is { } d)
        {
            _difficulty.Text = d;
            _pips.Lit = header.Pips;
            _meta.AddChild(_diffBox);
        }

        _titleText = header.Title;
        _title.Text = header.Title;
        FitTitle();

        _caption.Text = header.ClockCaption;
        // Reading: "LISTEN" in place of the time (the bar still runs down the reading time).
        _clockText = header.ClockText;
        _time.AddThemeFontSizeOverride("font_size", _clockText is null ? 58 : 36);
        _lights.Visible = lights;
        _time.Visible = !lights;
        _bar.Visible = !lights;
    }

    /// <summary>The clock: <paramref name="remaining"/> seconds (null = no deadline) and the bar fraction.</summary>
    public void SetClock(double? remaining, double fraction)
    {
        _time.Text = _clockText ?? (remaining is { } r ? HudClock.Format(r) : "–");
        ShellLayout.SetBar(_barFill, (float)fraction);
    }

    public void SetLights(int lit) => _lights.Lit = lit;

    /// <summary>The small caps meta line, its line box trimmed to the caps.</summary>
    private static Font MetaFont => ArcTheme.Display("Bold", 3, 5, 3);

    /// <summary>Bold with the line box trimmed to about cap height plus descenders (Titillium's line gap is 1.52 em).</summary>
    private static Font TitleFont(int size) => ArcTheme.Display("Bold", 0, (int)Math.Round(size * 0.28), (int)Math.Round(size * 0.17));

    /// <summary>One line at 44 px when it fits, else up to two lines at 30 (down to 26 for very long questions).</summary>
    private void FitTitle()
    {
        var width = _title.Size.X;
        if (width <= 0 || _titleText.Length == 0)
        {
            return;
        }
        var size = TwoLineSizes[^1];
        if (HudStyle.Width(TitleFont(TitleMax), _titleText, TitleMax) <= width)
        {
            size = TitleMax;
        }
        else
        {
            foreach (var s in TwoLineSizes)
            {
                var font = TitleFont(s);
                var lines = font.GetMultilineStringSize(_titleText, HorizontalAlignment.Left, width, s, -1,
                    TextServer.LineBreakFlag.WordBound | TextServer.LineBreakFlag.Mandatory).Y / font.GetHeight(s);
                if (lines <= 2.2f)
                {
                    size = s;
                    break;
                }
            }
        }
        _title.AddThemeFontOverride("font", TitleFont(size));
        _title.AddThemeFontSizeOverride("font_size", size);
    }
}

/// <summary>Three skewed bars, <see cref="Lit"/> of them white (broadcast.css <c>.diff i</c>).</summary>
public partial class DifficultyPips : Control
{
    private int _lit;

    public DifficultyPips()
    {
        CustomMinimumSize = new Vector2(76, 22);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public int Lit
    {
        get => _lit;
        set
        {
            _lit = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var y = (Size.Y / 2) - 3.5f;
        for (var i = 0; i < 3; i++)
        {
            var x = i * 25f;
            var c = i < _lit ? Colors.White : new Color(1, 1, 1, 0.2f);
            DrawColoredPolygon([new(x + 3, y), new(x + 24, y), new(x + 21, y + 7.5f), new(x, y + 7.5f)], c);
        }
    }
}

/// <summary>Five start lights mirroring the gantry during Grid.</summary>
public partial class StartLightsView : Control
{
    private int _lit;

    public StartLightsView()
    {
        CustomMinimumSize = new Vector2(150, 34);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public int Lit
    {
        get => _lit;
        set
        {
            if (_lit != value)
            {
                _lit = value;
                QueueRedraw();
            }
        }
    }

    public override void _Draw()
    {
        for (var i = 0; i < StartLights.Count; i++)
        {
            var c = new Vector2(15 + (i * 30), Size.Y / 2);
            if (i < _lit)
            {
                DrawCircle(c, 14, new Color(HudStyle.Red, 0.35f));
                DrawCircle(c, 11, new Color("ff1a12"));
            }
            else
            {
                DrawCircle(c, 11, new Color("2a1414"));
            }
        }
    }
}

/// <summary>
/// Answers A–D along the bottom (broadcast.css <c>.answers</c>). In Reveal the correct one turns green, wrong ones dim,
/// and each shows the team-colour pips of who picked it: tall pips for players, thin ones for AI. A qualifying Result
/// lights the correct answer the same way, without pips.
/// </summary>
public partial class AnswerRow : Control
{
    private const float Spacing = 15;
    private const float KeyWidth = 78;
    private IReadOnlyList<string> _answers = [];
    private int? _correct;
    private readonly List<List<(Color Colour, bool Human)>> _pickers = [];

    public AnswerRow()
    {
        Name = "AnswerRow";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(RedlineHostView view)
    {
        _answers = view.Question?.Answers ?? [];
        // Qualifying's Result shows the answer too (no pickers: the host view never says who picked what there).
        _correct = view.Phase is RacePhase.Reveal or RacePhase.QualifyingResult ? view.CorrectIndex : null;
        _pickers.Clear();
        for (var i = 0; i < _answers.Count; i++)
        {
            _pickers.Add([.. view.Drivers
                .Where(d => _correct is not null && d.AnswerIndex == i)
                .OrderBy(d => d.Player is null)
                .Select(d => (HudStyle.Hex(TeamAccent.ById(d.TeamId)), d.Player is not null))]);
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        var n = _answers.Count;
        if (n == 0)
        {
            return;
        }
        var w = (Size.X - (Spacing * (n - 1))) / n;
        for (var i = 0; i < n; i++)
        {
            var r = new Rect2(i * (w + Spacing), 0, w, Size.Y);
            var right = _correct == i;
            var wrong = _correct is not null && !right;
            var alpha = wrong ? 0.4f : 1f;

            DrawRect(r, right ? HudStyle.Good : HudStyle.Faded(HudStyle.Over3D, wrong ? 0.8f : 1));
            DrawRect(r, right ? HudStyle.Good : HudStyle.Edge, false, 1);
            var key = new Rect2(r.Position, new Vector2(KeyWidth, r.Size.Y));
            DrawRect(key, right ? new Color(0, 0, 0, 0.15f) : new Color(1, 1, 1, 0.08f * alpha));
            var ink = right ? HudStyle.GoodInk : HudStyle.Faded(Colors.White, alpha);
            HudStyle.DrawCentered(this, HudStyle.Black, ((char)('A' + i)).ToString(), key, 32, ink);

            // Pickers at the right edge: players as tall pips, AI as thin ones, squeezed to at most a third of the box.
            var pickers = i < _pickers.Count ? _pickers[i] : [];
            var natural = pickers.Sum(p => p.Human ? 16f : 7f);
            var squeeze = Mathf.Min(1, (w * 0.33f) / Mathf.Max(1, natural));
            var px = r.End.X - 14;
            var pipsLeft = px;
            foreach (var (colour, human) in pickers.AsEnumerable().Reverse())
            {
                var pw = Mathf.Max(2, (human ? 11f : 4f) * squeeze);
                var ph = human ? 33f : 20f;
                px -= pw;
                var pip = new Rect2(px, r.GetCenter().Y - (ph / 2), pw, ph);
                DrawRect(pip, human ? colour : HudStyle.Faded(colour, 0.7f));
                if (right && human)
                {
                    DrawRect(pip, new Color(0, 0, 0, 0.35f), false, 1);
                }
                px -= Mathf.Max(1, (human ? 5 : 3) * squeeze);
                pipsLeft = px;
            }

            // The answer: one line down to 24 px, else two lines at 22 (the second trimmed if it must be).
            var textLeft = key.End.X + 22;
            var maxW = (pickers.Count > 0 ? pipsLeft - 10 : r.End.X - 16) - textLeft;
            var font = HudStyle.Bold;
            var size = HudStyle.FitSize(_answers[i], font, 31, 24, maxW);
            if (HudStyle.Width(font, _answers[i], size) <= maxW)
            {
                HudStyle.DrawLeft(this, font, _answers[i], textLeft, r.GetCenter().Y, size, ink);
            }
            else
            {
                var (first, second) = HudStyle.TwoLines(_answers[i], font, 22, maxW);
                HudStyle.DrawLeft(this, font, first, textLeft, r.GetCenter().Y - 13, 22, ink);
                HudStyle.DrawLeft(this, font, second, textLeft, r.GetCenter().Y + 14, 22, ink);
            }
        }
    }
}
