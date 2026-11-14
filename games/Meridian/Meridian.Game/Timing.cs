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
            SessionPhase.Explore => (ClockPurpose.Deadline, WaitTime(rules)),
            SessionPhase.Briefing => (ClockPurpose.Briefing, rules.BriefingTime),
            SessionPhase.Endgame when !Session.Queue.IsEmpty => (ClockPurpose.Present, TimeSpan.FromSeconds(Session.Queue[0].Seconds)),
            SessionPhase.Endgame => EndgameClock(rules),
            SessionPhase.Debrief => (ClockPurpose.Debrief, rules.DebriefTime),
            _ => (ClockPurpose.None, TimeSpan.Zero),
        };
    }

    private TimeSpan WaitTime(MeridianRules rules)
    {
        var game = Session.Game!;
        return game.Waiting switch
        {
            OrientPending => rules.RotationChoice,
            RollPending => rules.RollWait,
            ChoicePending => rules.ChoiceWait,
            TradePending => rules.TradeWait,
            AllocatePending => rules.AllocateWait,
            VotePending => rules.VoteWait,
            null when !game.ActiveCrew.Alive => rules.EchoWait,
            _ => rules.TurnInactivity,
        };
    }

    /// <summary>
    /// The endgame's clock. The seats owed a decision are the actor, or everyone still to vote. Any of them a bot plays:
    /// the next bot step. Any disconnected: the one pause for that disconnection, the same for every seat so it can't tell
    /// anyone who the traitor is (spec §3.3). Otherwise the decision's own deadline.
    /// </summary>
    private (ClockPurpose, TimeSpan) EndgameClock(MeridianRules rules)
    {
        var owed = Owed().Select(Session.SeatOf).ToList();
        if (owed.Any(s => s.Auto))
        {
            return (ClockPurpose.Bot, rules.BotStep);
        }
        if (owed.Any(s => !s.Connected))
        {
            return (ClockPurpose.Pause, rules.EndgamePause);
        }
        return (ClockPurpose.Deadline, WaitTime(rules));
    }

    /// <summary>The seats the game waits on: every seat still to vote, or the one actor.</summary>
    private IEnumerable<SeatId> Owed()
        => Session.Game!.Waiting is VotePending vote ? vote.Owed : [BotRunner.Actor(Session.Game!)];
}
