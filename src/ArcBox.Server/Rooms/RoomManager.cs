using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Server.Rooms;

/// <summary>
/// Owns the rooms in memory. A room that isn't in memory but has a stored record within
/// <see cref="RoomOptions.RetainFor"/> is rebuilt the first time it is looked up (spec §3.5).
/// </summary>
public sealed class RoomManager : IAsyncDisposable
{
    public const string ClockKey = "rooms";

    private readonly RoomServices _services;
    private readonly ILogger<RoomManager> _logger;

    private readonly ConcurrentDictionary<string, RoomEntry> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Room> _connections = new();

    // Rebuilds in progress. Every lookup that misses memory for a code at the same time awaits the same one.
    private readonly ConcurrentDictionary<string, Lazy<Task<RoomEntry?>>> _rebuilding = new(StringComparer.OrdinalIgnoreCase);

    public RoomManager(RoomServices services)
    {
        _services = services;
        _logger = services.Loggers.CreateLogger<RoomManager>();
    }

    public int Count => _rooms.Count;

    public async Task<(Room Room, CreateRoomResult Result)> CreateRoomAsync(string hostConnectionId)
    {
        if (_rooms.Count >= _services.Options.MaxRooms)
        {
            throw new RoomException("Server is full, try again later.");
        }

        // A stored room may come back, so its code isn't handed out again while its row is kept.
        var code = RoomCodes.Generate(c => _rooms.ContainsKey(c) || _services.Store.Exists(c));
        var hostToken = Secrets.NewToken();

        var room = new Room(code, hostToken, _services);
        var entry = Run(room);
        if (!_rooms.TryAdd(code, entry))
        {
            entry.Cts.Cancel();
            throw new RoomException("Server is full, try again later.");
        }

        try
        {
            await room.SaveAsync();
            await room.BindHostAsync(hostConnectionId, hostToken);
            Track(hostConnectionId, room);
        }
        catch
        {
            RemoveRoom(entry);
            _services.Store.Delete(code);
            throw;
        }

        return (room, new CreateRoomResult(code, hostToken));
    }

    /// <summary>The room with this code: from memory, else rebuilt from its stored record. Null if there is neither.</summary>
    public async Task<Room?> FindAsync(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return null;
        var code = RoomCodes.Normalize(rawCode);
        if (TryGetLive(code, out var live))
        {
            return live.Room;
        }

        var rebuild = _rebuilding.GetOrAdd(code, c => new Lazy<Task<RoomEntry?>>(() => Task.Run(() => Rebuild(c))));
        try
        {
            return (await rebuild.Value)?.Room;
        }
        finally
        {
            // Only after the room is in _rooms (or known not to exist), so a later miss finds it there.
            _rebuilding.TryRemove(new KeyValuePair<string, Lazy<Task<RoomEntry?>>>(code, rebuild));
        }
    }

    /// <summary>
    /// The room with this code if it is in memory; never rebuilds one. For bookkeeping that follows live rooms, such
    /// as the speech rate limiter; callers acting for a client use <see cref="FindAsync"/>.
    /// </summary>
    public Room? FindLoaded(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return null;
        return _rooms.TryGetValue(RoomCodes.Normalize(rawCode), out var entry) ? entry.Room : null;
    }

    public void Track(string connectionId, Room room)
    {
        _connections[connectionId] = room;
    }

    public Room? RoomFor(string connectionId)
    {
        return _connections.TryGetValue(connectionId, out var room) ? room : null;
    }

    public void Untrack(string connectionId)
    {
        _connections.TryRemove(connectionId, out _);
    }

