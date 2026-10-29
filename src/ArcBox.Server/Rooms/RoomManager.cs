using System.Collections.Concurrent;
using ArcBox.Sdk.Protocol;

namespace ArcBox.Server.Rooms;

public sealed class RoomManager : IAsyncDisposable
{
    public const string ClockKey = "rooms";

    private readonly RoomServices _services;
    private readonly ILogger<RoomManager> _logger;

    private readonly ConcurrentDictionary<string, RoomEntry> _rooms = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Room> _connections = new();

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

        var code = RoomCodes.Generate(c => _rooms.ContainsKey(c));
        var hostToken = Secrets.NewToken();

        var room = new Room(code, hostToken, _services);
        var cts = new CancellationTokenSource();
        var runTask = Task.Run(() => room.RunAsync(cts.Token));

        var entry = new RoomEntry(room, cts, runTask);
        if (!_rooms.TryAdd(code, entry))
        {
            cts.Cancel();
            throw new RoomException("Server is full, try again later.");
        }

        try
        {
            await room.BindHostAsync(hostConnectionId, hostToken);
            Track(hostConnectionId, room);
        }
        catch
        {
            RemoveRoom(code);
            throw;
        }

        return (room, new CreateRoomResult(code, hostToken));
    }

    public Room? Find(string rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode)) return null;
        var code = RoomCodes.Normalize(rawCode);
        return _rooms.TryGetValue(code, out var entry) ? entry.Room : null;
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

            // Watchdog check: without enqueueing
            var busySince = room.BusySince;
            if (busySince.HasValue && (now - busySince.Value) > _services.Options.GameCallTimeout)
            {
                _logger.LogWarning("Room {Code} hung; force closing.", room.Code);
                room.ForceClose("game stopped responding");
                RemoveRoom(room.Code);
                continue;
            }

            // Expiry check
            bool expired = false;
            try
            {
                expired = await room.IsExpiredAsync().WaitAsync(TimeSpan.FromSeconds(1), _services.Time);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Timeout checking expiry for room {Code}", room.Code);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking expiry for room {Code}", room.Code);
            }

            if (expired)
            {
                room.ForceClose("Room closed after being idle.");
                RemoveRoom(room.Code);
            }
        }
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

    private void RemoveRoom(string code)
    {
        if (_rooms.TryRemove(code, out var entry))
        {
            foreach (var kvp in _connections)
            {
                if (kvp.Value.Code == code)
                {
                    _connections.TryRemove(kvp.Key, out _);
                }
            }

            entry.Cts.Cancel();
        }
    }

    private sealed record RoomEntry(Room Room, CancellationTokenSource Cts, Task RunTask);
}
