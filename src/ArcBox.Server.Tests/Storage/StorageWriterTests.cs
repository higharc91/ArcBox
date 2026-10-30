using ArcBox.Server.Storage;
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
}
