using System.Collections.Concurrent;
using System.Net;
using System.Text;
using ArcBox.Server.Games.Redline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Redline.Logic;

namespace ArcBox.Server.Tests.Redline;

/// <summary>
/// The pool is driven entirely by <see cref="ParkingTimeProvider"/>: every wait in the loop is a
/// <c>Task.Delay(…, time, ct)</c>, which creates exactly one fake timer. The harness waits (in real time) until the
/// loop has parked on its next timer, then advances fake time exactly to that timer's due time. Each step is
/// therefore deterministic regardless of thread scheduling, and gaps can be asserted exactly.
/// </summary>
public class OpenTdbQuestionPoolTests
{
    private static readonly TimeSpan Spacing = TimeSpan.FromSeconds(5.5);

    [Fact]
    public async Task Fills_to_target_in_spaced_requests()
    {
        await using var h = new Harness(fetch => Ok(Unique(fetch)));

        await h.StepUntil(() => h.Pool.Count >= 150);
        await h.Steps(5); // idle re-checks: must not fetch again once at target

        var fetches = h.Fetches();
        Assert.Equal(3, fetches.Count);
        Assert.Equal(150, h.Pool.Count);
        var token = Assert.Single(h.TokenRequests());
        Assert.True(fetches[0].At - token.At >= Spacing, "token request and first fetch must be spaced too");
        Assert.All(Gaps(fetches), gap => Assert.True(gap >= Spacing, $"gap {gap} < {Spacing}"));
        Assert.All(fetches, f => Assert.Contains("token=tok1", f.Req.RequestUri!.Query));
    }

    [Fact]
    public async Task Never_issues_two_requests_less_than_spacing_apart()
    {
        // A mix of every outcome: Ok, rate limit, token empty, failure, Ok with nothing new.
        await using var h = new Harness(fetch => (fetch % 5) switch
        {
            0 => Ok(Unique(fetch)),
            1 => Code(5),
            2 => Code(4),
            3 => Code(2),
            _ => Ok(Unique(0)),
        }, configure: o => (o.Target, o.LowWater) = (10_000, 10_000));

        await h.StepUntil(() => h.Fetches().Count >= 25);

        var all = h.AllRequests();
        Assert.All(Gaps(all), gap => Assert.True(gap >= Spacing, $"gap {gap} < {Spacing}"));
    }

    [Fact]
    public async Task Refills_only_below_low_water()
    {
        await using var h = new Harness(fetch => Ok(Unique(fetch)));
        await h.StepUntil(() => h.Pool.Count >= 150);

        var into = new List<TriviaQuestion>();
        Assert.Equal(90, h.Pool.TryTake(90, into)); // 60 left: not below LowWater
        await h.Steps(5);
        Assert.Equal(3, h.Fetches().Count);
        Assert.Equal(60, h.Pool.Count);

        Assert.Equal(1, h.Pool.TryTake(1, into)); // 59 left: refill until >= Target
        await h.StepUntil(() => h.Pool.Count >= 150);
        await h.Steps(5);

        Assert.Equal(5, h.Fetches().Count); // 59 → 109 → 159
        Assert.Equal(159, h.Pool.Count);
    }

    [Fact]
    public async Task Requests_new_token_after_token_empty()
    {
        await using var h = new Harness(fetch => fetch == 0 ? Code(4) : Ok(Unique(fetch)));

        await h.StepUntil(() => h.Pool.Count >= 50);

        var paths = h.AllRequests().Select(r => r.Req.RequestUri!.AbsolutePath).ToList();
        Assert.Equal(["/api_token.php", "/api.php", "/api_token.php", "/api.php"], paths.Take(4));
        Assert.All(h.TokenRequests(), r => Assert.Equal("?command=request", r.Req.RequestUri!.Query));
        var fetches = h.Fetches();
        Assert.Contains("token=tok1", fetches[0].Req.RequestUri!.Query);
        Assert.Contains("token=tok2", fetches[1].Req.RequestUri!.Query);
        Assert.Equal(50, h.Pool.Count);
    }

