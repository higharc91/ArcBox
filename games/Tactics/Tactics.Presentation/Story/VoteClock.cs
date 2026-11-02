using System.Globalization;

namespace Tactics.Presentation.Story;

/// <summary>
/// The countdown on a question (overworld 5d §10), as <c>HudClock</c> does REDLINE's: the server's deadline is read against the TV's clock
/// when it changes, clamped to the question's length, and then counted on frame time, so a view that only adds a vote never makes it jump.
/// A TV's clock can be off the server's. When the TV sees a question open, its deadline is the server's now plus the question's length, which
/// tells how far off: every deadline after that is read on the server's time. Until then the clamp keeps it inside the question.
/// </summary>
public sealed class VoteClock
{
    private DateTimeOffset? _deadline;
    private double _length;
    private TimeSpan _skew;

    /// <summary>The seconds left, or null with no deadline: a question held for a phone.</summary>
    public double? Remaining { get; private set; }

    /// <summary>The clock's words: "0:14", or null with no deadline.</summary>
    public string? Text => Remaining is { } seconds ? Format(seconds) : null;

    /// <summary>
    /// A view's <paramref name="deadline"/>, read at <paramref name="now"/> on the TV's clock, for a question of <paramref name="length"/>
    /// seconds. <paramref name="opened"/>: the question opened as the view was sent, with its whole length to run, which is what the TV's clock
    /// is checked against. The same deadline again changes nothing.
    /// </summary>
    public void Read(DateTimeOffset? deadline, DateTimeOffset now, double length, bool opened = false)
    {
        if (deadline is not { } due)
        {
            Stop();
            return;
        }
        if (opened)
            _skew = due - now - TimeSpan.FromSeconds(length);
        else if (due == _deadline && length == _length)
            return;
        (_deadline, _length) = (due, length);
        Remaining = Math.Clamp((due - (now + _skew)).TotalSeconds, 0, Math.Max(0, length));
    }

    /// <summary>A frame of <paramref name="seconds"/> went by.</summary>
    public void Tick(double seconds)
    {
        if (Remaining is { } left)
            Remaining = Math.Max(0, left - Math.Max(0, seconds));
    }

    /// <summary>No question: no clock. What it learned of the TV's clock stays.</summary>
    public void Stop() => (_deadline, Remaining) = (null, null);

    /// <summary>"0:14": whole seconds rounded up, so it shows 0:00 only when time is really up.</summary>
    public static string Format(double seconds)
    {
        var s = (int)Math.Ceiling(Math.Max(0, seconds) - 1e-6);
        return string.Create(CultureInfo.InvariantCulture, $"{s / 60}:{s % 60:00}");
    }
}
