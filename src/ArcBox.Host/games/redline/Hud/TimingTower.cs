using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The compact 22-row timing tower (spec §4.8; mockup 15 <c>.t22</c>, ×1.5): position, team bar, code, player name in
/// yellow, gap, and a throttle / status / result chip; AI rows at 55 % opacity, player rows highlighted, ▲▼ movement in
/// Reveal. Rows are keyed by driver code and glide to their new slot over 0.5 s when the order changes.
/// In <see cref="ShowDistance"/> mode (Chequered classification) the chip column shows the distance instead.
/// In qualifying (qualifying spec §6) it is the timing board, POS · DRIVER · LAP · TOTAL: the lap's chip (purple, green or
/// DELETED, at the Result) and then the running total at the right edge, answered ticks during the question.
/// Drawn in one <c>_Draw</c> so 22 rows stay cheap and pixel-exact.
/// </summary>
public partial class TimingTower : Control
{
    public const float Width = 456;
    public const float RowHeight = 32;
    public const float HeadHeight = 34;
    private const float SortSeconds = 0.5f;

    private const float PosWidth = 40;
    private const float ChipWidth = 96;
    private const float ChipHeight = 20;
    private const float GapWidth = 46;
    private const float AuxWidth = 30;
    private const float Pad = 8;
    /// <summary>Qualifying's TOTAL column at the right edge ("12.34", "5.90 · 2/3", "—").</summary>
    private const float TotalWidth = 92;
    /// <summary>Qualifying's LAP chip: narrower than the race's, so TOTAL fits a correct-lap count ("DELETED" still fits).</summary>
    private const float LapChipWidth = 84;

    private readonly Dictionary<string, (float From, float To, double Start)> _y = new(StringComparer.Ordinal);
    private IReadOnlyList<TowerRow> _rows = [];
    private RacePhase _phase;
    private double _time;
    private string _heading = "POS · DRIVER";
    private string _column = "GAP";
    private bool _qualifying;
    private readonly Dictionary<string, int> _distance = new(StringComparer.Ordinal);

    public TimingTower()
    {
        Name = "TimingTower";
        MouseFilter = MouseFilterEnum.Ignore;
        ClipContents = true;
    }

    /// <summary>Chequered classification: distance instead of chips.</summary>
    public bool ShowDistance { get; set; }

    public float FullHeight(int rows) => HeadHeight + (rows * RowHeight);

    public void Show(RedlineHostView view)
    {
        var rows = Tower.Rows(view);
        var animate = _rows.Count > 0 && view.Phase != RacePhase.Grid;
        foreach (var r in rows)
        {
            var target = r.Slot * RowHeight;
            if (animate && _y.TryGetValue(r.Code, out var y))
            {
                var current = Current(y);
                if (!Mathf.IsEqualApprox(y.To, target))
                {
                    _y[r.Code] = (current, target, _time);
                }
            }
            else
            {
                _y[r.Code] = (target, target, _time);
            }
        }
        _rows = rows;
        _phase = view.Phase;
        _qualifying = QualifyingText.IsQualifying(view.Phase);
        _distance.Clear();
        foreach (var d in view.Drivers)
        {
            _distance[d.Code] = d.Distance;
        }
        _column = view.Phase switch
        {
            RacePhase.Throttle => "GAP · STATUS",
            RacePhase.Reading or RacePhase.Question => "GAP · THROTTLE",
            RacePhase.Reveal => "GAP · RESULT",
            RacePhase.Chequered => "GAP · DISTANCE",
            _ => "GAP",
        };
        _heading = view.Phase == RacePhase.Chequered ? "CLASSIFICATION" : "POS · DRIVER";
        CustomMinimumSize = new Vector2(Width, FullHeight(rows.Count));
        Size = CustomMinimumSize;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        _time += delta;
        var gliding = _y.Values.Any(v => !Mathf.IsEqualApprox(v.From, v.To) && _time - v.Start <= SortSeconds + 0.05);
        var asking = _phase is RacePhase.Question or RacePhase.QualifyingQuestion;
        if (gliding || (asking && _rows.Any(r => r.Mark == AnswerMark.Answering && r.Human)))
        {
            QueueRedraw();
        }
    }

