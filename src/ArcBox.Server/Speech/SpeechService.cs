using Microsoft.Extensions.Options;

namespace ArcBox.Server.Speech;

/// <summary>The platform's text-to-speech service: themed, cached and safe to call from any game's server code.</summary>
public interface ISpeechService
{
    /// <summary>True when speech is enabled and a provider key is configured.</summary>
    bool IsAvailable { get; }

    /// <summary>MP3 bytes for <paramref name="text"/> (trimmed) in theme <paramref name="themeId"/>, from the cache when possible.</summary>
    /// <exception cref="SpeechUnavailableException">Speech is disabled or unconfigured.</exception>
    /// <exception cref="UnknownVoiceThemeException">No such theme.</exception>
    /// <exception cref="SpeechSynthesisException">The provider failed.</exception>
    Task<byte[]> SynthesizeAsync(string themeId, string text, CancellationToken ct);

    /// <summary>
    /// Fire-and-forget: synthesises each text into the cache in the background (at most two at a time, across all
    /// callers). Returns at once, never throws; failures are logged (at most once a minute) and dropped.
    /// </summary>
    void Prewarm(string themeId, IEnumerable<string> texts);
}

public sealed class SpeechUnavailableException() : Exception("Speech is disabled or has no API key.");

public sealed class UnknownVoiceThemeException(string id) : Exception($"Unknown voice theme '{id}'.");

public sealed class SpeechService : ISpeechService, IDisposable
{
    public const int PrewarmConcurrency = 2;
    private static readonly TimeSpan FailureLogInterval = TimeSpan.FromMinutes(1);

    private readonly IVoiceThemeRegistry _themes;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly SpeechCache _cache;
    private readonly SpeechUsage _usage;
    private readonly TimeProvider _time;
    private readonly ILogger<SpeechService> _log;
    private readonly SpeechOptions _options;
    private readonly SemaphoreSlim _prewarmGate = new(PrewarmConcurrency, PrewarmConcurrency);
    private readonly CancellationTokenSource _stopping = new();
    private readonly CancellationTokenRegistration _onStopping;
    private readonly Lock _failureGate = new();
    private DateTimeOffset _lastFailureLog = DateTimeOffset.MinValue;
    private int _suppressedFailures;
    private volatile bool _disposed;

    public SpeechService(
        IVoiceThemeRegistry themes,
        ISpeechSynthesizer synthesizer,
        SpeechCache cache,
        SpeechUsage usage,
        TimeProvider time,
        IOptions<SpeechOptions> options,
        ILogger<SpeechService> log,
        IHostApplicationLifetime? lifetime = null)
    {
        _themes = themes;
        _synthesizer = synthesizer;
        _cache = cache;
        _usage = usage;
        _time = time;
        _options = options.Value;
        _log = log;
        // Stop background prewarming as soon as the host starts shutting down, not only at container disposal.
        if (lifetime is not null)
            _onStopping = lifetime.ApplicationStopping.Register(CancelBackgroundWork);
    }

    public bool IsAvailable =>
        _options.Enabled
        && string.Equals(_options.Provider, "google", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(_options.Google.ApiKey);

    public async Task<byte[]> SynthesizeAsync(string themeId, string text, CancellationToken ct)
    {
        if (!IsAvailable || _disposed)
            throw new SpeechUnavailableException();
        var voice = _themes.Resolved(themeId) ?? throw new UnknownVoiceThemeException(themeId);
        var trimmed = text.Trim();

        try
        {
            return await _cache.GetOrCreateAsync(
                SpeechCache.Key(voice, trimmed),
                async token =>
                {
                    var audio = await _synthesizer.SynthesizeAsync(trimmed, voice, token).ConfigureAwait(false);
                    _usage.Record(themeId, trimmed);
                    return audio;
                },
                ct).ConfigureAwait(false);
        }
        catch (SpeechSynthesisException e)
        {
            LogFailure(themeId, e.Message);
            throw;
        }
    }

    public void Prewarm(string themeId, IEnumerable<string> texts)
    {
        try
        {
            if (!IsAvailable || _disposed)
                return;
            if (_themes.Resolved(themeId) is null)
            {
                _log.LogWarning("Prewarm skipped: unknown voice theme {Theme}", themeId);
                return;
            }

            var distinct = texts
                .Where(t => t is not null)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0 && t.Length <= _options.MaxChars)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var text in distinct)
                _ = Task.Run(() => PrewarmOneAsync(themeId, text));
        }
        catch (Exception e)
        {
            _log.LogWarning("Prewarm scheduling failed: {Error}", e.Message);
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _onStopping.Dispose();
        CancelBackgroundWork();
    }

    private async Task PrewarmOneAsync(string themeId, string text)
    {
        try
        {
            var token = _stopping.Token;
            await _prewarmGate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await SynthesizeAsync(themeId, text, token).ConfigureAwait(false);
            }
            finally
            {
                _prewarmGate.Release();
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Shutting down.
        }
        catch (SpeechSynthesisException)
        {
            // Already logged (rate-limited) by SynthesizeAsync.
        }
        catch (Exception e)
        {
            LogFailure(themeId, $"{e.GetType().Name}: {e.Message}");
        }
    }

    private void CancelBackgroundWork()
    {
        try
        {
            _stopping.Cancel();
        }
        catch (AggregateException)
        {
            // No callbacks of ours can throw; ignore anything else's.
        }
    }

    // Spec §4: Google failures are logged at most once a minute, with a count of the ones in between.
    private void LogFailure(string themeId, string message)
    {
        int suppressed;
        lock (_failureGate)
        {
            var now = _time.GetUtcNow();
            if (now - _lastFailureLog < FailureLogInterval)
            {
                _suppressedFailures++;
                return;
            }

            _lastFailureLog = now;
            suppressed = _suppressedFailures;
            _suppressedFailures = 0;
        }

        _log.LogWarning("Speech synthesis failed for theme {Theme}: {Error} ({Suppressed} more failures since the last report)",
            themeId, message, suppressed);
    }
}
