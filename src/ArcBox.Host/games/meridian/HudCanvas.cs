using ArcBox.Host.Shell;
using Godot;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Board;
using Meridian.Presentation.Debrief;
using Meridian.Presentation.Hud;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// The TV's HUD (spec §5.5; M4 plan, Task 7): the seat strip and the figures across the top, the card panel or dice tray
/// over the board, the log and the waiting banner. It draws a <see cref="HudModel"/>; sizes keep to the 22 px label
/// minimum at 1080p, and every seat shows its mark as well as its colour.
/// </summary>
public partial class HudCanvas : Node2D
{
    private Director? _director;
    private double _waited;
    private object? _waitFor;
    private double _time;

    public void Use(Director director, ArtTextures textures) => (_director, _textures) = (director, textures);

    private ArtTextures? _textures;

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;   // pixel-art portraits stay crisp

    public override void _Process(double delta)
    {
        _time += delta;
        // The banner counts down from the seconds the view gave, restarting whenever a new wait arrives.
        var wait = _director?.Shown?.Wait;
        if (!Equals(wait, _waitFor))   // by value: every view is deserialized afresh
        {
            (_waitFor, _waited) = (wait, 0);
        }
        _waited += delta;
        // Redraw when what the HUD says changes (the model is a record: equality is by value), or while a panel slides
        // or dice tumble.
        if (_director?.Shown is { } view)
        {
            var hud = HudModel.Of(view, _director.Current, _waited, _textures?.Looks);
            if (!hud.Equals(_drawn) || _director.Current is { Short: false } || !ReferenceEquals(view, _drawnView))
            {
                (_drawn, _drawnView) = (hud, view);
                QueueRedraw();
            }
        }
    }

    private HudModel? _drawn;
    private object? _drawnView;

    public override void _Draw()
    {
        if (_director?.Shown is not { } view)
        {
            return;
        }
        var size = GetViewportRect().Size;
        var areas = DeckLayout.Areas(new TvSize((int)size.X, (int)size.Y));
        var hud = HudModel.Of(view, _director.Current, _waited, _textures?.Looks);
        DrawTop(hud, areas, size);
        if (DebriefModel.Of(view) is not null)
        {
            return;   // the Debrief has its own column
        }
        DrawPanel(hud, areas);
        DrawLog(hud, areas);
        if (hud.Wait is { } wait)
        {
            var w = ArcTheme.Bold.GetStringSize(wait, HorizontalAlignment.Left, -1, 30).X + 40;
            var at = new Vector2((float)(areas.Main.X + areas.Main.W / 2) - w / 2, (float)areas.Main.Y + 16);
            DrawRect(new Rect2(at, new Vector2(w, 50)), new Color("ffd447"));
            DrawString(ArcTheme.Bold, at + new Vector2(20, 36), wait, HorizontalAlignment.Left, -1, 30, new Color("101018"));
        }
    }