    /// <summary>
    /// Qualifying's TOTAL, right-aligned at <paramref name="right"/>: the time in white (dim "—" before a first correct lap),
    /// and a correct-lap count after a deleted lap ("5.90 · 2/3") smaller and dimmer after it.
    /// </summary>
    private void DrawTotal(string text, float right, float mid, float alpha)
    {
        if (text == Tower.NoTime)
        {
            HudStyle.DrawRight(this, HudStyle.Bold, text, right, mid, 18, HudStyle.Faded(HudStyle.Dim, alpha));
            return;
        }
        var parts = text.Split(" · ", 2);
        if (parts.Length == 2)
        {
            var count = $"· {parts[1]}";
            HudStyle.DrawRight(this, HudStyle.SemiBold, count, right, mid + 1, 14, HudStyle.Faded(HudStyle.Dim, alpha));
            right -= HudStyle.Width(HudStyle.SemiBold, count, 14) + 5;
        }
        HudStyle.DrawRight(this, HudStyle.Bold, parts[0], right, mid, 18, HudStyle.Faded(Colors.White, alpha));
    }

    /// <summary>Qualifying's LAP (chip) and TOTAL columns, in tower x.</summary>
    private static (Rect2 Lap, Rect2 Total) QualifyingColumns(float w)
    {
        var right = w - Pad;
        var total = new Rect2(right - TotalWidth, 0, TotalWidth, RowHeight);
        var lap = new Rect2(total.Position.X - 8 - LapChipWidth, 0, LapChipWidth, RowHeight);
        return (lap, total);
    }

    private float Current((float From, float To, double Start) y)
    {
        var t = (float)Mathf.Clamp((_time - y.Start) / SortSeconds, 0, 1);
        var e = t < 0.5f ? 4 * t * t * t : 1 - (Mathf.Pow((-2 * t) + 2, 3) / 2);
        return Mathf.Lerp(y.From, y.To, e);
    }

    public override void _Draw()
    {
        var w = Size.X;
        HudStyle.DrawPanel(this, new Rect2(0, 0, w, Size.Y));

        // Heading row (.th).
        var head = ArcTheme.Spaced("Bold", 3);
        HudStyle.DrawLeft(this, head, _heading, 14, HeadHeight / 2, 15, HudStyle.Dim);
        if (_qualifying)
        {
            var (lap, _) = QualifyingColumns(w);
            HudStyle.DrawCentered(this, head, "LAP", new Rect2(lap.Position.X, 0, lap.Size.X, HeadHeight), 15, HudStyle.Dim);
            HudStyle.DrawRight(this, head, "TOTAL", w - 12, HeadHeight / 2, 15, HudStyle.Dim);
        }
        else
        {
            HudStyle.DrawRight(this, head, _column, w - 12, HeadHeight / 2, 15, HudStyle.Dim);
        }
        DrawLine(new Vector2(0, HeadHeight - 0.5f), new Vector2(w, HeadHeight - 0.5f), HudStyle.Edge, 1);

        // AI rows first so a gliding player row draws over them.
        foreach (var row in _rows.OrderBy(r => r.Human))
        {
            DrawRow(row, HeadHeight + Current(_y[row.Code]), w);
        }
    }

