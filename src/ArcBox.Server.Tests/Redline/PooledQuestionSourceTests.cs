using System.Collections.Concurrent;
using ArcBox.Server.Games.Redline;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Redline.Logic;

namespace ArcBox.Server.Tests.Redline;

public class PooledQuestionSourceTests
{
    private static PooledQuestionSource Source(OpenTdbQuestionPool pool, ListLogger log, bool enabled = true) =>
        new(pool, Options.Create(new OpenTdbOptions { Enabled = enabled }), log);

    [Fact]
    public async Task Takes_from_pool_first_then_fallback()
    {
        var pool = await SeededPool.CreateAsync(50);
        pool.TryTake(40, []); // leave exactly 10
        var log = new ListLogger();

        var questions = Source(pool, log).Take(15);

        Assert.Equal(15, questions.Count);
        Assert.Equal(10, questions.Count(q => q.Origin == QuestionOrigin.OpenTdb));
        Assert.Equal(5, questions.Count(q => q.Origin == QuestionOrigin.Fallback));
        Assert.Equal(0, pool.Count);
        var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
        Assert.Contains("5", warning.Message);
    }

    [Fact]
    public async Task Pool_with_enough_questions_logs_nothing_and_uses_no_fallback()
    {
        var pool = await SeededPool.CreateAsync(50);
        var log = new ListLogger();

        var questions = Source(pool, log).Take(15);

        Assert.All(questions, q => Assert.Equal(QuestionOrigin.OpenTdb, q.Origin));
        Assert.Empty(log.Entries);
        Assert.Equal(35, pool.Count);
    }

    [Fact]
    public async Task Disabled_uses_fallback_only()
    {
        var pool = await SeededPool.CreateAsync(50);

        var questions = Source(pool, new ListLogger(), enabled: false).Take(15);

        Assert.Equal(15, questions.Count);
        Assert.All(questions, q => Assert.Equal(QuestionOrigin.Fallback, q.Origin));
        Assert.Equal(50, pool.Count); // never consulted
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(15)]
    [InlineData(200)]
    public async Task Always_returns_exactly_count(int count)
    {
        var pool = await SeededPool.CreateAsync(50);
        pool.TryTake(50, []);

        var questions = Source(pool, new ListLogger()).Take(count);

        Assert.Equal(count, questions.Count);
    }

    [Fact]
    public async Task Negative_count_returns_empty_instead_of_throwing()
    {
        var pool = await SeededPool.CreateAsync(50);

        Assert.Empty(Source(pool, new ListLogger()).Take(-3));
    }

    [Fact]
    public async Task Fallback_cycles_through_the_whole_pack_before_repeating()
    {
        var pool = await SeededPool.CreateAsync(50);
        pool.TryTake(50, []);
        var source = Source(pool, new ListLogger(), enabled: false);
        var pack = FallbackQuestions.All;

        var texts = source.Take(pack.Count * 3 + 7).Select(q => q.Text).ToList();

        for (var cycle = 0; cycle < 3; cycle++)
        {
            var block = texts.Skip(cycle * pack.Count).Take(pack.Count).ToList();
            Assert.Equal(pack.Count, block.Distinct().Count());
            Assert.Equal(pack.Select(q => q.Text).Order(), block.Order());
        }

        Assert.Equal(7, texts.Skip(pack.Count * 3).Distinct().Count());
    }

    [Fact]
    public async Task Fallback_never_repeats_a_question_within_one_take_across_a_reshuffle()
    {
        var pool = await SeededPool.CreateAsync(50);
        var source = Source(pool, new ListLogger(), enabled: false);
        var pack = FallbackQuestions.All;

        source.Take(11); // offset the cursor so later takes straddle the cycle boundary
        var calls = pack.Count * 40 / 15; // crosses about forty reshuffles
        for (var call = 0; call < calls; call++)
        {
            var texts = source.Take(15).Select(q => q.Text).ToList();
            Assert.Equal(15, texts.Distinct().Count());
        }
    }

    [Fact]
    public async Task Parallel_takes_never_share_a_pool_question()
    {
        var pool = await SeededPool.CreateAsync(400);
        var source = Source(pool, new ListLogger());
        var taken = new ConcurrentBag<TriviaQuestion>();

        for (var wave = 0; wave < 3; wave++)
        {
            Parallel.For(0, 8, _ =>
            {
                foreach (var q in source.Take(15))
                    taken.Add(q);
            });
        }

        var fromPool = taken.Where(q => q.Origin == QuestionOrigin.OpenTdb).Select(q => q.Text).ToList();
        Assert.Equal(360, taken.Count);
        Assert.Equal(360, fromPool.Count); // 3 waves x 8 x 15 = 360 <= 400, all served by the pool
        Assert.Equal(fromPool.Count, fromPool.Distinct().Count());
    }

    private sealed class ListLogger : ILogger<PooledQuestionSource>
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Enqueue((logLevel, formatter(state, exception)));
    }
}