    private void DrawTop(HudModel hud, Areas areas, Vector2 size)
    {
        var top = areas.Hud;
        if (_textures?.DrawBox(this, UiKit.For(_textures.Art, UiElement.Figures), new Rect2(0, 0, size.X, (float)top.H)) != true)
        {
            DrawRect(new Rect2(0, 0, size.X, (float)top.H), new Color(0.03f, 0.03f, 0.05f, 0.9f));
        }
        DrawLine(new Vector2(0, (float)top.H), new Vector2(size.X, (float)top.H), new Color(1, 1, 1, 0.08f), 2);

        // The figures first, right-aligned, so the text beside them knows where to stop.
        var statsLeft = DrawStats(hud, size);

        // The banner, the scenario's title and its open objectives, never running into the figures.
        var room = Math.Max(200f, statsLeft - 28 - 24);
        DrawString(ArcTheme.Bold, new Vector2(28, 44), hud.Banner, HorizontalAlignment.Left, Math.Min(size.X * 0.45f, room), 34, Colors.White);
        if (hud.Scenario is { } title)
        {
            var line = hud.Objectives.Count > 0 ? $"{title.ToUpperInvariant()} · {hud.Objectives[0]}" : title.ToUpperInvariant();
            DrawString(ArcTheme.SemiBold, new Vector2(28, 76), line, HorizontalAlignment.Left, room, 22, new Color("c08cff"));
        }

        // The seat strip. It fits the screen: with six seats, crew names go first (the name, mark and colour still say who),
        // then per-seat counters shrink to their numbers.
        var x = 28f;
        var y = (float)top.H - 32;
        var fit = 0;
        while (fit < 2 && hud.Seats.Sum(s => Chip(s, fit).Width + 12) > size.X - 56)
        {
            fit++;
        }
        foreach (var seat in hud.Seats)
        {
            var colour = ArcTheme.PlayerColour(seat.Colour);
            var (name, tags, badges, width) = Chip(seat, fit);
            var chip = new Rect2(x - 6, y - 22, width, 44);
            if (_textures?.DrawBox(this, UiKit.For(_textures.Art, UiElement.SeatSlot), chip) != true)
            {
                DrawRect(chip, seat.Active ? new Color(colour, 0.25f) : new Color(1, 1, 1, 0.04f));
            }
            else if (seat.Active)
            {
                DrawRect(chip, new Color(colour, 0.25f));   // the active seat's tint over the slot art
            }
            if (seat.Active)
            {
                DrawRect(chip, colour, false, 3);
            }
            if (_textures?.Portrait(seat.Crew) is { } portrait)
            {
                // The crew member's portrait (#333) in a ring of the player's colour; the name beside it carries the seat.
                DrawCircle(new Vector2(x + 14, y), 17, colour);
                DrawTextureRect(portrait, new Rect2(x - 1, y - 15, 30, 30), false);
            }
            else
            {
                DrawCircle(new Vector2(x + 14, y), 15, colour);
                DrawString(ArcTheme.Black, new Vector2(x - 1, y + 7), seat.Mark, HorizontalAlignment.Center, 30, seat.Mark.Length > 1 ? 16 : 20, new Color("101018"));
            }
            DrawString(ArcTheme.SemiBold, new Vector2(x + 36, y + 8), name, HorizontalAlignment.Left, -1, 24, seat.Offline ? ArcTheme.Dim : Colors.White);
            var tx = x + 36 + ArcTheme.SemiBold.GetStringSize(name, HorizontalAlignment.Left, -1, 24).X + 8;
            if (tags.Length > 0)
            {
                DrawString(ArcTheme.Bold, new Vector2(tx, y + 8), tags, HorizontalAlignment.Left, -1, 22, new Color("ffd447"));
                tx += ArcTheme.Bold.GetStringSize(tags, HorizontalAlignment.Left, -1, 22).X + 8;
            }
            if (badges.Length > 0)
            {
                DrawString(ArcTheme.Bold, new Vector2(tx + 6, y + 8), badges, HorizontalAlignment.Left, -1, 22, new Color("c08cff"));
            }
            x += width + 12;
        }
    }

    /// <summary>
    /// A seat chip's text and width at a level of fit: 0 in full, 1 without the crew name, 2 with counters as bare numbers.
    /// </summary>
    private static (string Name, string Tags, string Badges, float Width) Chip(HudSeat seat, int fit)
    {
        var name = seat.Crew is { } crew && fit == 0 ? $"{seat.Name} · {crew}" : seat.Name;
        var tags = (seat.Auto ? " auto" : "") + (seat.Offline ? " offline" : "");
        // The scenario's per-seat counters ("Infection 4"), on the chip they belong to.
        var badges = string.Join("  ", (seat.Badges.Items.IsDefaultOrEmpty ? [] : seat.Badges.Items)
            .Select(b => fit < 2 ? $"{b.Label} {b.Value}" : b.Value));
        var width = 44 + ArcTheme.SemiBold.GetStringSize(name, HorizontalAlignment.Left, -1, 24).X
                    + (tags.Length > 0 ? ArcTheme.Bold.GetStringSize(tags, HorizontalAlignment.Left, -1, 22).X + 8 : 0)
                    + (badges.Length > 0 ? ArcTheme.Bold.GetStringSize(badges, HorizontalAlignment.Left, -1, 22).X + 14 : 0) + 18;
        return (name, tags, badges, width);
    }

    /// <summary>The figures, right-aligned, as many as fit beside the banner. Returns the left edge of the leftmost drawn.</summary>
    private float DrawStats(HudModel hud, Vector2 size)
    {
        var right = size.X - 28;
        foreach (var stat in hud.Stats.Reverse())
        {
            var w = Math.Max(ArcTheme.Bold.GetStringSize(stat.Value, HorizontalAlignment.Left, -1, 34).X,
                             ArcTheme.SemiBold.GetStringSize(stat.Label, HorizontalAlignment.Left, -1, 22).X) + 24;
            if (right - w < size.X * 0.48f)
            {
                break;   // no room left beside the banner: the panel and the phones carry the rest
            }
            DrawString(ArcTheme.SemiBold, new Vector2(right - w, 34), stat.Label, HorizontalAlignment.Left, -1, 22, ArcTheme.Dim);
            DrawString(ArcTheme.Bold, new Vector2(right - w, 72), stat.Value, HorizontalAlignment.Left, -1, 34, Colors.White);
            right -= w + 10;
        }
        return right;
    }

