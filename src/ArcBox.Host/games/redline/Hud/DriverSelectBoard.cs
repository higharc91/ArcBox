using ArcBox.Host.Shell;
using Godot;
using Redline.Logic;
using Redline.Presentation.Hud;
using Board = Redline.Presentation.Hud.DriverSelectBoard;

namespace ArcBox.Host.Games.Redline.Hud;

/// <summary>
/// Driver Select (mockup 14): the claim board on the right, 11 team rows × 2 seats (claimed seats white with the player
/// name in red, free ones "AI"), with the phase clock; and a claim lower-third bottom-left for the latest claim, the same
/// car the line-up camera holds on.
/// </summary>
public partial class DriverSelectBoard : Control
{
    public const float BoardWidth = 800;
    private const float HeadHeight = 120;
    private const float FootHeight = 44;

    private IReadOnlyList<BoardTeam> _teams = [];
    private readonly Dictionary<string, double> _claimedAt = new(StringComparer.Ordinal);
    private string _meta = "";
    private string _clock = "";
    private string _seatsLeft = "";
    private double _time;

    public DriverSelectBoard()
    {
        Name = "DriverSelectBoard";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public void Show(RedlineHostView view)
    {
        var fresh = _teams.Count == 0;
        _teams = Board.Teams(view);
        foreach (var seat in _teams.SelectMany(t => t.Seats))
        {
            if (seat.Claimed && !_claimedAt.ContainsKey(seat.Code))
            {
                _claimedAt[seat.Code] = fresh ? -10 : _time;
            }
            else if (!seat.Claimed)
            {
                _claimedAt.Remove(seat.Code);
            }
        }
        _meta = HudText.Header(view).Meta[0];
        _seatsLeft = Board.SeatsLeft(view);
        QueueRedraw();
    }

    public void SetClock(string text)
    {
        if (_clock != text)
        {
            _clock = text;
            QueueRedraw();
        }
    }

    public override void _Process(double delta)
    {
        _time += delta;
        if (_claimedAt.Values.Any(t => _time - t < 0.8))
        {
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        var w = Size.X;
        HudStyle.DrawPanel(this, new Rect2(Vector2.Zero, Size), HudStyle.Glass);

        HudStyle.DrawLeft(this, ArcTheme.Spaced("Bold", 3), _meta, 28, 38, 17, HudStyle.Dim);
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, "Driver select", 26, 84, 50, Colors.White);
        HudStyle.DrawRight(this, ArcTheme.Display("Black", 0, 0, 0), _clock, w - 28, 66, 56, Colors.White);
        DrawLine(new Vector2(0, HeadHeight - 0.5f), new Vector2(w, HeadHeight - 0.5f), HudStyle.Edge, 1);

        var rowH = (Size.Y - HeadHeight - FootHeight) / Math.Max(1, _teams.Count);
        var teamCol = 232f;
        var seatW = (w - teamCol - 16 - 14) / 2;
        for (var i = 0; i < _teams.Count; i++)
        {
            var team = _teams[i];
            var top = HeadHeight + (i * rowH);
            var mid = top + (rowH / 2);
            DrawLine(new Vector2(0, top + rowH - 0.5f), new Vector2(w, top + rowH - 0.5f), HudStyle.Edge, 1);
            DrawRect(new Rect2(0, top + 6, 6, rowH - 12), HudStyle.Hex(team.Accent));
            var teamFont = ArcTheme.Spaced("Black", 2);
            HudStyle.DrawLeft(this, teamFont, HudStyle.Fit(team.Name, teamFont, 19, teamCol - 28), 24, mid, 19, Colors.White);

            for (var s = 0; s < team.Seats.Count && s < 2; s++)
            {
                var seat = team.Seats[s];
                var cell = new Rect2(teamCol + (s * (seatW + 14)), mid - 27, seatW, 54);
                DrawSeat(cell, seat);
            }
        }

        HudStyle.DrawLeft(this, ArcTheme.Spaced("Bold", 3), _seatsLeft, 28, Size.Y - (FootHeight / 2), 16, HudStyle.Dim);
    }

    private void DrawSeat(Rect2 cell, BoardSeat seat)
    {
        var mid = cell.GetCenter().Y;
        var claimedFor = _claimedAt.TryGetValue(seat.Code, out var at) ? _time - at : double.MaxValue;
        if (seat.Claimed)
        {
            DrawRect(cell, Colors.White);
            if (claimedFor < 0.8)
            {
                // A fresh claim flashes red, then settles to white.
                DrawRect(cell, new Color(HudStyle.Red, (float)(1 - (claimedFor / 0.8))));
            }
        }
        else
        {
            DrawRect(cell, new Color(1, 1, 1, 0.05f));
        }
        var ink = seat.Claimed ? HudStyle.Dark : new Color(1, 1, 1, 0.75f);
        var number = seat.Number.ToString();
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, number, cell.Position.X + 14, mid, 25, seat.Claimed ? HudStyle.Dark : Colors.White);
        var codeX = cell.Position.X + 14 + 46;
        HudStyle.DrawLeft(this, HudStyle.Bold, seat.Code, codeX, mid, 23, ink);
        var right = cell.End.X - 14;
        if (seat.PlayerName is { } name)
        {
            var font = ArcTheme.Spaced("Black", 1);
            var max = right - codeX - HudStyle.Width(HudStyle.Bold, seat.Code, 23) - 12;
            HudStyle.DrawRight(this, font, HudStyle.Fit(name, font, 19, max), right, mid, 19, HudStyle.Red);
        }
        else
        {
            HudStyle.DrawRight(this, ArcTheme.Spaced("Black", 2), "AI", right, mid, 15, new Color(1, 1, 1, 0.3f));
        }
    }
}

