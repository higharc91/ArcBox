using ArcBox.Server.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Storage;

public class StorageWriterTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static (ArcDb Db, StorageWriter Writer, FakeTimeProvider Time) Create()
    {
        var db = StorageTestSupport.NewMemoryDb();
        db.Execute("CREATE TABLE scratch (k TEXT NOT NULL, v INTEGER NOT NULL)");
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return (db, new StorageWriter(db, time, NullLogger<StorageWriter>.Instance), time);
    }

    private static Action<Microsoft.Data.Sqlite.SqliteConnection> Insert(string k, int v) => connection =>
    {
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO scratch (k, v) VALUES ($k, $v)";
        command.Parameters.AddWithValue("$k", k);
        command.Parameters.AddWithValue("$v", v);
        command.ExecuteNonQuery();
    };

    [Fact]
    public async Task Newest_write_per_key_wins()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        for (var i = 1; i <= 10; i++) writer.Write("c:1", Insert("c:1", i));

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
        Assert.Equal(10, db.Scalar<int>("SELECT v FROM scratch"));
        Assert.Equal(1, writer.WritesExecuted);
    }

    [Fact]
    public async Task Different_keys_run_in_the_order_they_were_first_queued()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        writer.Write("a", Insert("a", 1));
        writer.Write("b", Insert("b", 2));
        writer.Write("a", Insert("a", 3));

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal("a,b", db.Scalar<string>("SELECT group_concat(k) FROM (SELECT k FROM scratch ORDER BY rowid)"));
        Assert.Equal(2, writer.WritesExecuted);
    }

    [Fact]
    public async Task A_failing_write_does_not_lose_the_others()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        writer.Write("a", Insert("a", 1));
        writer.Write("bad", _ => throw new InvalidOperationException("boom"));
        writer.Write("b", Insert("b", 2));

        await writer.FlushAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
        Assert.Equal(2, writer.WritesExecuted);
        Assert.Equal(TimeSpan.Zero, writer.OldestPendingAge);
    }

    [Fact]
    public async Task StopAsync_drains_pending_writes()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        var ct = TestContext.Current.CancellationToken;
        await writer.StartAsync(ct);
        writer.Write("a", Insert("a", 1));
        writer.Write("b", Insert("b", 2));

        await writer.StopAsync(ct);

        Assert.Equal(2, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
    }

    [Fact]
    public async Task StopAsync_flushes_even_when_the_loop_never_ran()
    {
        // With no loop there is no loop-side final flush, so only StopAsync's own drain can write these.
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        writer.Write("a", Insert("a", 1));

        await writer.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
    }

    // Found by the REDLINE restart run (#24): with phones connected, Kestrel can spend the host's whole shutdown timeout
    // closing their connections, so the writer is stopped with a token that is already cancelled. It must still write
    // what is queued, and not throw (an exception there aborted the server process).
    [Fact]
    public async Task StopAsync_with_the_shutdown_timeout_spent_still_drains_and_does_not_throw()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        await writer.StartAsync(TestContext.Current.CancellationToken);
        writer.Write("a", Insert("a", 1));
        writer.Write("b", Insert("b", 2));

        await writer.StopAsync(new CancellationToken(canceled: true));

        Assert.Equal(2, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
    }

    // Spec §4: the drain happens within the shutdown timeout. A stalled disk must not hold the shutdown up past the cap; the
    // writes it gives up on are reported as an Error.
    [Fact]
    public async Task StopAsync_gives_up_on_a_stalled_drain_at_the_cap_and_logs_what_may_be_lost()
    {
        var db = StorageTestSupport.NewMemoryDb();
        using var _db = db;
        db.Execute("CREATE TABLE scratch (k TEXT NOT NULL, v INTEGER NOT NULL)");
        var log = new RecordingLogger();
        using var gate = new ManualResetEventSlim(false);
        var writer = new StorageWriter(db, new FakeTimeProvider(), log) { StopDrainLimit = TimeSpan.FromMilliseconds(200) };
        writer.Write("stuck", _ => gate.Wait(Patience));   // the disk stalls on the first write of the batch
        writer.Write("a", Insert("a", 1));
        writer.Write("b", Insert("b", 2));
        try
        {
            var stop = writer.StopAsync(new CancellationToken(canceled: true));

            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(Patience, TestContext.Current.CancellationToken)));
            await stop;
            var error = Assert.Single(log.Entries, e => e.Level == LogLevel.Error);
            Assert.Contains("3 queued writes may be lost", error.Message);
        }
        finally
        {
            gate.Set();   // let the stalled batch finish before the database goes
            writer.Dispose();
        }
    }

    private sealed class RecordingLogger : ILogger<StorageWriter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (Entries) Entries.Add((logLevel, formatter(state, exception)));
        }
    }

    [Fact]
    public async Task StopAsync_flushes_a_write_queued_after_the_loop_has_exited()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        var ct = TestContext.Current.CancellationToken;
        await writer.StartAsync(ct);
        await writer.StopAsync(ct); // the loop has exited and nothing is pending
        writer.Write("late", Insert("late", 1));

        await writer.StopAsync(ct);

        Assert.Equal(1, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
    }

    [Fact]
    public async Task Disposing_the_writer_mid_flush_does_not_lose_the_batch()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        var ct = TestContext.Current.CancellationToken;
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        writer.Write("a", c =>
        {
            started.Set();
            release.Wait(Patience, ct);
            Insert("a", 1)(c);
        });
        writer.Write("b", Insert("b", 2));
        var flush = writer.FlushAsync(ct);
        Assert.True(started.Wait(Patience, ct));

        writer.Dispose(); // the container disposing while the shutdown drain is mid-batch
        release.Set();
        await flush;

        Assert.Equal(2, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
        Assert.Equal(2, writer.WritesExecuted);
    }

    [Fact]
    public async Task Sixty_four_pending_writes_flush_without_waiting_for_the_timer()
    {
        // The fake clock never advances, so only the 64-operation trigger can wake the loop.
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        var ct = TestContext.Current.CancellationToken;
        await writer.StartAsync(ct);
        for (var i = 0; i < 64; i++) writer.Write($"k{i}", Insert($"k{i}", i));

        var deadline = DateTime.UtcNow + Patience;
        while (writer.WritesExecuted < 64 && DateTime.UtcNow < deadline) await Task.Delay(10, ct);

        Assert.Equal(64, writer.WritesExecuted);
        await writer.StopAsync(ct);
    }

    [Fact]
    public async Task The_timer_flushes_a_small_backlog()
    {
        var (db, writer, time) = Create();
        using var _db = db;
        using var _w = writer;
        var ct = TestContext.Current.CancellationToken;
        await writer.StartAsync(ct);
        writer.Write("a", Insert("a", 1));

        var deadline = DateTime.UtcNow + Patience;
        while (writer.WritesExecuted < 1 && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(10, ct);
        }

        Assert.Equal(1, writer.WritesExecuted);
        await writer.StopAsync(ct);
    }

    [Fact]
    public async Task OldestPendingAge_reports_a_backlog()
    {
        var (db, writer, time) = Create();
        using var _db = db;
        using var _w = writer;
        Assert.Equal(TimeSpan.Zero, writer.OldestPendingAge);

        var ct = TestContext.Current.CancellationToken;
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        writer.Write("slow", _ =>
        {
            started.Set();
            release.Wait(Patience, ct);
        });
        time.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), writer.OldestPendingAge); // queued, not yet running

        var flush = writer.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(started.Wait(Patience, ct));
        time.Advance(TimeSpan.FromSeconds(4));
        Assert.Equal(TimeSpan.FromSeconds(7), writer.OldestPendingAge); // running and blocked

        release.Set();
        await flush;

        Assert.Equal(TimeSpan.Zero, writer.OldestPendingAge);
    }

    [Fact]
    public async Task FlushAsync_waits_for_a_flush_already_in_progress()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        var ct = TestContext.Current.CancellationToken;
        var started = new ManualResetEventSlim();
        var release = new ManualResetEventSlim();
        writer.Write("slow", _ =>
        {
            started.Set();
            release.Wait(Patience, ct);
        });
        var first = writer.FlushAsync(TestContext.Current.CancellationToken);
        Assert.True(started.Wait(Patience, ct));
        writer.Write("late", Insert("late", 1));

        var second = writer.FlushAsync(TestContext.Current.CancellationToken);
        Assert.False(second.IsCompleted);
        release.Set();
        await Task.WhenAll(first, second);

        Assert.Equal(1, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));
    }

    // ---- A failing disk (the chapter e2e that hung after "disk I/O error" flushes on a full disk) ------------------------

    /// <summary>A disk that fails while <see cref="Broken"/>, as SQLite says it: error 10, "disk I/O error", from the open.</summary>
    internal sealed class FailingDisk(ArcDb db)
    {
        public bool Broken { get; set; }
        public int Tries { get; private set; }

        public Microsoft.Data.Sqlite.SqliteConnection Open()
        {
            Tries++;
            if (Broken) throw new Microsoft.Data.Sqlite.SqliteException("SQLite Error 10: 'disk I/O error'.", 10);
            return db.Open();
        }
    }

    [Fact]
    public async Task A_failing_disk_keeps_the_newest_writes_and_writes_them_when_it_recovers()
    {
        var (db, _, time) = Create();
        using var _db = db;
        var disk = new FailingDisk(db) { Broken = true };
        var log = new RecordingLogger();
        using var writer = new StorageWriter(db, time, log) { Connect = disk.Open };
        var ct = TestContext.Current.CancellationToken;
        writer.Write("a", Insert("a", 1));
        writer.Write("b", Insert("b", 2));

        await writer.FlushAsync(ct);            // never throws for the storage
        Assert.True(writer.Failing);
        Assert.Equal(0, writer.WritesExecuted);
        time.Advance(TimeSpan.FromSeconds(12));
        Assert.Equal(TimeSpan.FromSeconds(12), writer.OldestPendingAge);    // kept, and the health check sees how long for
        writer.Write("a", Insert("a", 3));      // a newer save of a, meanwhile: it replaces the one kept
        writer.Write("c", Insert("c", 4));
        await writer.FlushAsync(ct);
        Assert.Equal(0, db.Scalar<int>("SELECT COUNT(*) FROM scratch"));

        disk.Broken = false;
        await writer.FlushAsync(ct);

        Assert.False(writer.Failing);
        Assert.Equal("a:3,b:2,c:4", db.Scalar<string>("SELECT group_concat(k || ':' || v) FROM (SELECT k, v FROM scratch ORDER BY rowid)"));
        Assert.Equal(TimeSpan.Zero, writer.OldestPendingAge);
        Assert.Equal(3, disk.Tries);
        // Said clearly: an error when it starts, a warning for each retry that fails, and the recovery.
        Assert.Equal([LogLevel.Error, LogLevel.Warning, LogLevel.Warning], log.Entries.Select(e => e.Level));
        Assert.Contains("Storage is failing: 2 writes are kept", log.Entries[0].Message);
        Assert.Contains("Storage still failing after 2 tries: 3 writes kept", log.Entries[1].Message);
        Assert.Contains("Storage recovered after 2 failed tries", log.Entries[2].Message);
    }

    [Fact]
    public async Task A_write_the_disk_fails_mid_batch_is_kept_and_one_that_fails_on_its_own_is_dropped()
    {
        var (db, _, time) = Create();
        using var _db = db;
        using var writer = new StorageWriter(db, time, NullLogger<StorageWriter>.Instance);
        var ct = TestContext.Current.CancellationToken;
        var full = true;
        writer.Write("a", Insert("a", 1));
        writer.Write("full", c =>
        {
            if (full) throw new Microsoft.Data.Sqlite.SqliteException("SQLite Error 13: 'database or disk is full'.", 13);
            Insert("full", 2)(c);
        });
        writer.Write("bad", _ => throw new InvalidOperationException("boom"));

        await writer.FlushAsync(ct);
        Assert.True(writer.Failing);
        Assert.Equal(1, writer.WritesExecuted);

        full = false;
        await writer.FlushAsync(ct);
        Assert.False(writer.Failing);
        Assert.Equal("a,full", db.Scalar<string>("SELECT group_concat(k) FROM (SELECT k FROM scratch ORDER BY rowid)"));
    }

    [Fact]
    public void The_back_off_doubles_from_half_a_second_to_thirty()
    {
        Assert.Equal([0.5, 1, 2, 4, 8, 16, 30, 30], Enumerable.Range(1, 8).Select(n => StorageWriter.RetryAfter(n).TotalSeconds));
        Assert.Equal(StorageWriter.RetryMost, StorageWriter.RetryAfter(1_000));
        Assert.True(StorageWriter.IsStorageFault(new Microsoft.Data.Sqlite.SqliteException("disk I/O error", 10)));
        Assert.True(StorageWriter.IsStorageFault(new IOException("No space left on device")));
        Assert.False(StorageWriter.IsStorageFault(new Microsoft.Data.Sqlite.SqliteException("constraint failed", 19)));
        Assert.False(StorageWriter.IsStorageFault(new InvalidOperationException("boom")));
    }

    /// <summary>The loop, on a disk that's out for a simulated minute: it backs off rather than trying every 250 ms, then writes.</summary>
    [Fact]
    public async Task The_loop_backs_off_while_the_disk_fails_and_flushes_once_it_recovers()
    {
        var (db, _, time) = Create();
        using var _db = db;
        var disk = new FailingDisk(db) { Broken = true };
        using var writer = new StorageWriter(db, time, NullLogger<StorageWriter>.Instance) { Connect = disk.Open };
        var ct = TestContext.Current.CancellationToken;
        await writer.StartAsync(ct);
        writer.Write("a", Insert("a", 1));

        for (var i = 0; i < 240; i++)           // a minute in steps of 250 ms
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(2, ct);
        }
        Assert.True(writer.Failing);
        Assert.InRange(disk.Tries, 2, 12);      // 0.25 + 0.5 + 1 + 2 + 4 + 8 + 16 + 30 s: about 8, against 240 without the back-off

        disk.Broken = false;
        var deadline = DateTime.UtcNow + Patience;
        while (writer.WritesExecuted < 1 && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMilliseconds(250));
            await Task.Delay(5, ct);
        }
        Assert.Equal(1, writer.WritesExecuted);
        Assert.False(writer.Failing);
        await writer.StopAsync(ct);
    }

    [Fact]
    public async Task A_save_queued_while_the_disk_fails_wins_over_the_one_kept()
    {
        var (db, writer, _) = Create();
        using var _db = db;
        using var _w = writer;
        var ct = TestContext.Current.CancellationToken;
        writer.Write("c:1", _ =>
        {
            writer.Write("c:1", Insert("c:1", 2));      // a newer save, mid-flush
            throw new Microsoft.Data.Sqlite.SqliteException("SQLite Error 10: 'disk I/O error'.", 10);
        });

        await writer.FlushAsync(ct);
        await writer.FlushAsync(ct);

        Assert.Equal(2, db.Scalar<int>("SELECT v FROM scratch"));
        Assert.False(writer.Failing);
    }
}