    [Fact]
    public async Task Backs_off_and_caps_on_rate_limit()
    {
        await using var h = new Harness(_ => Code(5));

        await h.StepUntil(() => h.Fetches().Count >= 10);

        // Base backoff 5 s is below the 5.5 s request spacing, so the first wait is the spacing.
        Assert.Equal(Seconds(5.5, 10, 20, 40, 80, 160, 300, 300, 300), Gaps(h.Fetches()).Take(9));
        Assert.Single(h.TokenRequests());
        Assert.Equal(0, h.Pool.Count);
        Assert.Contains(h.Log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Theory]
    [InlineData("http429")]
    [InlineData("http500")]
    [InlineData("malformed")]
    [InlineData("httpException")]
    [InlineData("unexpectedException")]
    public async Task Backs_off_on_failures_and_exceptions(string mode)
    {
        await using var h = new Harness(_ => mode switch
        {
            "http429" => Json("{}", HttpStatusCode.TooManyRequests),
            "http500" => Json("{}", HttpStatusCode.InternalServerError),
            "malformed" => Json("{\"response_code\":0,\"results\":"),
            "httpException" => throw new HttpRequestException("boom"),
            _ => throw new InvalidOperationException("unexpected"),
        });

        await h.StepUntil(() => h.Fetches().Count >= 5);

        Assert.Equal(Seconds(5.5, 10, 20, 40), Gaps(h.Fetches()).Take(4));
        Assert.False(h.Pool.ExecuteTask!.IsCompleted, "the loop must survive failures");
        Assert.True(h.Log.Entries.Count(e => e.Level == LogLevel.Warning) >= 5);
    }

    [Fact]
    public async Task Keeps_running_when_token_request_throws()
    {
        var tokenCalls = 0;
        await using var h = new Harness(fetch => Ok(Unique(fetch)), token: () =>
            Interlocked.Increment(ref tokenCalls) == 1 ? throw new InvalidOperationException("boom") : Json(TokenJson("tokX")));

        await h.StepUntil(() => h.Pool.Count >= 50);

        Assert.Equal(2, h.TokenRequests().Count);
        Assert.Contains("token=tokX", h.Fetches()[0].Req.RequestUri!.Query);
        Assert.Contains(h.Log.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task Ok_with_new_questions_resets_backoff()
    {
        await using var h = new Harness(fetch => fetch == 3 ? Ok(Unique(fetch)) : Code(5));

        await h.StepUntil(() => h.Fetches().Count >= 7);

        // fail, fail, fail, Ok(50 new) → reset, fail (base), fail.
        Assert.Equal(Seconds(5.5, 10, 20, 5.5, 5.5, 10), Gaps(h.Fetches()).Take(6));
    }

    [Fact]
    public async Task Ok_with_nothing_new_neither_resets_nor_escalates_backoff()
    {
        await using var h = new Harness(fetch => fetch == 3 ? Ok("") : Code(5));

        await h.StepUntil(() => h.Fetches().Count >= 6);

        // fail, fail, fail (next backoff 40), empty Ok → normal spacing, fail → 40 (not reset to 5).
        Assert.Equal(Seconds(5.5, 10, 20, 5.5, 40), Gaps(h.Fetches()).Take(5));
    }

    [Fact]
    public async Task Dedupes_repeated_texts()
    {
        // Same 50 questions every time; later batches vary case and surrounding whitespace.
        await using var h = new Harness(fetch => Ok(Unique(0, text: i => fetch switch
        {
            0 => $"Pool question {i}?",
            1 => $"  POOL QUESTION {i}?  ",
            _ => $"pool Question {i}?\t",
        })));

        await h.StepUntil(() => h.Fetches().Count >= 4);
        await h.Settle();

        Assert.Equal(50, h.Pool.Count);
        var into = new List<TriviaQuestion>();
        h.Pool.TryTake(50, into);
        Assert.All(into, q => Assert.StartsWith("Pool question ", q.Text, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Dedupe_window_is_bounded()
    {
        // Batches A, B, A: with a 50-hash window, A's hashes have been evicted by B, so A is accepted again.
        await using var h = new Harness(fetch => Ok(Unique(fetch % 2 == 0 ? 0 : 50)), configure: o => o.DedupeWindow = 50);

        await h.StepUntil(() => h.Fetches().Count >= 3);
        await h.Settle();

        Assert.Equal(150, h.Pool.Count);
    }

    [Fact]
    public async Task TryTake_dequeues_without_blocking_and_reports_count()
    {
        await using var h = new Harness(fetch => fetch == 0 ? Ok(Unique(0)) : Code(5));
        var into = new List<TriviaQuestion>();

        Assert.Equal(0, h.Pool.TryTake(5, into)); // empty pool: returns immediately
        Assert.Empty(into);

        await h.StepUntil(() => h.Pool.Count >= 50);

        Assert.Equal(20, h.Pool.TryTake(20, into));
        Assert.Equal(30, h.Pool.Count);
        Assert.Equal(0, h.Pool.TryTake(0, into));
        Assert.Equal(30, h.Pool.TryTake(100, into)); // at most what's available
        Assert.Equal(0, h.Pool.Count);
        Assert.Equal(0, h.Pool.TryTake(1, into));
        Assert.Equal(50, into.Count);
        Assert.Equal(50, into.Select(q => q.Text).Distinct().Count());
        Assert.Equal("Pool question 0?", into[0].Text); // FIFO
    }

    [Fact]
    public async Task Concurrent_TryTake_never_hands_out_a_question_twice()
    {
        await using var h = new Harness(fetch => Ok(Unique(fetch)));
        var taken = new ConcurrentBag<TriviaQuestion>();
        using var go = new ManualResetEventSlim();

        // 8 takers race each other while the loop is still filling.
        var takers = Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            go.Wait();
            var mine = new List<TriviaQuestion>();
            for (var i = 0; i < 2000; i++)
                h.Pool.TryTake(3, mine);
            foreach (var q in mine)
                taken.Add(q);
        })).ToArray();
        go.Set();
        await h.Steps(4);
        await Task.WhenAll(takers);

        var leftover = new List<TriviaQuestion>();
        h.Pool.TryTake(int.MaxValue, leftover);
        var all = taken.Concat(leftover).Select(q => q.Text).ToList();
        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.Equal(50 * h.Fetches().Count, all.Count);
    }

    [Fact]
    public async Task Stops_cleanly_on_cancellation()
    {
        var h = new Harness(fetch => Ok(Unique(fetch)));
        await h.StepUntil(() => h.Pool.Count >= 50);
        await h.Settle();

        await h.Pool.StopAsync(CancellationToken.None);

        Assert.True(h.Pool.ExecuteTask!.IsCompleted);
        Assert.False(h.Pool.ExecuteTask.IsFaulted);
        h.Pool.Dispose();
    }

    [Fact]
    public async Task Stops_cleanly_during_a_long_backoff()
    {
        var h = new Harness(_ => Code(5));
        await h.StepUntil(() => h.Fetches().Count >= 8); // parked on a 300 s backoff
        await h.Settle();

        var stop = h.Pool.StopAsync(CancellationToken.None);

        await stop.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); // fake time is not advanced: the delay must observe the token
        Assert.True(h.Pool.ExecuteTask!.IsCompleted);
        Assert.False(h.Pool.ExecuteTask.IsFaulted);
        h.Pool.Dispose();
    }

    // ---- helpers ----

    private static TimeSpan[] Seconds(params double[] s) => s.Select(TimeSpan.FromSeconds).ToArray();

    private static List<TimeSpan> Gaps(IReadOnlyList<(HttpRequestMessage Req, DateTimeOffset At)> requests) =>
        requests.Zip(requests.Skip(1), (a, b) => b.At - a.At).ToList();

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Code(int code) => Json($$"""{"response_code":{{code}},"results":[]}""");

    private static HttpResponseMessage Ok(string questions) => Json($$"""{"response_code":0,"results":[{{questions}}]}""");

    private static string TokenJson(string token) =>
        $$"""{"response_code":0,"response_message":"Token Generated Successfully!","token":"{{token}}"}""";

    /// <summary>50 well-formed questions numbered from <c>50 × start</c>-style offsets: texts are unique per index.</summary>
    private static string Unique(int batch, Func<int, string>? text = null)
    {
        var first = batch * 50;
        return string.Join(",", Enumerable.Range(first, 50).Select(i =>
            Q(text?.Invoke(i) ?? $"Pool question {i}?", $"Right {i}", $"Wrong {i}a", $"Wrong {i}b", $"Wrong {i}c")));
    }

    private static string Enc(string s) => Uri.EscapeDataString(s);

    private static string Q(string text, string correct, params string[] incorrect) =>
        $$"""
        {"type":"multiple","difficulty":"easy","category":"General%20Knowledge","question":"{{Enc(text)}}","correct_answer":"{{Enc(correct)}}","incorrect_answers":[{{string.Join(",", incorrect.Select(i => $"\"{Enc(i)}\""))}}]}
        """;

    private static async Task Eventually(Func<bool> cond, string what = "condition")
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!cond())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting for {what}");
            await Task.Delay(2);
        }
    }

    /// <summary>A FakeTimeProvider that records every timer the pool creates, so the test knows when it is parked.</summary>
    private sealed class ParkingTimeProvider() : FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    {
        private readonly List<DateTimeOffset> _dues = [];

        public int TimersCreated
        {
            get { lock (_dues) return _dues.Count; }
        }

        public DateTimeOffset DueOf(int index)
        {
            lock (_dues) return _dues[index];
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            // Publish the timer only once it is armed: a test that sees it earlier could advance the clock before
            // base.CreateTimer runs, and the timer would then be armed relative to the already-advanced clock.
            var due = GetUtcNow() + dueTime;
            var timer = base.CreateTimer(callback, state, dueTime, period);
            lock (_dues) _dues.Add(due);
            return timer;
        }
    }

    private sealed class ListLogger : ILogger<OpenTdbQuestionPool>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Enqueue((logLevel, formatter(state, exception)));
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly ParkingTimeProvider _time = new();
        private readonly FakeHandler _handler;
        private int _consumed;
        private int _fetchIndex = -1;
        private int _tokenIndex;

        public Harness(Func<int, HttpResponseMessage> fetch, Func<HttpResponseMessage>? token = null,
            Action<OpenTdbOptions>? configure = null)
        {
            _handler = new FakeHandler
            {
                Clock = _time,
                Respond = req => req.RequestUri!.AbsolutePath == "/api_token.php"
                    ? token?.Invoke() ?? Json(TokenJson($"tok{Interlocked.Increment(ref _tokenIndex)}"))
                    : fetch(Interlocked.Increment(ref _fetchIndex)),
            };
            var http = new HttpClient(_handler) { BaseAddress = new Uri("https://opentdb.com/") };
            var options = new OpenTdbOptions();
            configure?.Invoke(options);
            Pool = new OpenTdbQuestionPool(new OpenTdbClient(http), Options.Create(options), _time, Log);
            Pool.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        public OpenTdbQuestionPool Pool { get; }

        public ListLogger Log { get; } = new();

        public List<(HttpRequestMessage Req, DateTimeOffset At)> AllRequests()
        {
            lock (_handler.Requests) return [.. _handler.Requests];
        }

        public List<(HttpRequestMessage Req, DateTimeOffset At)> Fetches() =>
            AllRequests().Where(r => r.Req.RequestUri!.AbsolutePath == "/api.php").ToList();

        public List<(HttpRequestMessage Req, DateTimeOffset At)> TokenRequests() =>
            AllRequests().Where(r => r.Req.RequestUri!.AbsolutePath == "/api_token.php").ToList();

        /// <summary>Waits until the loop is parked on a timer it has not been advanced past yet.</summary>
        public Task Settle() => Eventually(() => _time.TimersCreated > _consumed, "the pool loop to park on a timer");

        /// <summary>
        /// Waits for the loop to park, advances fake time exactly to that timer's due time, then waits for the loop to
        /// finish the work that timer released and park again — so state is stable whenever a step returns.
        /// </summary>
        public async Task Step()
        {
            await Settle();
            var delta = _time.DueOf(_consumed++) - _time.GetUtcNow();
            _time.Advance(delta > TimeSpan.Zero ? delta : TimeSpan.Zero);
            await Settle();
        }

        public async Task Steps(int n)
        {
            for (var i = 0; i < n; i++)
                await Step();
        }

        public async Task StepUntil(Func<bool> cond, int maxSteps = 200)
        {
            for (var i = 0; !cond(); i++)
            {
                if (i == maxSteps)
                    Assert.Fail($"Condition not reached after {maxSteps} timer steps");
                await Step();
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Pool.StopAsync(CancellationToken.None);
            Pool.Dispose();
        }
    }
}
