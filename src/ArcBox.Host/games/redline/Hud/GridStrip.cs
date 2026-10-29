using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Hud;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// The bottom strip outside the question phases. Grid (mockup 11): the throttle rules with the real payouts from the
/// view, the race line ("First to 28 or most distance after 15 rounds") and the question attribution. Throttle (mockup 03):
/// how many players have locked in, and the payout chips.
/// </summary>
public partial class GridStrip : Control
{
    private IReadOnlyList<RuleItem> _rules = [];
    private IReadOnlyList<Chip> _legend = [];
    private string _raceLine = "";
    private string _attribution = "";
    private string _locked = "";
    private bool _throttle;

    public GridStrip()
    {
        Name = "GridStrip";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(RedlineHostView view)
    {
        // An older server may not send payouts: then the strip names the throttles without numbers.
        _rules = view.Payouts is { } p ? HudText.Rules(p) : [];
        _legend = view.Payouts is { } q ? HudText.ThrottleLegend(q) : [];
        _raceLine = HudText.RaceLine(view);
        _attribution = view.Attribution;
        _locked = HudText.LockedLine(view);
        _throttle = view.Phase == RacePhase.Throttle;
        QueueRedraw();
    }

    public override void _Draw()
    {
        HudStyle.DrawPanel(this, new Rect2(Vector2.Zero, Size));
        var mid = Size.Y / 2;
        if (_throttle)
        {
            DrawThrottle(mid);
        }
        else
        {
            DrawRules(mid);
        }
    }

    private void DrawThrottle(float mid)
    {
        HudStyle.DrawLeft(this, ArcTheme.Spaced("Bold", 3), _locked, 30, mid, 19, Colors.White);
        var x = Size.X - 24;
        foreach (var chip in _legend.Reverse())
        {
            var font = ArcTheme.Spaced("Black", 2);
            var w = HudStyle.Width(font, chip.Text, 16) + 30;
            x -= w;
            HudStyle.DrawChip(this, new Rect2(x, mid - 16, w, 32), chip, 16);
            x -= 12;
        }
    }

    private void DrawRules(float mid)
    {
        var x = 24f;
        var items = _rules.Count > 0
            ? _rules
            : [new RuleItem(Tower.ThrottleChip(Throttle.Cruise), "safe"), new RuleItem(Tower.ThrottleChip(Throttle.Push), "lose ground if wrong"),
               new RuleItem(Tower.ThrottleChip(Throttle.Redline), "spin out if wrong")];
        for (var i = 0; i < items.Count; i++)
        {
            var (chip, payout) = (items[i].Chip, items[i].Payout);
            var label = chip.Text;
            var w = HudStyle.Width(HudStyle.BlackItalic, label, 30) + 34;
            var box = new Rect2(x, mid - 25, w, 50);
            var (bg, ink, _) = HudStyle.Chip(chip.Kind);
            DrawRect(box, chip.Kind == ChipKind.Cruise ? new Color(1, 1, 1, 0.14f) : bg);
            HudStyle.DrawCentered(this, HudStyle.BlackItalic, label, box, 30, chip.Kind == ChipKind.Cruise ? new Color("d8d8e0") : ink);
            x = box.End.X + 16;
            HudStyle.DrawLeft(this, HudStyle.SemiBold, payout, x, mid, 27, Colors.White);
            x += HudStyle.Width(HudStyle.SemiBold, payout, 27) + 16;
            if (i < items.Count - 1)
            {
                DrawCircle(new Vector2(x, mid + 1), 2.5f, HudStyle.Dim);
                x += 18;
            }
        }

        var right = Size.X - 26;
        var room = right - x - 30;
        var lineFont = ArcTheme.Spaced("Bold", 1);
        HudStyle.DrawRight(this, lineFont, HudStyle.Fit(_raceLine, lineFont, 21, room), right, mid - 13, 21, Colors.White);
        HudStyle.DrawRight(this, HudStyle.SemiBold, HudStyle.Fit(_attribution, HudStyle.SemiBold, 17, room), right, mid + 15, 17, HudStyle.Dim);
    }
}
