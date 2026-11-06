using System.Threading.Channels;
using ArcBox.Server.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Storage;

public sealed class BackupServiceTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("arcbox-backup-");

    public void Dispose()
    {
        try { _dir.Delete(true); } catch (IOException) { }
    }

    private static BackupService NewService(ArcDb db, StorageOptions options, FakeTimeProvider? time = null) =>
        new(db, Options.Create(options), time ?? new FakeTimeProvider(), NullLogger<BackupService>.Instance);

    [Fact]
    public async Task RunOnce_writes_dated_copy_and_keeps_14()
    {
        var options = StorageTestSupport.FileOptions(_dir.FullName);
        using var db = StorageTestSupport.NewDb(options);
        db.Execute("INSERT INTO profiles (id, display_name, created_utc, last_seen_utc) VALUES ('pr_a', 'Ann', 't', 't')");
        var backups = Path.Combine(_dir.FullName, "backups");
        Directory.CreateDirectory(backups);
        for (var day = 1; day <= 15; day++)
            File.WriteAllText(Path.Combine(backups, $"arcbox-2026-01-{day:00}.db"), "old");
        File.WriteAllText(Path.Combine(backups, "notes.txt"), "not a backup");

        await NewService(db, options).RunOnceAsync(new DateTimeOffset(2026, 1, 20, 4, 0, 0, TimeSpan.Zero));

        var kept = Directory.GetFiles(backups, "arcbox-*.db").Select(Path.GetFileName).Order().ToList();
        Assert.Equal(14, kept.Count);
        Assert.Equal("arcbox-2026-01-20.db", kept[^1]);
        Assert.Equal("arcbox-2026-01-03.db", kept[0]); // the two oldest, 01 and 02, are gone
        Assert.True(File.Exists(Path.Combine(backups, "notes.txt")));
        Assert.Empty(Directory.GetFiles(backups, "*.tmp"));

        using var copy = new SqliteConnection($"Data Source={Path.Combine(backups, "arcbox-2026-01-20.db")};Mode=ReadOnly;Pooling=False");
        copy.Open();
        using var count = copy.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM profiles";
        Assert.Equal(1L, count.ExecuteScalar());
    }

    [Fact]
    public async Task RunOnce_twice_in_a_day_replaces_that_days_copy()
    {
        var options = StorageTestSupport.FileOptions(_dir.FullName);
        using var db = StorageTestSupport.NewDb(options);
        var service = NewService(db, options);
        var now = new DateTimeOffset(2026, 1, 20, 4, 0, 0, TimeSpan.Zero);
        await service.RunOnceAsync(now);
        db.Execute("INSERT INTO profiles (id, display_name, created_utc, last_seen_utc) VALUES ('pr_a', 'Ann', 't', 't')");

        await service.RunOnceAsync(now.AddHours(1));

        using var copy = new SqliteConnection($"Data Source={Path.Combine(_dir.FullName, "backups", "arcbox-2026-01-20.db")};Mode=ReadOnly;Pooling=False");
        copy.Open();
        using var count = copy.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM profiles";
        Assert.Equal(1L, count.ExecuteScalar());
    }

    [Fact]
    public async Task In_memory_db_is_skipped()
    {
        var options = new StorageOptions
        {
            ConnectionString = $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared",
            BackupDirectory = Path.Combine(_dir.FullName, "backups"),
        };
        using var db = StorageTestSupport.NewDb(options);
        Assert.True(db.IsInMemory);

        await NewService(db, options).RunOnceAsync(new DateTimeOffset(2026, 1, 20, 4, 0, 0, TimeSpan.Zero));

        Assert.False(Directory.Exists(Path.Combine(_dir.FullName, "backups")));
    }

    [Fact]
    public async Task Service_runs_daily_at_0400_utc()
    {
        var options = StorageTestSupport.FileOptions(_dir.FullName);
        using var db = StorageTestSupport.NewDb(options);
        var time = new TimerWatch(new FakeTimeProvider(new DateTimeOffset(2026, 1, 20, 3, 0, 0, TimeSpan.Zero)));
        var service = new BackupService(db, Options.Create(options), time, NullLogger<BackupService>.Instance);
        var ct = TestContext.Current.CancellationToken;
        var backups = Path.Combine(_dir.FullName, "backups");
        await service.StartAsync(ct);
        try
        {
            // Each wait is for the service's own timer, so the clock never moves before the service is waiting on it.
            Assert.Equal(TimeSpan.FromHours(1), await time.NextTimer(ct));
            Assert.False(Directory.Exists(backups)); // not yet 04:00

            time.Fake.Advance(TimeSpan.FromHours(1));
            Assert.Equal(TimeSpan.FromHours(24), await time.NextTimer(ct));
            Assert.True(File.Exists(Path.Combine(backups, "arcbox-2026-01-20.db")));
            Assert.False(File.Exists(Path.Combine(backups, "arcbox-2026-01-21.db")));

            time.Fake.Advance(TimeSpan.FromHours(23));
            Assert.False(File.Exists(Path.Combine(backups, "arcbox-2026-01-21.db"))); // 03:00 the next day

            time.Fake.Advance(TimeSpan.FromHours(1));
            Assert.Equal(TimeSpan.FromHours(24), await time.NextTimer(ct));
            Assert.True(File.Exists(Path.Combine(backups, "arcbox-2026-01-21.db")));
        }
        finally
        {
            await service.StopAsync(ct);
        }
    }

    /// <summary>
    /// A fake clock that says when a timer is made, and for how long: the service makes one each time it starts to wait, after the backup
    /// before it is written, so a test can wait for that rather than for the wall clock.
    /// </summary>
    private sealed class TimerWatch(FakeTimeProvider fake) : TimeProvider
    {
        private readonly Channel<TimeSpan> _made = Channel.CreateUnbounded<TimeSpan>();

        public FakeTimeProvider Fake => fake;

        public override DateTimeOffset GetUtcNow() => fake.GetUtcNow();

        public override long GetTimestamp() => fake.GetTimestamp();

        public override long TimestampFrequency => fake.TimestampFrequency;

        public override TimeZoneInfo LocalTimeZone => fake.LocalTimeZone;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = fake.CreateTimer(callback, state, dueTime, period);
            _made.Writer.TryWrite(dueTime);
            return timer;
        }

        /// <summary>The due time of the next timer made. Only a hang is timed out, never an answer.</summary>
        public async Task<TimeSpan> NextTimer(CancellationToken ct)
        {
            using var hang = CancellationTokenSource.CreateLinkedTokenSource(ct);
            hang.CancelAfter(TimeSpan.FromMinutes(1));
            return await _made.Reader.ReadAsync(hang.Token);
        }
    }
}
