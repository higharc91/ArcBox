using ArcBox.Server.Games;
using ArcBox.Server.Games.Redline;
using ArcBox.Server.Speech;
using ArcBox.Server.Tests.Speech.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Redline.Logic;

namespace ArcBox.Server.Tests.Speech;

public class NarratedQuestionSourceTests : IDisposable
{
    private readonly TempDir _dir = new();

    public void Dispose() => _dir.Dispose();

    private sealed class FixedSource(params TriviaQuestion[] questions) : IQuestionSource
    {
        public Difficulty? LastDifficulty { get; private set; }

        public IReadOnlyList<TriviaQuestion> Take(int count) => questions.Take(count).ToList();

        public IReadOnlyList<TriviaQuestion> Take(int count, Difficulty difficulty)
        {
            LastDifficulty = difficulty;
            return questions.Take(count).ToList();
        }
    }

    private sealed class RecordingSpeech : ISpeechService
    {
        public List<(string Theme, List<string> Texts)> Prewarms { get; } = [];

        public Exception? Throw { get; init; }

        public bool IsAvailable => true;

        public Task<byte[]> SynthesizeAsync(string themeId, string text, CancellationToken ct) => throw new NotSupportedException();

        public void Prewarm(string themeId, IEnumerable<string> texts)
        {
            if (Throw is not null)
                throw Throw;
            Prewarms.Add((themeId, texts.ToList()));
        }
    }

    private static TriviaQuestion Q(string text) =>
        new("Sport", Difficulty.Easy, text, "Yes", ["No", "Maybe", "Never"], QuestionOrigin.Fallback);

    private static readonly FixedSource Inner = new(Q("Who won in 2021?"), Q("How many laps?"), Q("Which team is red?"));

    [Fact]
    public void Take_by_difficulty_passes_through_to_the_inner_source_and_prewarms()
    {
        var speech = new RecordingSpeech();
        var inner = new FixedSource(Q("Who won in 2021?"), Q("How many laps?"));
        var source = new NarratedQuestionSource(inner, speech, NullLogger<NarratedQuestionSource>.Instance);

        var questions = source.Take(2, Difficulty.Easy);

        Assert.Equal(Difficulty.Easy, inner.LastDifficulty);
        Assert.Equal(2, questions.Count);
        var (theme, texts) = Assert.Single(speech.Prewarms);
        Assert.Equal(RedlineVoices.Questions, theme);
        Assert.Equal(["Who won in 2021?", "How many laps?"], texts);
    }

    [Fact]
    public void Take_prewarms_question_texts()
    {
        var speech = new RecordingSpeech();
        var source = new NarratedQuestionSource(Inner, speech, NullLogger<NarratedQuestionSource>.Instance);

        var questions = source.Take(2);

        Assert.Equal(Inner.Take(2), questions);
        var (theme, texts) = Assert.Single(speech.Prewarms);
        Assert.Equal(RedlineVoices.Questions, theme);
        Assert.Equal(["Who won in 2021?", "How many laps?"], texts);
    }

    [Fact]
    public async Task Prewarm_errors_do_not_affect_take()
    {
        // A prewarm that throws synchronously...
        var throwing = new NarratedQuestionSource(Inner, new RecordingSpeech { Throw = new InvalidOperationException("boom") },
            NullLogger<NarratedQuestionSource>.Instance);
        Assert.Equal(3, throwing.Take(3).Count);

        // ...and a real service whose provider fails or hangs: Take returns at once with the same questions.
        var synth = new ScriptedSynthesizer();
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        synth.Behaviour = async (text, _, _) => text.StartsWith("Who", StringComparison.Ordinal)
            ? throw new SpeechSynthesisException("Google TTS HTTP 500")
            : await release.Task;
        var (service, _, _) = SpeechKit.Service(synth, SpeechKit.Options(_dir.Path), null, [.. RedlineVoices.All]);
        var source = new NarratedQuestionSource(Inner, service, NullLogger<NarratedQuestionSource>.Instance);

        var questions = source.Take(3);

        Assert.Equal(Inner.Take(3), questions);
        await SpeechKit.Until(() => synth.Calls >= 2, "prewarms to run");
        release.SetResult([1]);
        service.Dispose();
    }

    [Fact]
    public void AddRedline_decorates_the_question_source_only_when_speech_is_available()
    {
        static IQuestionSource Resolve(bool withSpeech, string? key)
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Redline:OpenTdb:Enabled"] = "false",
                ["Speech:Google:ApiKey"] = key,
                ["Speech:CacheDir"] = Path.Combine(Path.GetTempPath(), "arcbox-tts-unused"),
            }).Build();
            var services = new ServiceCollection();
            services.AddLogging();
            if (withSpeech)
                services.AddSpeech(config);
            services.AddRedline(config);
            services.AddSingleton<IGameRegistry, GameRegistry>();
            return services.BuildServiceProvider().GetRequiredService<IQuestionSource>();
        }

        Assert.IsType<NarratedQuestionSource>(Resolve(withSpeech: true, key: "k"));
        Assert.IsType<PooledQuestionSource>(Resolve(withSpeech: true, key: null));
        Assert.IsType<PooledQuestionSource>(Resolve(withSpeech: false, key: "k"));
    }
}

public class SpeechRateLimiterTests
{
    [Fact]
    public void Rate_limiters_are_dropped_when_rooms_go_away()
    {
        var live = new Dictionary<string, object>();
        using var limiter = new SpeechRateLimiter(code => live.GetValueOrDefault(code), perMinute: 2);
        var roomA = new object();
        var roomB = new object();
        live["AAAA"] = roomA;
        live["BBBB"] = roomB;

        Assert.True(limiter.TryAcquire("AAAA", roomA, out _));
        Assert.True(limiter.TryAcquire("AAAA", roomA, out _));
        Assert.False(limiter.TryAcquire("AAAA", roomA, out var retryAfter));
        Assert.True(retryAfter > TimeSpan.Zero);
        Assert.True(limiter.TryAcquire("BBBB", roomB, out _));
        Assert.Equal(2, limiter.Count);

        // Room A closes; the next request from anywhere drops its limiter.
        live.Remove("AAAA");
        Assert.True(limiter.TryAcquire("BBBB", roomB, out _));
        Assert.Equal(1, limiter.Count);

        // A new room that happens to reuse the code starts with a fresh allowance.
        var roomA2 = new object();
        live["AAAA"] = roomA2;
        Assert.True(limiter.TryAcquire("AAAA", roomA2, out _));
    }
}