    private void DrawRow(TowerRow row, float top, float w)
    {
        var alpha = row.Human ? 1f : HudStyle.AiAlpha;
        var mid = top + (RowHeight / 2);
        // Qualifying: P1 is lit only once it has a time (before lap 1's result the order is just the seeded one).
        var leader = row.Position == 1 && _phase is not (RacePhase.DriverSelect or RacePhase.Grid)
                     && !(_qualifying && row.Gap == Tower.NoTime);

        if (row.Human)
        {
            DrawRect(new Rect2(0, top, w, RowHeight), new Color(1, 1, 1, 0.1f));
        }
        DrawLine(new Vector2(0, top + RowHeight - 0.5f), new Vector2(w, top + RowHeight - 0.5f), new Color(1, 1, 1, 0.05f), 1);

        // Position box (white for the leader).
        var posRect = new Rect2(0, top, PosWidth, RowHeight);
        DrawRect(posRect, leader ? Colors.White : new Color(1, 1, 1, 0.05f * alpha));
        HudStyle.DrawCentered(this, HudStyle.Black, row.Position.ToString(), posRect, 18,
            leader ? HudStyle.Dark : HudStyle.Faded(Colors.White, alpha));

        // Team bar.
        var accent = HudStyle.Hex(row.Accent);
        DrawRect(new Rect2(PosWidth + Pad, top + (RowHeight * 0.2f), 5, RowHeight * 0.6f), HudStyle.Faded(accent, alpha));

        // Code and player name.
        var x = PosWidth + Pad + 5 + Pad;
        var codeColour = row.Human ? Colors.White : HudStyle.Faded(new Color("c9cad3"), alpha);
        HudStyle.DrawLeft(this, HudStyle.Bold, row.Code, x, mid, 20, codeColour);
        x += HudStyle.Width(HudStyle.Bold, row.Code, 20) + 7;

        var right = w - Pad;
        var chipLeft = right - ChipWidth;
        var gapRight = chipLeft - 10;
        var auxLeft = gapRight - GapWidth - AuxWidth;
        if (_qualifying)
        {
            // POS · DRIVER · LAP · TOTAL: the chip column moves left of the total, the mark sits just before the chip.
            chipLeft = QualifyingColumns(w).Lap.Position.X;
            auxLeft = chipLeft - 4 - AuxWidth;
        }

        if (row.PlayerName is { } name)
        {
            var font = ArcTheme.Spaced("Black", 1);
            var colour = row.Connected ? HudStyle.Player : HudStyle.Faded(HudStyle.Player, 0.45f);
            HudStyle.DrawLeft(this, font, HudStyle.Fit(name, font, 15, auxLeft - x - 2), x, mid + 1, 15, colour);
        }

        // Movement (Reveal) or the answer mark (Question).
        var auxCentre = new Vector2(auxLeft + (AuxWidth / 2), mid);
        if (row.Movement != 0)
        {
            var up = row.Movement > 0;
            var colour = up ? HudStyle.Good : HudStyle.Bad;
            HudStyle.DrawTriangle(this, auxCentre + new Vector2(-6, 0), 9, up, HudStyle.Faded(colour, alpha));
            HudStyle.DrawLeft(this, HudStyle.Black, Math.Abs(row.Movement).ToString(), auxCentre.X + 1, mid, 14, HudStyle.Faded(colour, alpha));
        }
        else if (row.Mark == AnswerMark.Answered && row.Human)
        {
            HudStyle.DrawCheck(this, auxCentre, 13, HudStyle.Good, 2.5f);
        }
        else if (row.Mark == AnswerMark.Answering && row.Human)
        {
            for (var i = -1; i <= 1; i++)
            {
                var pulse = 0.35f + (0.65f * (0.5f + (0.5f * Mathf.Sin((float)(_time * 5) - i))));
                DrawCircle(auxCentre + new Vector2(i * 6, 0), 2, HudStyle.Faded(HudStyle.Dim, pulse));
            }
        }

        // Gap, or qualifying's total at the right edge.
        if (_qualifying)
        {
            DrawTotal(row.Gap, right - 4, mid, alpha);
        }
        else if (row.Gap.Length > 0)
        {
            HudStyle.DrawRight(this, HudStyle.SemiBold, row.Gap, gapRight, mid, 16, HudStyle.Faded(HudStyle.Dim, row.Human ? 1 : 0.85f));
        }

        // Chip, or the distance in the classification.
        if (ShowDistance)
        {
            var d = _distance.TryGetValue(row.Code, out var dist) ? dist : 0;
            HudStyle.DrawRight(this, HudStyle.Black, d.ToString(), right - 6, mid, 20, HudStyle.Faded(Colors.White, alpha));
        }
        else if (row.Chip is { } chip)
        {
            // Qualifying's lap times are the figure the room watches for: a size up on the race's chips.
            // Qualifying's lap chips stay at full strength on AI rows too: an AI's purple lap must read as purple.
            var chipWidth = _qualifying ? LapChipWidth : ChipWidth;
            HudStyle.DrawChip(this, new Rect2(chipLeft, mid - (ChipHeight / 2), chipWidth, ChipHeight), chip, _qualifying ? 14 : 12,
                row.Human || _qualifying ? 1 : HudStyle.AiAlpha);
        }
    }
}