    /// <summary>The card panel or dice tray, sliding in and out with the beat's steps.</summary>
    private void DrawPanel(HudModel hud, Areas areas)
    {
        if (hud.Panel is not { } panel || _director?.Position is not { } at)
        {
            return;
        }
        var slide = at.Step.Name switch
        {
            "in" => 1 - (float)at.Progress,
            "out" => (float)at.Progress,
            _ => 0f,
        };
        // Beside the log column, along the bottom of the main area.
        var main = areas.Main;
        var x = (float)(main.X + main.W * LogShare) + 24;
        var w = (float)(main.X + main.W) - x - 28;
        // Dice need their row; text takes the lines it needs, up to four.
        var lines = Math.Clamp((int)Math.Ceiling(ArcTheme.SemiBold.GetStringSize(panel.Text, HorizontalAlignment.Left, -1, 30).X / (w - 60)), 1, 4);
        var h = panel.Faces.Count > 0 ? 190f : (panel.Title.Length > 0 ? 100f : 50f) + lines * 46f + 24f;
        var y = (float)(main.Y + main.H) - h - 28 + slide * (h + 40);
        var accent = new Color(panel.Accent);
        // The panel's art: a card's deck frame, the dice tray, or the kit's panel; else M4's flat panel with its accent bar.
        var element = panel.Faces.Count > 0 ? UiElement.DiceTray : UiElement.CardPanel;
        if (_textures?.DrawBox(this, UiKit.For(_textures.Art, element, panel.Deck), new Rect2(x, y, w, h)) != true)
        {
            DrawRect(new Rect2(x, y, w, h), new Color(0.04f, 0.04f, 0.07f, 0.94f));
            DrawRect(new Rect2(x, y, 10, h), accent);
        }
        if (panel.Title.Length > 0)
        {
            DrawString(ArcTheme.Bold, new Vector2(x + 36, y + 56), panel.Title, HorizontalAlignment.Left, w - 60, 40, Colors.White);
        }
        if (panel.Faces.Count > 0)
        {
            // The dice: tumbling, then landed, then the total beside them.
            var landed = at.Step.Name != "tumble" && at.Step.Name != "flash";
            for (var i = 0; i < panel.Faces.Count; i++)
            {
                var face = landed ? panel.Faces[i] : (int)(_time * 12 + i * 2) % 3;
                var box = new Rect2(x + 36 + i * 74, y + 84, 62, 62);
                // The die's art (#335): the roll strip while tumbling, a face once landed; else the drawn die.
                var art = _textures?.Art ?? global::Meridian.Presentation.Art.MeridianArt.None;
                var drawn = landed ? _textures?.DrawFrame(this, UiKit.DieFace(art, face), box, 0)
                                   : _textures?.DrawFrame(this, UiKit.DieRoll(art), box, _time + i * 0.13);
                if (drawn != true)
                {
                    DrawRect(box, face > 0 ? new Color("ffd447") : new Color(0.3f, 0.3f, 0.36f));
                    DrawString(ArcTheme.Black, box.Position + new Vector2(0, 48), $"{face}", HorizontalAlignment.Center, 62, 42, new Color("101018"));
                }
            }
            if (landed)
            {
                DrawString(ArcTheme.SemiBold, new Vector2(x + 36 + panel.Faces.Count * 74 + 20, y + 128), panel.Text, HorizontalAlignment.Left,
                           w - panel.Faces.Count * 74 - 90, 30, Colors.White);
            }
            return;
        }
        DrawMultilineString(ArcTheme.SemiBold, new Vector2(x + 36, panel.Title.Length > 0 ? y + 100 : y + 60), panel.Text,
                            HorizontalAlignment.Left, w - 60, 30, 4, new Color(1, 1, 1, 0.92f));
    }

    /// <summary>The share of the main area's width the log keeps, bottom left.</summary>
    private const float LogShare = 0.3f;

    private void DrawLog(HudModel hud, Areas areas)
    {
        var y = (float)(areas.Main.Y + areas.Main.H) - 24 - (hud.Log.Count - 1) * 30;
        if (hud.Log.Count > 0 && _textures is { } textures)
        {
            // A panel behind the log only when the kit has one: M4 drew the log straight onto the board.
            textures.DrawBox(this, UiKit.For(textures.Art, UiElement.Log),
                             new Rect2(12, y - 34, (float)areas.Main.W * LogShare + 32, hud.Log.Count * 30 + 30));
        }
        for (var i = 0; i < hud.Log.Count; i++)
        {
            var alpha = 0.45f + 0.55f * (i + 1) / hud.Log.Count;
            DrawString(ArcTheme.SemiBold, new Vector2(28, y + i * 30), hud.Log[i], HorizontalAlignment.Left, (float)areas.Main.W * LogShare, 22,
                       new Color(1, 1, 1, alpha));
        }
    }
}
