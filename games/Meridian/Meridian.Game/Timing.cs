using System.Globalization;

namespace Meridian.Game;

/// <summary>The clock's timer keys: <c>clock:{seq}</c>.</summary>
internal static class Timing
{
    public static string Key(int seq) => $"clock:{seq.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string key, out int seq)
    {
        seq = 0;
        return key.StartsWith("clock:", StringComparison.Ordinal)
               && int.TryParse(key.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out seq);
    }
}

public sealed partial class MeridianGame
{
    /// <summary>
    /// Re-arms the one clock for whatever the game now waits on. Called after every change: it cancels the old timer and
    /// sets a new one under a new sequence number, so an old timer that already fired is ignored when it arrives.
    /// </summary>
    private void Rearm()
    {
        var (purpose, after) = NextClock();
        _ctx.CancelTimer(Timing.Key(Session.Clock.Seq));
        var clock = new Clock(Session.Clock.Seq + 1, purpose);
        Session = Session with { Clock = clock };
        if (purpose != ClockPurpose.None)
        {
            _ctx.SetTimer(Timing.Key(clock.Seq), after);
        }
    }

    private (ClockPurpose Purpose, TimeSpan After) NextClock() => Session.Phase switch
    {
        SessionPhase.Boarding => (ClockPurpose.Boarding, Remaining(ClockPurpose.Boarding, _pack.Rules.BoardingTime)),
        _ => (ClockPurpose.None, TimeSpan.Zero),
    };

    /// <summary>
    /// A clock that keeps its purpose keeps its deadline: Boarding's 45 s doesn't restart every time someone picks, and
    /// a disconnected seat's grace doesn't restart when another phone drops.
    /// </summary>
    private TimeSpan Remaining(ClockPurpose purpose, TimeSpan full)
        => Session.Clock.Purpose == purpose && Deadline is { } due ? due - _ctx.Now : full;
}
