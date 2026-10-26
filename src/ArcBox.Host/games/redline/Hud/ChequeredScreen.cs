using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// Chequered flag (mockup 05) over the finish-line camera: chequered band, "ALEX WINS" headline, the podium (2-1-3),
/// the full classification on the right, the awards cards and the question attribution.
/// </summary>
public partial class ChequeredScreen : Control
{
    private const float BandHeight = 18;
    private const float TitleTop = 34;
    private const float TitleHeight = 150;
    private const float AwardsHeight = 110;

    private readonly TimingTower _classification = new() { ShowDistance = true };
    private string _headline = "";
    private string _meta = "";
    private string _winnerLine = "";
    private string _attribution = "";
    private IReadOnlyList<Finisher> _podium = [];
    private IReadOnlyList<AwardCard> _awards = [];

    public ChequeredScreen()
    {
        Name = "ChequeredScreen";
        MouseFilter = MouseFilterEnum.Ignore;
        AddChild(_classification);
        Resized += Place;
    }

    /// <summary>Rects covered by panels, for name-tag culling.</summary>
    public IEnumerable<Rect2> Panels()
    {
        var o = GetGlobalRect().Position;
        yield return new Rect2(o + new Vector2(0, 0), new Vector2(LeftWidth, TitleTop + TitleHeight));
        yield return _classification.GetGlobalRect();
        yield return new Rect2(o + PodiumRect.Position, PodiumRect.Size);
        yield return new Rect2(o + new Vector2(0, Size.Y - AwardsHeight), new Vector2(LeftWidth, AwardsHeight));
        yield return new Rect2(o + new Vector2(Size.X - TimingTower.Width, Size.Y - 44), new Vector2(TimingTower.Width, 44));
    }

    private float LeftWidth => Size.X - TimingTower.Width - HudStyle.Gap;

    private Rect2 PodiumRect => new(0, Size.Y - AwardsHeight - HudStyle.Gap - 400, Mathf.Min(960, LeftWidth), 400);

    public void Show(RedlineHostView view)
    {
        _classification.Show(view);
        _headline = HudText.Headline(view);
        _meta = HudText.Header(view).Meta[0];
        _winnerLine = HudText.WinnerLine(view);
        _attribution = view.Attribution;
        _podium = HudText.Classification(view, 3);
        _awards = HudText.Awards(view);
        Place();
        QueueRedraw();
    }

    private void Place()
    {
        _classification.Position = new Vector2(Size.X - TimingTower.Width, TitleTop + BandHeight + 8);
    }

    public override void _Draw()
    {
        DrawBand();

        // Headline panel.
        var title = new Rect2(0, TitleTop, LeftWidth, TitleHeight);
        DrawRect(title, HudStyle.Faded(HudStyle.Glass, 0.92f));
        DrawRect(new Rect2(title.Position, new Vector2(6, title.Size.Y)), HudStyle.Red);
        var size = HudStyle.FitSize(_headline, HudStyle.BlackItalic, 104, 60, LeftWidth * 0.62f);
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, _headline, 34, TitleTop + 62, size, Colors.White);
        var metaFont = ArcTheme.Spaced("Bold", 3);
        var after = 34 + HudStyle.Width(HudStyle.BlackItalic, _headline, size) + 40;
        HudStyle.DrawLeft(this, metaFont, _meta, after, TitleTop + 84, 20, HudStyle.Dim);
        HudStyle.DrawLeft(this, HudStyle.SemiBold, _winnerLine, 38, TitleTop + 124, 26, new Color("d0d0da"));

        DrawPodium();
        DrawAwards();

