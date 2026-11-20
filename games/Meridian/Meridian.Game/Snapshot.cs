using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using Meridian.Logic;

namespace Meridian.Game;

/// <summary>
/// What a snapshot holds (spec §5.6; M6 plan, Task 3): the whole session, game state and random generators included, so
/// a resumed game draws what it would have drawn. Clocks and connections are not kept: <see cref="MeridianGame.Resume"/>
/// rebuilds them.
/// </summary>
internal sealed record SnapshotEnvelope(int Version, MeridianSession Session);

// Snapshots and resume (spec §5.6; M6 plan, Tasks 3 and 4).
public sealed partial class MeridianGame : ISnapshotGame
{
    /// <summary>The snapshot format. A snapshot of another version is refused, which leaves the room in the lobby.</summary>
    public const int SnapshotVersion = 1;

    /// <summary>The moment the last snapshot was taken at; a new one is due when the moment changes.</summary>
    private Moment? _snapshotAt;

    private MeridianSession? _restored;

    /// <summary>A turn and a phase: the session's position, as far as snapshots go.</summary>
    private readonly record struct Moment(SessionPhase Phase, int Round, string? Active);

    private Moment CurrentMoment => new(Session.Phase, Session.Game?.Round ?? 0, Session.Game?.ActiveSeat.Value);

    /// <summary>
    /// A snapshot at the start of each turn and at each phase change, and none in between: about one small write a
    /// minute, and a restart replays at most one turn.
    /// </summary>
    public JsonElement? TakeSnapshot()
    {
        if (Session is null || Session.Phase == SessionPhase.Over || Session.Resuming)
        {
            return null;
        }
        var moment = CurrentMoment;
        if (moment == _snapshotAt)
        {
            return null;
        }
        _snapshotAt = moment;
        return JsonSerializer.SerializeToElement(new SnapshotEnvelope(SnapshotVersion, Session), ArcJson.Options);
    }

    /// <summary>Reads a snapshot for <see cref="Resume"/>. Throws for another version or a session that won't read.</summary>
    public void Restore(JsonElement snapshot)
    {
        var envelope = snapshot.Deserialize<SnapshotEnvelope>(ArcJson.Options)
                       ?? throw new JsonException("An empty Meridian snapshot.");
        if (envelope.Version != SnapshotVersion)
        {
            throw new JsonException($"A Meridian snapshot of version {envelope.Version}; this build reads {SnapshotVersion}.");
        }
        if (envelope.Session?.Seats.IsEmpty != false)
        {
            throw new JsonException("A Meridian snapshot with no session.");
        }
        _restored = envelope.Session;
    }

    /// <summary>
    /// Picks up from the restored snapshot after a restart. Every seat comes back disconnected and played by nobody, and
    /// the clock is rebuilt; the platform reconnects players one by one.
    /// </summary>
    public void Resume(IGameContext ctx, IReadOnlyList<Player> players, ICampaignLease? campaign)
    {
        var restored = _restored ?? throw new InvalidOperationException("Resume before Restore.");
        if (restored.Seats.Any(s => players.All(p => p.Id != s.Player)))
        {
            throw new InvalidOperationException("The snapshot's seats don't match the room's players.");
        }
        _ctx = ctx;
        _pack = pack.Value;
        var seats = restored.Seats.Select(s => s with { Connected = false, Auto = false, PauseUsed = false }).ToEqArray();
        Session = restored with { Seats = seats, Clock = new Clock(restored.Clock.Seq, ClockPurpose.None), Resuming = true };
        _snapshotAt = CurrentMoment;   // this is the moment the snapshot was taken at: nothing new to save yet
        _restored = null;
        Rearm(fresh: true);
    }

    /// <summary>
    /// Phones are back, or the wait for them is over: play on. A fresh decision gets its full time, and a seat still
    /// missing gets the usual grace or pause, then a bot.
    /// </summary>
    private void EndResuming()
    {
        Session = Session with { Resuming = false };
        Rearm(fresh: true);
    }
}
