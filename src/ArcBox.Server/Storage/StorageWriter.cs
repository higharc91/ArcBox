using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Storage;

/// <summary>
/// Moves database writes off the game's hot path. Callers queue an operation under a key; while it waits, a newer
/// operation for the same key replaces it, so ten saves of one campaign during a slow disk write become one write of
/// the newest document. Operations must therefore be idempotent and carry the full state they save.
/// </summary>
/// <remarks>
/// <para>
/// A batch is flushed every 250 ms, or at once when 64 operations are pending, and runs in one transaction. Different
/// keys run in the order each was first queued, so a parent row queued before its children is written before them. A
/// batch that fails is retried operation by operation, so one bad write doesn't lose the others. When the host stops,
/// everything still queued is written first.
/// </para>
/// <para>
/// A write that fails because of the storage itself (the database won't open, the disk is full or failing, the file is
/// locked: <see cref="IsStorageFault"/>) is never dropped: it goes back in the queue, unless a newer one for its key has
/// come since, and the writer tries again after a back-off that doubles from <see cref="RetryFirst"/> to
/// <see cref="RetryMost"/>. The first failure of a run is logged as an error, each retry as a warning, and the recovery once
/// it writes again. A write that fails on its own (its SQL, a constraint) is dropped and logged, as before. Nobody waits for
/// any of this: <see cref="Write"/> only queues, so a failing disk never holds up a room.
/// </para>
/// </remarks>
public sealed class StorageWriter : BackgroundService
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromMilliseconds(250);
    public const int FlushAtPending = 64;

    /// <summary>
    /// How long <see cref="StopAsync"/> waits for the last writes, whether or not the host's token is already cancelled.
    /// 4 s, inside the host's 5 s <c>HostOptions.ShutdownTimeout</c> (spec §4: the drain happens within the shutdown
    /// timeout). Wall-clock time, not the injected clock: it bounds the real shutdown. Settable so tests can use a short one.
    /// </summary>
    public TimeSpan StopDrainLimit { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>The wait before the first retry after the storage fails; it doubles with each failure after that.</summary>
    public static readonly TimeSpan RetryFirst = TimeSpan.FromMilliseconds(500);

    /// <summary>The longest wait between retries while the storage keeps failing.</summary>
    public static readonly TimeSpan RetryMost = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How a flush gets its connection: <see cref="ArcDb.Open"/> unless set. Settable so tests can stand in a disk that fails.
    /// </summary>
    public Func<SqliteConnection>? Connect { get; init; }

    private readonly ArcDb _db;
    private readonly TimeProvider _time;
    private readonly ILogger<StorageWriter> _log;

    private readonly object _lock = new();
    private Dictionary<string, Action<SqliteConnection>> _pending = new(StringComparer.Ordinal);
    private List<string> _order = [];
    private DateTimeOffset _pendingSince;
    private DateTimeOffset? _inFlightSince;
    private int _inFlightCount;
    private TaskCompletionSource _wake = NewWake();

    private readonly SemaphoreSlim _flushing = new(1, 1); // one flush at a time, so a flush never overlaps another
    private long _writesExecuted;

    private int _failures;                   // flushes in a row the storage has failed
    private DateTimeOffset? _failingSince;   // when the first of them did
    private DateTimeOffset? _retryAt;        // when the loop tries again; null while the storage is fine

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

    /// <summary>
    /// Completes when everything queued before the call has been tried once: written, dropped and logged for a fault of its own,
    /// or kept for a retry when the storage failed (<see cref="Failing"/>). It never throws for the storage.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _flushing.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Dictionary<string, Action<SqliteConnection>> ops;
            List<string> order;
            DateTimeOffset since;
            lock (_lock)
            {
                if (_pending.Count == 0) return;
                (ops, order) = (_pending, _order);
                _pending = new Dictionary<string, Action<SqliteConnection>>(StringComparer.Ordinal);
                _order = [];
                _inFlightSince = since = _pendingSince;
                _inFlightCount = order.Count;
            }

            var kept = new List<string>();
            Exception? fault = null;
            try
            {
                // The operations are synchronous SQLite calls; keep them off the caller's thread.
                (kept, fault) = await Task.Run(() => Execute(ops, order), CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                lock (_lock)
                {
                    (_inFlightSince, _inFlightCount) = (null, 0);
                    Requeue(ops, kept, since);
                }
            }
            Settle(kept.Count, order.Count - kept.Count, fault);
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

    /// <summary>
    /// Puts the writes the storage failed back at the front of the queue, in their order, each unless a newer one for its key has
    /// been queued since. Under <see cref="_lock"/>.
    /// </summary>
    private void Requeue(Dictionary<string, Action<SqliteConnection>> ops, List<string> kept, DateTimeOffset since)
    {
        if (kept.Count == 0) return;
        if (_pending.Count == 0 || since < _pendingSince) _pendingSince = since;
        foreach (var key in kept) _pending.TryAdd(key, ops[key]);
        _order = [.. kept, .. _order.Where(k => !kept.Contains(k))];
    }

    /// <summary>After a flush: on a failure, when to try again, and what to log; on a success after failures, the recovery.</summary>
    private void Settle(int kept, int written, Exception? fault)
    {
        var now = _time.GetUtcNow();
        if (kept > 0)
        {
            int failures;
            TimeSpan wait;
            lock (_lock)
            {
                failures = ++_failures;
                _failingSince ??= now;
                wait = RetryAfter(failures);
                _retryAt = now + wait;
            }
            if (failures == 1)
                _log.LogError(fault, "Storage is failing: {Kept} writes are kept and will be retried in {Wait} ({Written} written)", kept, wait, written);
            else
                _log.LogWarning("Storage still failing after {Failures} tries: {Kept} writes kept, retrying in {Wait}: {Fault}",
                    failures, kept, wait, fault?.Message);
            return;
        }
        int was;
        DateTimeOffset? from;
        lock (_lock)
        {
            (was, from) = (_failures, _failingSince);
            (_failures, _failingSince, _retryAt) = (0, null, null);
        }
        if (was > 0)
            _log.LogWarning("Storage recovered after {Failures} failed tries over {Down}: {Written} writes written", was, now - from!.Value, written);
    }

    /// <summary>The wait before retry <paramref name="failures"/> (1 for the first): <see cref="RetryFirst"/>, doubling, at most <see cref="RetryMost"/>.</summary>
    public static TimeSpan RetryAfter(int failures)
        => TimeSpan.FromTicks(Math.Min(RetryMost.Ticks, RetryFirst.Ticks << Math.Clamp(failures - 1, 0, 20)));

    /// <summary>
    /// True when <paramref name="e"/> is the storage's fault rather than the write's: SQLite's busy, locked, out of memory,
    /// read-only, I/O, full, can't-open and protocol errors, and any I/O exception. Those writes are kept and retried.
    /// </summary>
    public static bool IsStorageFault(Exception e) => e switch
    {
        SqliteException sqlite => sqlite.SqliteErrorCode is 5 or 6 or 7 or 8 or 10 or 13 or 14 or 15,
        IOException or UnauthorizedAccessException => true,
        _ => false,
    };

    /// <summary>True while the storage is failing and the writer is waiting to try again.</summary>
    public bool Failing
    {
        get
        {
            lock (_lock) return _failures > 0;
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

                // While the storage is failing, wait out the back-off: the writes are kept, and newer ones replace them meanwhile.
                DateTimeOffset? retryAt;
                lock (_lock) retryAt = _retryAt;
                var wait = retryAt is { } at ? at - _time.GetUtcNow() : TimeSpan.Zero;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, _time, stoppingToken).ConfigureAwait(false);

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
        // The loop has ended; write what's left, for up to StopDrainLimit whatever the token says. By the time the host
        // stops this service, Kestrel may have spent the whole shutdown timeout closing the phones' connections, so the
        // token is often already cancelled: honouring it would drop the last seat changes without trying (and its
        // exception aborted the process). The cap keeps a stalled disk from holding the shutdown up for good.
        var drain = FlushAsync(CancellationToken.None);
        try
        {
            await drain.WaitAsync(StopDrainLimit).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // The drain carries on in the background until the process exits; observe a late failure so it isn't unobserved.
            _ = drain.ContinueWith(t => _log.LogError(t.Exception, "Storage drain failed after the writer stopped"),
                CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
            int unwritten;
            lock (_lock) unwritten = _inFlightCount + _pending.Count;
            _log.LogError("Storage writer stopped after waiting {Limit} for its last writes: {Unwritten} queued writes may be lost",
                StopDrainLimit, unwritten);
            return;
        }
        int left;
        lock (_lock) left = _pending.Count;
        if (left > 0)
            _log.LogError("Storage writer stopped with the storage failing: {Unwritten} queued writes are lost", left);
    }

    /// <summary>
    /// Runs the batch, and then any that failed one by one. Gives back the keys the storage failed, in order, to be kept, and
    /// the fault that did it; a write that failed on its own is logged and dropped.
    /// </summary>
    private (List<string> Kept, Exception? Fault) Execute(Dictionary<string, Action<SqliteConnection>> ops, List<string> order)
    {
        // A connection per flush: opening is cheap, and it leaves nothing for Dispose (or a drain that runs after it) to race with.
        SqliteConnection connection;
        try
        {
            connection = Connect?.Invoke() ?? _db.Open();
        }
        catch (Exception e)
        {
            return ([.. order], e);     // no database at all: everything waits for it
        }
        using var _ = connection;
        try
        {
            using var transaction = connection.BeginTransaction();
            foreach (var key in order) ops[key](connection);
            transaction.Commit();
            Interlocked.Add(ref _writesExecuted, order.Count);
            return ([], null);
        }
        catch (Exception e)
        {
            _log.LogWarning(e, "Storage batch of {Count} failed; retrying one by one", order.Count);
        }

        var kept = new List<string>();
        Exception? fault = null;
        foreach (var key in order)
        {
            try
            {
                using var transaction = connection.BeginTransaction();
                ops[key](connection);
                transaction.Commit();
                Interlocked.Increment(ref _writesExecuted);
            }
            catch (Exception e) when (IsStorageFault(e))
            {
                kept.Add(key);
                fault ??= e;
            }
            catch (Exception e)
            {
                _log.LogError(e, "Storage write {Key} failed and was dropped", key);
            }
        }
        return (kept, fault);
    }

    private static TaskCompletionSource NewWake() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
