using ArcBox.Sdk;
using ArcBox.Server.Campaigns;

namespace ArcBox.Server.Rooms;

// Durable rooms (spec §3.5): the room's membership is saved as a RoomRecord at every change that matters, and a room
// that left memory (a restart, or parked when idle) is rebuilt from it with every seat offline.
public sealed partial class Room
{
    public const string InterruptedByRestart = "Game interrupted: the server restarted.";
    public const string InterruptedByIdle = "Game interrupted: the room was idle.";

    // Results owed, as a GameEnded, to the host and to these seats the first time each binds after a rebuild. Only the
    // actor touches them.
    private GameResults? _notice;
    private bool _noticeForHost;
    private readonly HashSet<PlayerId> _noticeForSlots = [];

    // When the room last queued its record, as UTC ticks; read off the actor by the room manager.
    private long _savedTicks;

    /// <summary>True once the room has closed (or been parked); readable from any thread.</summary>
    public bool IsClosed => _status == RoomStatus.Closed;

    /// <summary>When the room last saved its record, or null if it never has; readable from any thread.</summary>
    public DateTimeOffset? SavedUtc => Interlocked.Read(ref _savedTicks) is var ticks and not 0
        ? new DateTimeOffset(ticks, TimeSpan.Zero)
        : null;

    /// <summary>
    /// A room from its stored record, with every seat offline. A game that was running is dealt with by the first
    /// message the actor runs, before any caller's: resumed if its manifest says so and it can have its campaign back,
    /// otherwise the room is in the lobby and owes everyone a "Game interrupted" note.
    /// </summary>
    public static Room Rebuild(RoomRecord record, RoomServices services)
    {
        var room = new Room(record.Code, record.HostTokenHash, services, record.CreatedUtc);
        room._members.Restore(record.Slots, record.NextSlotId);

        // A game that is no longer installed leaves nothing selected.
        var manifest = record.SelectedGame is { } id
            ? services.Registry.Manifests.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase))
            : null;
        room._selectedGameId = manifest?.Id;
        room._campaignId = manifest is null ? null : record.CampaignId;

        bool running = record.GameRunning && manifest is not null;
        bool parked = record.Parked;
        room.Enqueue(() => room.Recover(running, parked));
        return room;
    }

    /// <summary>The room's membership as stored. Call it on the actor.</summary>
    public RoomRecord ToRecord() => new(
        Code,
        _hostTokenHash,
        [.. _members.All.Select(s => new SlotRecord(s.Id, s.Name, s.SecretHash, s.JoinOrder, s.Profile))],
        _members.NextSlotId,
        _selectedGameId,
        _campaignId,
        GameRunning: _status == RoomStatus.InGame,
        _createdUtc,
        _time.GetUtcNow());

    /// <summary>Saves the room as it is now, on the actor. The room manager calls it when it creates the room.</summary>
    public Task SaveAsync() => EnqueueAsync(Save);

    /// <summary>
    /// Parks the room if it has been idle for <see cref="RoomOptions.IdleTimeout"/>, in one step on the actor: its row
    /// is saved as parked (a running game stays marked as running), anyone still connected is told the room closed, and
    /// the room closes, which gives back its campaign. Returns whether it parked.
    /// </summary>
    public Task<bool> TryParkAsync() => EnqueueAsync(() =>
    {
        if (!IsExpired())
        {
            return false;
        }

        _store.Save(ToRecord() with { Parked = true });
        ForceClose("Room closed after being idle.");
        return true;
    });

    /// <summary>Queues the room's record. A closed room saves nothing, so a closed room's row stays as it was left.</summary>
    private void Save()
    {
        if (_status != RoomStatus.Closed)
        {
            var record = ToRecord();
            _store.Save(record);
            Interlocked.Exchange(ref _savedTicks, record.UpdatedUtc.UtcTicks);
        }
    }

    /// <summary>Settles a rebuilt room whose record says a game was running, then saves where it ended up.</summary>
    private void Recover(bool gameRunning, bool parked)
    {
        if (gameRunning)
        {
            var interrupted = parked ? InterruptedByIdle : InterruptedByRestart;
            if (TryResume(interrupted) is { } message)
            {
                _lastResults = new GameResults([], message);
                _notice = _lastResults;
                _noticeForHost = true;
                _noticeForSlots.UnionWith(_members.All.Select(s => s.Id));
                BroadcastLobby();
            }
        }

        Save();
    }

    /// <summary>
    /// Resumes the selected game if its manifest has <see cref="GameManifest.ResumesAfterRestart"/>, checking its
    /// campaign out again first. Returns null when it resumed, or the note the lobby shows instead.
    /// </summary>
    private string? TryResume(string interrupted)
    {
        if (SelectedManifest is not { ResumesAfterRestart: true } manifest)
        {
            return interrupted;
        }

        var game = _registry.Create(manifest.Id);
        if (game is not IResumableGame)
        {
            _logger.LogWarning("Game {GameId} says it resumes after a restart but isn't an IResumableGame", manifest.Id);
            return interrupted;
        }

        CampaignLease? lease = null;
        if (manifest.UsesCampaigns)
        {
            if (SelectedCampaign() is not { } campaign)
            {
                return interrupted;
            }

            // Another room may have picked the campaign up while this one was away; it isn't resumed twice.
            var id = campaign.Info.Id;
            lease = _campaigns.CheckOut(id, Code);
            if (lease is null)
            {
                return $"{campaign.Info.Name} is being played in room {_campaigns.LeasedBy(id)}.";
            }
            Interlocked.Exchange(ref _leasedCampaignId, id);
        }

        // As at Start, a seat offline when the game begins is absent; after a rebuild that is every seat.
        foreach (var slot in _members.All)
        {
            slot.AbsentAtStart = true;
        }

        _session = new GameSession(game, Players(), _time, Random.Shared.Next(), Enqueue, _logger, lease);
        _session.TimerFired += Publish;
        _status = RoomStatus.InGame;
        _session.Resume();

        BroadcastLobby();
        Publish();
        return null;
    }

    /// <summary>The note owed to the host, once.</summary>
    private GameResults? TakeHostNotice()
    {
        if (!_noticeForHost)
        {
            return null;
        }

        _noticeForHost = false;
        return _notice;
    }

    /// <summary>The note owed to <paramref name="slot"/>, once.</summary>
    private GameResults? TakeSlotNotice(PlayerId slot) => _noticeForSlots.Remove(slot) ? _notice : null;

    /// <summary>A new game makes the note stale: whoever hasn't seen it yet won't.</summary>
    private void ForgetNotice()
    {
        _notice = null;
        _noticeForHost = false;
        _noticeForSlots.Clear();
    }
}
