using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Redline.Logic;

namespace ArcBox.Server.Games.Redline;

/// <summary>
/// Keeps a warm queue of OpenTDB questions so rooms can take them synchronously (spec §4.5).
/// </summary>
/// <remarks>
/// <para>Threading: <see cref="TryTake"/> and <see cref="Count"/> are called from room actors on any thread and touch
/// only the <see cref="ConcurrentQueue{T}"/>, whose <c>TryDequeue</c> is atomic, so no question is handed out twice.
/// Everything else (token, backoff, fill state and the dedupe set/FIFO) is owned by the single refill loop in
/// <see cref="ExecuteAsync"/> and is never touched from another thread, so it needs no locking.</para>
/// <para>Rate limit: every loop iteration issues at most one request and always ends with a wait of at least
/// <see cref="OpenTdbOptions.MinRequestSpacing"/>, measured from when that request completed, so no two requests
/// (token requests included) are ever closer together than the spacing.</para>
/// </remarks>
public sealed class OpenTdbQuestionPool(
    OpenTdbClient client,
    IOptions<OpenTdbOptions> options,
    TimeProvider time,
    ILogger<OpenTdbQuestionPool> log) : BackgroundService
{
    private const int BatchSize = 50;
    private static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(5);

    private readonly OpenTdbOptions _options = options.Value;
    private readonly ConcurrentQueue<TriviaQuestion> _queue = new();

    // Loop-owned state: only ExecuteAsync and the methods it calls read or write these.
    private readonly HashSet<string> _recent = new(StringComparer.Ordinal);
    private readonly Queue<string> _recentOrder = new();
    private string? _token;
    private bool _filling;
    private TimeSpan _nextBackoff = BaseBackoff;

    /// <summary>Questions currently available. Thread-safe.</summary>
    public int Count => _queue.Count;

    /// <summary>
    /// Dequeues up to <paramref name="count"/> questions into <paramref name="into"/> without blocking and returns how
    /// many were added. Thread-safe; concurrent callers never receive the same question.
    /// </summary>
    public int TryTake(int count, List<TriviaQuestion> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        var taken = 0;
        while (taken < count && _queue.TryDequeue(out var question))
        {
            into.Add(question);
            taken++;
        }

        return taken;
    }

    /// <summary>
    /// Like <see cref="TryTake(int, List{TriviaQuestion})"/> but only takes questions of <paramref name="difficulty"/>.
    /// Reads <see cref="Count"/> once and dequeues at most that many items, so it always terminates even when the queue
    /// holds no match. Matches are kept until <paramref name="count"/> is reached; every other scanned item (non-matches
    /// and surplus matches) goes back to the tail in the order it was seen, so the remaining questions keep their relative order.
    /// </summary>
    /// <remarks>
    /// Other threads can briefly see the queue short while a scan is in flight (items are out of the queue until
    /// requeued), so a concurrent taker may get fewer matches than are strictly present; callers fall back for the rest.
    /// No question is ever handed out twice or lost.
    /// </remarks>
    public int TryTake(int count, List<TriviaQuestion> into, Difficulty difficulty)
    {
        ArgumentNullException.ThrowIfNull(into);
        var taken = 0;
        for (var scan = _queue.Count; scan > 0 && _queue.TryDequeue(out var question); scan--)
        {
            if (taken < count && question.Difficulty == difficulty)
            {
                into.Add(question);
                taken++;
            }
            else
            {
                _queue.Enqueue(question);
            }
        }

        return taken;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            stoppingToken.ThrowIfCancellationRequested();

            TimeSpan wait;
            try
            {
                wait = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                log.LogWarning(e, "OpenTDB refill attempt threw unexpectedly");
                wait = Failure();
            }

            // Clamp so a misconfigured negative spacing cannot make Task.Delay throw out of the loop.
            await Task.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, time, stoppingToken).ConfigureAwait(false);
        }
    }

    /// <summary>Issues at most one request and returns how long to wait before the next iteration.</summary>
    private async Task<TimeSpan> RunOnceAsync(CancellationToken ct)
    {
        UpdateFilling();
        if (!_filling)
            return _options.MinRequestSpacing; // idle re-check

        if (_token is null)
        {
            _token = await client.RequestTokenAsync(ct).ConfigureAwait(false);
            if (_token is not null)
                return _options.MinRequestSpacing;

            log.LogWarning("OpenTDB token request failed");
            return Failure();
        }

        switch (await client.FetchAsync(_token, BatchSize, ct).ConfigureAwait(false))
        {
            case OpenTdbResult.Ok ok:
                var added = Enqueue(ok.Questions);
                UpdateFilling(); // reaching Target stops filling now, not after rooms have taken some again
                // Only real progress resets the backoff; an empty or all-duplicate batch just continues at normal spacing.
                if (added > 0)
                    _nextBackoff = BaseBackoff;
                else if (ok.Questions.Count == 0)
                    log.LogWarning("OpenTDB returned no usable questions");
                else
                    log.LogInformation("OpenTDB batch of {Received} questions were all recent duplicates", ok.Questions.Count);
                return _options.MinRequestSpacing;

            case OpenTdbResult.TokenEmpty:
                log.LogInformation("OpenTDB token exhausted; requesting a new one");
                _token = null;
                return _options.MinRequestSpacing;

            case OpenTdbResult.RateLimited:
                log.LogWarning("OpenTDB rate limited the question pool");
                return Failure();

            case OpenTdbResult.Failed failed:
                log.LogWarning("OpenTDB request failed: {Reason}", failed.Reason);
                return Failure();

            default:
                throw new InvalidOperationException("Unknown OpenTDB result");
        }
    }

    /// <summary>Hysteresis: start filling below LowWater, keep going until at or above Target.</summary>
    private void UpdateFilling()
    {
        var count = _queue.Count;
        if (count < _options.LowWater)
            _filling = true;
        else if (count >= _options.Target)
            _filling = false;
    }

    /// <summary>Returns the wait after a failure (5 s, 10 s, 20 s … capped, never below the spacing) and escalates.</summary>
    private TimeSpan Failure()
    {
        var backoff = _nextBackoff < _options.MaxBackoff ? _nextBackoff : _options.MaxBackoff;
        _nextBackoff = _nextBackoff >= _options.MaxBackoff / 2 ? _options.MaxBackoff : _nextBackoff * 2;
        return backoff > _options.MinRequestSpacing ? backoff : _options.MinRequestSpacing;
    }

    private int Enqueue(IReadOnlyList<TriviaQuestion> questions)
    {
        var added = 0;
        foreach (var question in questions)
        {
            var key = Hash(question.Text);
            if (!_recent.Add(key))
                continue;

            _recentOrder.Enqueue(key);
            while (_recentOrder.Count > _options.DedupeWindow)
                _recent.Remove(_recentOrder.Dequeue());

            _queue.Enqueue(question);
            added++;
        }

        return added;
    }

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim().ToLowerInvariant())));
}
