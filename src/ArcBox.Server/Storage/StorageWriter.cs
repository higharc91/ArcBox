using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Storage;

/// <summary>
/// Moves database writes off the game's hot path. Callers queue an operation under a key; while it waits, a newer
/// operation for the same key replaces it, so ten saves of one campaign during a slow disk write become one write of
/// the newest document. Operations must therefore be idempotent and carry the full state they save.
/// </summary>
/// <remarks>
/// A batch is flushed every 250 ms, or at once when 64 operations are pending, and runs in one transaction. Different
/// keys run in the order each was first queued, so a parent row queued before its children is written before them. A
/// batch that fails is retried operation by operation, so one bad write doesn't lose the others. When the host stops,
/// everything still queued is written first.
/// </remarks>
public sealed class StorageWriter : BackgroundService
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);
    public const int FlushAtPending = 64;

    private readonly ArcDb _db;
    private readonly TimeProvider _time;
    private readonly ILogger<StorageWriter> _log;

    private readonly object _lock = new();
    private Dictionary<string, Action<SqliteConnection>> _pending = new(StringComparer.Ordinal);
    private List<string> _order = [];
    private DateTimeOffset _pendingSince;
    private DateTimeOffset? _inFlightSince;
    private TaskCompletionSource _wake = NewWake();

    private readonly SemaphoreSlim _flushing = new(1, 1); // one flush at a time, so a flush never overlaps another
    private long _writesExecuted;

    public StorageWriter(ArcDb db, TimeProvider time, ILogger<StorageWriter> log)
    {
        _db = db;
        _time = time;
        _log = log;
    }

    /// <summary>Queues <paramref name="op"/>; the newest operation per <paramref name="key"/> wins.</summary>
    public void Write(string key, Action<SqliteConnection> op)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(op);
        lock (_lock)
        {
            if (_pending.Count == 0) _pendingSince = _time.GetUtcNow();
            if (!_pending.ContainsKey(key)) _order.Add(key);
            _pending[key] = op;
            if (_pending.Count >= FlushAtPending) _wake.TrySetResult();
        }
    }

    /// <summary>Completes when everything queued before the call has been written (or has failed and been logged).</summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _flushing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Dictionary<string, Action<SqliteConnection>> ops;
            List<string> order;
            lock (_lock)
            {
                if (_pending.Count == 0) return;
                (ops, order) = (_pending, _order);
                _pending = new Dictionary<string, Action<SqliteConnection>>(StringComparer.Ordinal);
                _order = [];
                _inFlightSince = _pendingSince;
            }

            try
            {
                // The operations are synchronous SQLite calls; keep them off the caller's thread.
                await Task.Run(() => Execute(ops, order), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                lock (_lock) _inFlightSince = null;
            }
        }
        finally
        {
            _flushing.Release();
        }
    }

    /// <summary>How long the oldest operation not yet written has been waiting; zero when there is none.</summary>
    public TimeSpan OldestPendingAge
    {
        get
        {
            lock (_lock)
            {
                DateTimeOffset? oldest = _inFlightSince;
                if (_pending.Count > 0 && (oldest is null || _pendingSince < oldest)) oldest = _pendingSince;
                return oldest is { } since ? _time.GetUtcNow() - since : TimeSpan.Zero;
            }
        }
    }

    /// <summary>Operations that have run successfully (a coalesced operation counts once). For tests.</summary>
    public long WritesExecuted => Interlocked.Read(ref _writesExecuted);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                Task wake;
                lock (_lock) wake = _wake.Task;
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var interval = Task.Delay(FlushInterval, _time, tick.Token);
                await Task.WhenAny(interval, wake).ConfigureAwait(false);
                tick.Cancel(); // release the timer if the 64-operation trigger won
                lock (_lock) _wake = NewWake();

                try
                {
                    await FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Storage flush failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        // The loop has ended; write what's left, whatever the token says. By the time the host stops this service,
        // Kestrel may have spent the whole shutdown timeout closing the phones' connections, so the token is often
        // already cancelled: honouring it would drop the last seat changes (and its exception aborted the process). The
        // writes are a few small SQLite statements, so this cannot hold the shutdown up for long.
        await FlushAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private void Execute(Dictionary<string, Action<SqliteConnection>> ops, List<string> order)
    {
        // A connection per flush: opening is cheap, and it leaves nothing for Dispose (or a drain that runs after it) to race with.
        using var connection = _db.Open();
        try
        {
            using var transaction = connection.BeginTransaction();
            foreach (var key in order) ops[key](connection);
            transaction.Commit();
            Interlocked.Add(ref _writesExecuted, order.Count);
            return;
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Storage batch of {Count} failed; retrying one by one", order.Count);
        }

        foreach (var key in order)
        {
            try
            {
                using var transaction = connection.BeginTransaction();
                ops[key](connection);
                transaction.Commit();
                Interlocked.Increment(ref _writesExecuted);
            }
            catch (Exception e)
            {
                _log.LogError(e, "Storage write {Key} failed and was dropped", key);
            }
        }
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
