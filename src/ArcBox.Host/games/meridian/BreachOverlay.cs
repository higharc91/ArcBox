using ArcBox.Host.Shell;
using Godot;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Breach;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// The Breach sequence, full screen (spec §3.1; M4 plan, Task 8): the lights fail to red, "BREACH", the scenario's name
/// and announcement, then "Read your phone" with the ready count until the endgame begins. It draws a
/// <see cref="BreachFrame"/>.
/// </summary>
public partial class BreachOverlay : Node2D
{
    private Director? _director;
    private Func<string, string> _roomName = id => id;
    private double _time;

    public void Use(Director director, Func<string, string> roomName) => (_director, _roomName) = (director, roomName);

    private bool _shown;

    public override void _Process(double delta)
    {
        _time += delta;
        var shown = _director?.Shown is { } view && BreachSequence.Of(view, _director.Current, _director.Position) is not null;
        if (shown || _shown)
        {
            QueueRedraw();   // it pulses while on screen; one more frame clears it
        }
        _shown = shown;
    }

    public override void _Draw()
    {
        if (_director?.Shown is not { } view || BreachSequence.Of(view, _director.Current, _director.Position) is not { } frame)
        {
            return;
        }
        var size = GetViewportRect().Size;
        var pulse = 0.5f + 0.5f * Mathf.Sin((float)_time * 3f);
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.35f + 0.08f * pulse, 0.02f, 0.03f, (float)(0.88 * frame.Red)));
        if (frame.Stage == BreachStage.Lights)
        {
            return;
        }
        var centre = size.X / 2;
        switch (frame.Stage)
        {
            case BreachStage.Breach:
                DrawString(ArcTheme.Black, new Vector2(0, size.Y * 0.42f), frame.Title, HorizontalAlignment.Center, size.X, 160, Colors.White);
                if (frame.Compartment is { } room)
                {
                    DrawString(ArcTheme.SemiBold, new Vector2(0, size.Y * 0.42f + 70), $"in the {_roomName(room)}", HorizontalAlignment.Center, size.X, 40,
                               new Color(1, 0.85f, 0.85f));
                }
                DrawString(ArcTheme.SemiBold, new Vector2(0, size.Y * 0.42f + 140), frame.Text, HorizontalAlignment.Center, size.X, 34, Colors.White);
                break;
            case BreachStage.Announcement:
                DrawString(ArcTheme.Black, new Vector2(0, size.Y * 0.26f), frame.Title.ToUpperInvariant(), HorizontalAlignment.Center, size.X, 84, Colors.White);
                DrawMultilineString(ArcTheme.SemiBold, new Vector2(size.X * 0.18f, size.Y * 0.26f + 90), frame.Text, HorizontalAlignment.Center,
                                    size.X * 0.64f, 36, 8, new Color(1, 0.94f, 0.94f));
                break;
            case BreachStage.Phones:
                DrawString(ArcTheme.Bold, new Vector2(0, size.Y * 0.3f), frame.Title.ToUpperInvariant(), HorizontalAlignment.Center, size.X, 48,
                           new Color(1, 0.85f, 0.85f));
                DrawString(ArcTheme.Black, new Vector2(0, size.Y * 0.47f), frame.Text, HorizontalAlignment.Center, size.X, 110, Colors.White);
                DrawString(ArcTheme.Bold, new Vector2(0, size.Y * 0.47f + 90), $"{frame.Ready} of {frame.Seats} ready", HorizontalAlignment.Center,
                           size.X, 40, new Color("ffd447"));
                // One pip per seat, filled for each that's ready: a count, never which seats.
                var pips = frame.Seats;
                for (var i = 0; i < pips; i++)
                {
                    var at = new Vector2(centre + (i - (pips - 1) / 2f) * 44, size.Y * 0.47f + 140);
                    DrawCircle(at, 14, i < frame.Ready ? new Color("ffd447") : new Color(1, 1, 1, 0.18f));
                }
                break;
        }
    }
}