    public async Task SweepAsync()
    {
        var now = _services.Time.GetUtcNow();
        var entries = _rooms.Values.ToList();

        foreach (var entry in entries)
        {
            var room = entry.Room;

            // Parked by an earlier sweep whose wait for it timed out.
            if (room.IsClosed)
            {
                RemoveRoom(entry);
                continue;
            }

            // Watchdog check: without enqueueing. A room whose game hung doesn't come back, so its row goes too.
            var busySince = room.BusySince;
            if (busySince.HasValue && (now - busySince.Value) > _services.Options.GameCallTimeout)
            {
                _logger.LogWarning("Room {Code} hung; force closing.", room.Code);
                // The row goes before the room closes: a lookup treats a closed room as a miss and rebuilds from the row,
                // so it must already be gone. And again after, in case the actor came unstuck and saved in between.
                _services.Store.Delete(room.Code);
                room.ForceClose("game stopped responding");
                _services.Store.Delete(room.Code);
                RemoveRoom(entry);
                continue;
            }

            // Idle rooms are parked: out of memory, row kept, campaign given back.
            bool parked = false;
            try
            {
                parked = await room.TryParkAsync().WaitAsync(TimeSpan.FromSeconds(1), _services.Time);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timeout checking expiry for room {Code}", room.Code);
            }
            catch (RoomException) when (room.IsClosed)
            {
                // Closed meanwhile (parked by a concurrent sweep, or force closed); removed below.
                parked = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking expiry for room {Code}", room.Code);
            }

            if (parked)
            {
                RemoveRoom(entry);
            }
        }
    }

    /// <summary>
    /// Deletes stored rooms last saved more than <see cref="RoomOptions.RetainFor"/> ago; returns how many. A room still
    /// in memory may have gone that long without a change (a TV left on in the lobby), so any that has gone half as long
    /// saves itself again first.
    /// </summary>
    public async Task<int> DeleteExpiredRowsAsync()
    {
        var now = _services.Time.GetUtcNow();
        var stale = _rooms.Values
            .Select(e => e.Room)
            .Where(r => !r.IsClosed && (r.SavedUtc is not { } saved || now - saved > _services.Options.RetainFor / 2))
            .ToList();

        foreach (var room in stale)
        {
            try
            {
                await room.SaveAsync().WaitAsync(TimeSpan.FromSeconds(1), _services.Time);
            }
            catch (Exception ex) when (ex is TimeoutException or RoomException)
            {
                // Hung (the watchdog deals with it) or closed meanwhile.
            }
        }

        return _services.Store.DeleteOlderThan(now - _services.Options.RetainFor);
    }

    public async ValueTask DisposeAsync()
    {
        var entries = _rooms.Values.ToList();
        _rooms.Clear();
        _connections.Clear();

        foreach (var entry in entries)
        {
            entry.Cts.Cancel();
        }

        foreach (var entry in entries)
        {
            try
            {
                await entry.RunTask;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }
    }

    /// <summary>
    /// The room in memory under <paramref name="code"/>, if it is still open. A closed one (parked or force closed, and
    /// not yet swept) is a miss: that exact entry is taken out, so a lookup rebuilds from storage instead of handing out
    /// a room that can only answer "Room has closed.".
    /// </summary>
    private bool TryGetLive(string code, [NotNullWhen(true)] out RoomEntry? entry)
    {
        if (_rooms.TryGetValue(code, out entry))
        {
            if (!entry.Room.IsClosed)
            {
                return true;
            }

            RemoveRoom(entry);
        }

        entry = null;
        return false;
    }

    private RoomEntry? Rebuild(string code)
    {
        // Another lookup may have finished rebuilding it just before this one began.
        if (TryGetLive(code, out var live))
        {
            return live;
        }

        var record = _services.Store.Load(code);
        if (record is null || record.UpdatedUtc < _services.Time.GetUtcNow() - _services.Options.RetainFor)
        {
            return null;
        }

        var entry = Run(Room.Rebuild(record, _services));
        if (!_rooms.TryAdd(code, entry))
        {
            entry.Cts.Cancel();
            return _rooms.TryGetValue(code, out live) ? live : null;
        }

        _logger.LogInformation("Room {Code} rebuilt from storage", code);
        return entry;
    }

    private static RoomEntry Run(Room room)
    {
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => room.RunAsync(cts.Token));
        return new RoomEntry(room, cts, runTask);
    }

    /// <summary>Takes the room out of memory. Its run loop ends, and on the way out gives back any campaign it holds.</summary>
    private void RemoveRoom(RoomEntry entry)
    {
        var code = entry.Room.Code;
        if (_rooms.TryRemove(new KeyValuePair<string, RoomEntry>(code, entry)))
        {
            foreach (var kvp in _connections)
            {
                if (kvp.Value == entry.Room)
                {
                    _connections.TryRemove(kvp.Key, out _);
                }
            }

            entry.Cts.Cancel();
        }
    }

    private sealed record RoomEntry(Room Room, CancellationTokenSource Cts, Task RunTask);
}