        // Attribution under the classification, on its own glass strip so it reads over bright grass.
        var credit = new Rect2(Size.X - TimingTower.Width, Size.Y - 44, TimingTower.Width, 44);
        HudStyle.DrawPanel(this, credit, HudStyle.Faded(HudStyle.Glass, 0.92f));
        HudStyle.DrawLeft(this, HudStyle.SemiBold, HudStyle.Fit(_attribution, HudStyle.SemiBold, 17, credit.Size.X - 28), credit.Position.X + 14,
            credit.GetCenter().Y, 17, new Color("b4b4c0"));
    }

    private void DrawBand()
    {
        const float cell = 9;
        var cols = (int)(Size.X / cell);
        DrawRect(new Rect2(0, 0, Size.X, BandHeight), new Color("1d1d24"));
        for (var c = 0; c < cols; c++)
        {
            for (var r = 0; r < 2; r++)
            {
                if ((c + r) % 2 == 0)
                {
                    DrawRect(new Rect2(c * cell, r * cell, cell, cell), new Color("f2f2f2"));
                }
            }
        }
    }

    private void DrawPodium()
    {
        if (_podium.Count == 0)
        {
            return;
        }
        var panel = PodiumRect;
        HudStyle.DrawPanel(this, panel, HudStyle.Faded(HudStyle.Glass, 0.92f));
        var colW = 270f;
        var gap = 18f;
        var left = panel.Position.X + ((panel.Size.X - ((colW * 3) + (gap * 2))) / 2);
        int[] order = [1, 0, 2];
        float[] heights = [210, 160, 120];
        var leaderDistance = _podium[0].Distance;
        for (var k = 0; k < 3 && k < order.Length; k++)
        {
            var i = order[k];
            if (i >= _podium.Count)
            {
                continue;
            }
            var f = _podium[i];
            var x = left + (k * (colW + gap));
            var blockH = heights[i];
            var block = new Rect2(x, panel.End.Y - blockH, colW, blockH);
            DrawRect(block, new Color(1, 1, 1, 0.06f));
            DrawRect(new Rect2(block.Position, new Vector2(colW, 5)), HudStyle.Hex(f.Accent));
            HudStyle.DrawCentered(this, HudStyle.BlackItalic, f.Position.ToString(), new Rect2(block.Position + new Vector2(0, 10), block.Size - new Vector2(0, 10)),
                i == 0 ? 110 : 90, new Color(1, 1, 1, 0.92f));

            // Name and distance above the block.
            var name = f.PlayerName ?? f.Surname.ToUpperInvariant();
            var nameSize = HudStyle.FitSize(name, HudStyle.Black, 42, 24, colW - 8);
            var nameRect = new Rect2(x, block.Position.Y - 92, colW, 46);
            HudStyle.DrawCentered(this, HudStyle.Black, name, nameRect, nameSize, f.PlayerName is null ? new Color("d8d8e0") : HudStyle.Player);
            var sub = f.PlayerName is null
                ? $"{f.Code} · AI · {f.Distance}"
                : $"{f.Code} · {(i == 0 ? "WINNER" : f.Distance == leaderDistance ? "TIED" : Tower.Signed(f.Distance - leaderDistance))} · {f.Distance}";
            HudStyle.DrawCentered(this, ArcTheme.Spaced("Bold", 3), sub, new Rect2(x, block.Position.Y - 46, colW, 30), 19, HudStyle.Dim);
        }
    }

    private void DrawAwards()
    {
        if (_awards.Count == 0)
        {
            return;
        }
        var n = _awards.Count;
        var w = (LeftWidth - (HudStyle.Gap * (n - 1))) / n;
        var top = Size.Y - AwardsHeight;
        var metaFont = ArcTheme.Spaced("Bold", 3);
        for (var i = 0; i < n; i++)
        {
            var a = _awards[i];
            var r = new Rect2(i * (w + HudStyle.Gap), top, w, AwardsHeight);
            DrawRect(r, HudStyle.Faded(HudStyle.Glass, 0.95f));
            DrawRect(r, HudStyle.Edge, false, 1);
            DrawRect(new Rect2(r.Position, new Vector2(6, r.Size.Y)), HudStyle.Red);
            HudStyle.DrawLeft(this, metaFont, a.Title, r.Position.X + 30, top + 34, 17, HudStyle.Dim);
            var x = r.Position.X + 28;
            if (a.Code is not null)
            {
                DrawRect(new Rect2(x, top + 58, 5, 34), HudStyle.Hex(a.Accent));
                x += 15;
            }
            var nameSize = HudStyle.FitSize(a.Name, HudStyle.Black, 36, 24, w * 0.55f);
            HudStyle.DrawLeft(this, HudStyle.Black, a.Name, x, top + 75, nameSize, HudStyle.Player);
            x += HudStyle.Width(HudStyle.Black, a.Name, nameSize) + 14;
            HudStyle.DrawLeft(this, HudStyle.SemiBold, HudStyle.Fit(a.Detail, HudStyle.SemiBold, 27, r.End.X - x - 16), x, top + 76, 27, Colors.White);
        }
    }
}
