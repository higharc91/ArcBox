using System.Globalization;

namespace Tactics.Presentation.Beats;

/// <summary>
/// The TV's frame: tick the beats, draw, and show what's held once they've played. What fails here must not stick the TV, so
/// the two kinds of failure are kept apart. A beat that throws while it's played is dropped, and still reported, as nothing else can
/// move past it. A failure to draw is nobody's beat: it's logged once and the beats go on, with nothing on screen to show
/// for it, so the queue still drains and whatever's held behind it still comes up.
/// </summary>
/// <param name="player">The beats.</param>
/// <param name="signal">Reports a finished beat.</param>
/// <param name="present">Draws what a tick did.</param>
/// <param name="log">Reports a failure.</param>
public sealed class BeatLoop(BeatPlayer player, Action<int> signal, Action<BeatTick> present, Action<string> log)
{
    private readonly HashSet<string> _logged = new(StringComparer.Ordinal);

    /// <summary>What to do once nothing's left to play, as show the results; null for nothing.</summary>
    public Action? Held { get; set; }

    /// <summary>One frame of <paramref name="delta"/> seconds.</summary>
    public void Step(double delta)
    {
        if (TickBeats(delta) is { } tick)
        {
            Guard(() => present(tick));
            foreach (var seq in tick.Shown)
                signal(seq);
        }
        if (Held is { } held && player.Idle)
        {
            Held = null;
            held();
        }
    }

    /// <summary>Runs <paramref name="draw"/>, and if it fails logs that, once for each different failure.</summary>
    public void Guard(Action draw)
    {
        try
        {
            draw();
        }
        catch (Exception ex)    // whatever it was: a TV that can't draw one thing still has a game to follow
        {
            Once(ex);
        }
    }

    private BeatTick? TickBeats(double delta)
    {
        try
        {
            return player.Tick(delta);
        }
        catch (Exception ex)    // whatever a beat threw
        {
            if (player.Skip() is { } seq)
            {
                log($"dropped beat {seq.ToString(CultureInfo.InvariantCulture)}: {ex.Message}");
                signal(seq);
            }
            else
            {
                Once(ex);
            }
            return null;
        }
    }

    private void Once(Exception ex)
    {
        var message = $"{ex.GetType().Name}: {ex.Message}";
        if (_logged.Add(message))
            log(message);
    }
}
