using System.Collections.Concurrent;
using System.Globalization;

namespace ArcBox.Server.Speech;

/// <summary>
/// Characters sent to the provider since start, per theme and in total (cost guard, spec §3.1). Cache hits and failed
/// requests are not counted. Characters are Unicode scalar values, as Google bills them.
/// </summary>
public sealed class SpeechUsage
{
    private readonly ConcurrentDictionary<string, long> _byTheme = new(StringComparer.Ordinal);
    private long _total;

    public long Total => Interlocked.Read(ref _total);

    public IReadOnlyDictionary<string, long> ByTheme => new SortedDictionary<string, long>(_byTheme, StringComparer.Ordinal);

    public void Record(string themeId, string text)
    {
        long chars = text.EnumerateRunes().Count();
        _byTheme.AddOrUpdate(themeId, chars, (_, n) => n + chars);
        Interlocked.Add(ref _total, chars);
    }

    /// <summary>"arcbox.host=120, redline.questions=4210", or "none".</summary>
    public string Describe()
    {
        var parts = ByTheme.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}").ToList();
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }
}

/// <summary>Logs <see cref="SpeechUsage"/> once an hour and at shutdown.</summary>
public sealed class SpeechUsageReporter(SpeechUsage usage, TimeProvider time, ILogger<SpeechUsageReporter> log) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                Report();
        }
        catch (OperationCanceledException)
        {
            // Stopping.
        }
    }

    private int _finalReported;

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
        // Some hosts (WebApplicationFactory, for one) stop hosted services more than once; report once.
        if (Interlocked.Exchange(ref _finalReported, 1) == 0)
            Report();
    }

    public void Report() =>
        log.LogInformation(
            "Speech usage since start: {TotalChars} characters synthesised ({ByTheme})",
            usage.Total, usage.Describe());
}
