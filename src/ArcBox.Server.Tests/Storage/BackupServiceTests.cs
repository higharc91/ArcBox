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
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 20, 3, 0, 0, TimeSpan.Zero));
        var service = NewService(db, options, time);
        var ct = TestContext.Current.CancellationToken;
        var backups = Path.Combine(_dir.FullName, "backups");
        await service.StartAsync(ct);
        try
        {
            await Task.Delay(100, ct);
            Assert.False(Directory.Exists(backups)); // not yet 04:00

            time.Advance(TimeSpan.FromHours(1));
            await WaitFor(() => File.Exists(Path.Combine(backups, "arcbox-2026-01-20.db")), ct);

            time.Advance(TimeSpan.FromHours(24));
            await WaitFor(() => File.Exists(Path.Combine(backups, "arcbox-2026-01-21.db")), ct);
        }
        finally
        {
            await service.StopAsync(ct);
        }
    }

    private static async Task WaitFor(Func<bool> condition, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) await Task.Delay(20, ct);
        Assert.True(condition());
    }
}