/// <summary>The claim lower-third: number on the team colour, "MO CLAIMED / FERNANDO ALONSO", "ASTON MARTIN / AMR26".</summary>
public partial class ClaimLowerThird : Control
{
    public const float Height = 116;
    private ClaimCard? _card;

    public ClaimLowerThird()
    {
        Name = "ClaimLowerThird";
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public ClaimCard? Card
    {
        get => _card;
        set
        {
            _card = value;
            QueueRedraw();
        }
    }

    public override void _Draw()
    {
        if (_card is not { } c)
        {
            return;
        }
        var h = Size.Y;
        var num = new Rect2(0, 0, h, h);
        DrawRect(num, HudStyle.Hex(c.Accent));
        HudStyle.DrawCentered(this, HudStyle.BlackItalic, c.Number.ToString(), num, 66, HudStyle.Hex(c.NumberInk));

        var maxTotal = Size.X - h;
        var kickerFont = ArcTheme.Spaced("Bold", 3);
        var nameSize = HudStyle.FitSize(c.DriverName, HudStyle.BlackItalic, 52, 34, (maxTotal * 0.62f) - 56);
        var nameW = Mathf.Max(HudStyle.Width(HudStyle.BlackItalic, c.DriverName, nameSize), HudStyle.Width(kickerFont, c.Kicker, 18)) + 56;
        var teamSize = HudStyle.FitSize(c.Chassis, HudStyle.BlackItalic, 52, 30, (maxTotal - nameW) - 52);
        var teamW = Mathf.Max(HudStyle.Width(HudStyle.BlackItalic, c.Chassis, teamSize), HudStyle.Width(kickerFont, c.TeamName, 18)) + 52;

        var a = new Rect2(h, 0, nameW, h);
        DrawRect(a, HudStyle.Glass);
        HudStyle.DrawLeft(this, kickerFont, c.Kicker, a.Position.X + 26, 34, 18, HudStyle.Dim);
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, c.DriverName, a.Position.X + 24, 76, nameSize, Colors.White);

        var b = new Rect2(a.End.X, 0, teamW, h);
        DrawRect(b, HudStyle.Glass);
        DrawLine(new Vector2(b.Position.X, 16), new Vector2(b.Position.X, h - 16), HudStyle.Edge, 1);
        HudStyle.DrawLeft(this, kickerFont, c.TeamName, b.Position.X + 26, 34, 18, HudStyle.Dim);
        HudStyle.DrawLeft(this, HudStyle.BlackItalic, c.Chassis, b.Position.X + 24, 76, teamSize, Colors.White);
    }
}
