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
    /// <param name="fresh">
    /// A new decision: give it its full time. Otherwise (a phone dropped, someone picked a crew member) a clock that keeps
    /// its purpose keeps its deadline, so nobody can stretch someone else's turn.
    /// </param>
    private void Rearm(bool fresh = false)
    {
        var (purpose, full) = NextClock();
        var after = !fresh && Session.Clock.Purpose == purpose && Deadline is { } due ? due - _ctx.Now : full;
        _ctx.CancelTimer(Timing.Key(Session.Clock.Seq));
        var clock = new Clock(Session.Clock.Seq + 1, purpose);
        Session = Session with { Clock = clock };
        if (purpose != ClockPurpose.None)
        {
            _ctx.SetTimer(Timing.Key(clock.Seq), after);
        }
    }

    /// <summary>What the game waits on now, and the full time for it.</summary>
    private (ClockPurpose Purpose, TimeSpan Full) NextClock()
    {
        var rules = _pack.Rules;
        return Session.Phase switch
        {
            SessionPhase.Boarding => (ClockPurpose.Boarding, rules.BoardingTime),
            SessionPhase.Explore when !Session.Queue.IsEmpty => (ClockPurpose.Present, TimeSpan.FromSeconds(Session.Queue[0].Seconds)),
            SessionPhase.Explore when Session.SeatOf(BotRunner.Actor(Session.Game!)) is { Auto: true } => (ClockPurpose.Bot, rules.BotStep),
            SessionPhase.Explore when Session.SeatOf(BotRunner.Actor(Session.Game!)) is { Connected: false } => (ClockPurpose.Grace, rules.DisconnectGrace),
            SessionPhase.Explore => (ClockPurpose.Deadline, Session.Game!.Waiting switch
            {
                OrientPending => rules.RotationChoice,
                RollPending => rules.RollWait,
                ChoicePending => rules.ChoiceWait,
                TradePending => rules.TradeWait,
                _ => rules.TurnInactivity,
            }),
            _ => (ClockPurpose.None, TimeSpan.Zero),
        };
    }
}
