using System.Globalization;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Storage;

/// <summary>
/// A daily copy of the database made with <c>VACUUM INTO</c>, which is consistent while the server keeps writing. Runs at
/// 04:00 UTC and keeps the newest <see cref="StorageOptions.BackupDays"/> copies. In-memory databases are skipped.
/// </summary>
public sealed class BackupService : BackgroundService
{
    private const string Pattern = "arcbox-????-??-??.db";
    private static readonly TimeSpan RunAt = TimeSpan.FromHours(4);

    private readonly ArcDb _db;
    private readonly StorageOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BackupService> _log;
    private readonly IHostEnvironment? _environment;

    public BackupService(ArcDb db, IOptions<StorageOptions> options, TimeProvider time, ILogger<BackupService> log, IHostEnvironment? environment = null)
    {
        _db = db;
        _options = options.Value;
        _time = time;
        _log = log;
        _environment = environment;
    }

    /// <summary>Writes <c>arcbox-yyyy-MM-dd.db</c> (UTC date of <paramref name="now"/>) and prunes the old ones.</summary>
    public async Task RunOnceAsync(DateTimeOffset now)
    {
        if (_db.IsInMemory) return;
        var directory = ArcDb.ResolvePath(_options.BackupDirectory, _environment);
        await Task.Run(() => Backup(directory, now.UtcDateTime)).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var now = _time.GetUtcNow();
                await Task.Delay(NextRun(now) - now, _time, stoppingToken).ConfigureAwait(false);
                try
                {
                    await RunOnceAsync(_time.GetUtcNow()).ConfigureAwait(false);
                }
                catch (Exception e)
                {
                    _log.LogError(e, "Database backup failed");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    private static DateTimeOffset NextRun(DateTimeOffset now)
    {
        var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero) + RunAt;
        return today > now ? today : today.AddDays(1);
    }

    private void Backup(string directory, DateTime utc)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, $"arcbox-{utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.db");
        // VACUUM INTO refuses an existing file, and a half-written copy must never look like a backup.
        var temp = target + ".tmp";
        File.Delete(temp);

        using (var connection = _db.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "VACUUM INTO $path";
            command.Parameters.AddWithValue("$path", temp);
            command.ExecuteNonQuery();
        }
        File.Move(temp, target, overwrite: true);

        // The date is in the name, so the newest sort last.
        var old = Directory.GetFiles(directory, Pattern).OrderDescending(StringComparer.Ordinal).Skip(Math.Max(_options.BackupDays, 1));
        foreach (var file in old) File.Delete(file);
        _log.LogInformation("Database backed up to {File}", target);
    }
}
