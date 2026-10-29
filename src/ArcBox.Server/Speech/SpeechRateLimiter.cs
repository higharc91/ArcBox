using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using ArcBox.Server.Rooms;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Speech;

/// <summary>
/// <c>RequestsPerMinutePerRoom</c> for <c>/api/tts</c>: one sliding-window limiter (one minute, six segments) per live
/// room.
/// </summary>
/// <remarks>
/// Limiters are keyed by room code and remember which <see cref="Room"/> instance they belong to. Every call drops the
/// limiters of rooms that are gone (the room manager no longer returns that instance for the code), so there are never
/// more than the live rooms plus those closed since the last call, and a new room that reuses a code starts fresh.
/// </remarks>
public sealed class SpeechRateLimiter : IDisposable
{
    private readonly ConcurrentDictionary<string, Entry> _limiters = new(StringComparer.Ordinal);
    private readonly Func<string, object?> _currentRoom;
    private readonly int _perMinute;
    private readonly Lock _gate = new();
    private bool _disposed;

    public SpeechRateLimiter(RoomManager rooms, IOptions<SpeechOptions> options)
        : this(code => rooms.FindLoaded(code), options.Value.RequestsPerMinutePerRoom)
    {
    }

    /// <param name="currentRoom">The live room object for a code, or null; compared by reference.</param>
    public SpeechRateLimiter(Func<string, object?> currentRoom, int perMinute)
    {
        _currentRoom = currentRoom;
        _perMinute = perMinute;
    }

    /// <summary>Limiters currently held (for tests and diagnostics).</summary>
    public int Count => _limiters.Count;

    /// <summary>Takes one permit for <paramref name="room"/>; false when it is over the limit. <paramref name="retryAfter"/> is a hint.</summary>
    public bool TryAcquire(string code, object room, out TimeSpan retryAfter)
    {
        // One lock for lookup, pruning and the acquire itself: all cheap, and no limiter is used after disposal.
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            Prune();
            if (!_limiters.TryGetValue(code, out var entry) || !ReferenceEquals(entry.Room, room))
            {
                entry?.Limiter.Dispose();
                entry = new Entry(room, Create());
                _limiters[code] = entry;
            }

            using var lease = entry.Limiter.AttemptAcquire();
            retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var hint) ? hint : TimeSpan.FromSeconds(10);
            return lease.IsAcquired;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var entry in _limiters.Values)
                entry.Limiter.Dispose();
            _limiters.Clear();
        }
    }

    // Under _gate.
    private void Prune()
    {
        foreach (var (code, entry) in _limiters)
        {
            if (!ReferenceEquals(_currentRoom(code), entry.Room) && _limiters.TryRemove(code, out var removed))
                removed.Limiter.Dispose();
        }
    }

    private SlidingWindowRateLimiter Create() => new(new SlidingWindowRateLimiterOptions
    {
        PermitLimit = _perMinute,
        Window = TimeSpan.FromMinutes(1),
        SegmentsPerWindow = 6,
        QueueLimit = 0,
        AutoReplenishment = true,
    });

    private sealed record Entry(object Room, SlidingWindowRateLimiter Limiter);
}
