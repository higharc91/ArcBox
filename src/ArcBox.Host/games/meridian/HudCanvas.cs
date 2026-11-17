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
            var hud = HudModel.Of(view, _director.Current, _waited);
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
        var hud = HudModel.Of(view, _director.Current, _waited);
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
        DrawRect(new Rect2(0, 0, size.X, (float)top.H), new Color(0.03f, 0.03f, 0.05f, 0.9f));
        DrawLine(new Vector2(0, (float)top.H), new Vector2(size.X, (float)top.H), new Color(1, 1, 1, 0.08f), 2);

        // The banner, the scenario's title and its open objectives.
        DrawString(ArcTheme.Bold, new Vector2(28, 44), hud.Banner, HorizontalAlignment.Left, size.X * 0.45f, 34, Colors.White);
        if (hud.Scenario is { } title)
        {
            var line = hud.Objectives.Count > 0 ? $"{title.ToUpperInvariant()} · {hud.Objectives[0]}" : title.ToUpperInvariant();
            DrawString(ArcTheme.SemiBold, new Vector2(28, 76), line, HorizontalAlignment.Left, size.X * 0.55f, 22, new Color("c08cff"));
        }

        // The seat strip.
        var x = 28f;
        var y = (float)top.H - 32;
        foreach (var seat in hud.Seats)
        {
            var colour = ArcTheme.PlayerColour(seat.Colour);
            var name = seat.Crew is { } crew ? $"{seat.Name} · {crew}" : seat.Name;
            var tags = (seat.Auto ? " auto" : "") + (seat.Offline ? " offline" : "");
            var width = 44 + ArcTheme.SemiBold.GetStringSize(name, HorizontalAlignment.Left, -1, 24).X
                        + (tags.Length > 0 ? ArcTheme.Bold.GetStringSize(tags, HorizontalAlignment.Left, -1, 22).X + 8 : 0) + 18;
            var chip = new Rect2(x - 6, y - 22, width, 44);
            DrawRect(chip, seat.Active ? new Color(colour, 0.25f) : new Color(1, 1, 1, 0.04f));
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
            if (tags.Length > 0)
            {
                var tx = x + 36 + ArcTheme.SemiBold.GetStringSize(name, HorizontalAlignment.Left, -1, 24).X + 8;
                DrawString(ArcTheme.Bold, new Vector2(tx, y + 8), tags, HorizontalAlignment.Left, -1, 22, new Color("ffd447"));
            }
            x += width + 12;
        }

        // The figures, right-aligned.
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
        DrawRect(new Rect2(x, y, w, h), new Color(0.04f, 0.04f, 0.07f, 0.94f));
        DrawRect(new Rect2(x, y, 10, h), accent);
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
                DrawRect(box, face > 0 ? new Color("ffd447") : new Color(0.3f, 0.3f, 0.36f));
                DrawString(ArcTheme.Black, box.Position + new Vector2(0, 48), $"{face}", HorizontalAlignment.Center, 62, 42, new Color("101018"));
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
        for (var i = 0; i < hud.Log.Count; i++)
        {
            var alpha = 0.45f + 0.55f * (i + 1) / hud.Log.Count;
            DrawString(ArcTheme.SemiBold, new Vector2(28, y + i * 30), hud.Log[i], HorizontalAlignment.Left, (float)areas.Main.W * LogShare, 22,
                       new Color(1, 1, 1, alpha));
        }
    }
}
