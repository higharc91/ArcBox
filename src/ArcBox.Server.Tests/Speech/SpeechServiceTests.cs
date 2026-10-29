using System.Text;
using ArcBox.Sdk.Speech;
using ArcBox.Server.Speech;
using ArcBox.Server.Tests.Speech.Support;

namespace ArcBox.Server.Tests.Speech;

public class SpeechServiceTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly ScriptedSynthesizer _synth = new();

    public void Dispose() => _dir.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cache_hit_skips_google()
    {
        var (service, _, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));

        var first = await service.SynthesizeAsync(VoiceThemes.Host, "Lights out!", Ct);
        var second = await service.SynthesizeAsync(VoiceThemes.Host, "  Lights out!\n", Ct); // same text once trimmed

        Assert.Equal(1, _synth.Calls);
        Assert.Equal(first, second);
        Assert.Single(Directory.GetFiles(_dir.Path, "*.mp3"));

        // A fresh instance (a restart, or another server) on the same directory finds the clip on disk.
        var other = new ScriptedSynthesizer();
        var (restarted, cache, _) = SpeechKit.Service(other, SpeechKit.Options(_dir.Path));
        Assert.Equal(1, cache.Count);
        Assert.Equal(first, await restarted.SynthesizeAsync(VoiceThemes.Host, "Lights out!", Ct));
        Assert.Equal(0, other.Calls);
    }

    // Review Focus 2: the question prewarm racing the TV's own request.
    [Fact]
    public async Task Concurrent_requests_share_one_synthesis()
    {
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synth.Behaviour = (_, _, _) => release.Task;
        var (service, _, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));

        service.Prewarm(VoiceThemes.Host, ["What is the capital of France?"]);
        var requests = Enumerable.Range(0, 5)
            .Select(_ => service.SynthesizeAsync(VoiceThemes.Host, "What is the capital of France?", Ct))
            .ToList();
        await SpeechKit.Until(() => _synth.Calls > 0, "the synthesis to start");
        await Task.Delay(50, Ct); // give any duplicate synthesis a chance to show up

        release.SetResult(Encoding.UTF8.GetBytes("clip"));
        var results = await Task.WhenAll(requests);

        Assert.Equal(1, _synth.Calls);
        Assert.All(results, r => Assert.Equal("clip", Encoding.UTF8.GetString(r)));
    }

    [Fact]
    public async Task One_caller_giving_up_does_not_cancel_the_shared_synthesis()
    {
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synth.Behaviour = (_, _, _) => release.Task;
        var (service, _, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));
        using var impatient = new CancellationTokenSource();

        var abandoned = service.SynthesizeAsync(VoiceThemes.Host, "Box box", impatient.Token);
        var patient = service.SynthesizeAsync(VoiceThemes.Host, "Box box", Ct);
        await SpeechKit.Until(() => _synth.Calls > 0, "the synthesis to start");
        impatient.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);

        release.SetResult([7]);
        Assert.Equal([7], await patient);
        Assert.Equal(1, _synth.Calls);
    }

    [Fact]
    public async Task Failures_are_not_cached()
    {
        var fail = true;
        _synth.Behaviour = (_, _, _) => fail
            ? Task.FromException<byte[]>(new SpeechSynthesisException("Google TTS HTTP 500: boom"))
            : Task.FromResult<byte[]>([1, 2]);
        var (service, cache, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));

        await Assert.ThrowsAsync<SpeechSynthesisException>(() => service.SynthesizeAsync(VoiceThemes.Host, "Pit stop", Ct));
        Assert.Equal(0, cache.Count);
        Assert.False(Directory.Exists(_dir.Path) && Directory.GetFiles(_dir.Path).Length > 0);

        fail = false;
        Assert.Equal([1, 2], await service.SynthesizeAsync(VoiceThemes.Host, "Pit stop", Ct));
        Assert.Equal(2, _synth.Calls);
    }

    [Fact]
    public async Task Cache_key_changes_with_theme_voice()
    {
        var charon = new ResolvedVoice("en-US-Chirp3-HD-Charon", "en-US", null, null, null);
        var keys = new[]
        {
            SpeechCache.Key(charon, "Hello"),
            SpeechCache.Key(charon with { Voice = "en-US-Chirp3-HD-Puck" }, "Hello"),
            SpeechCache.Key(charon with { LanguageCode = "en-GB" }, "Hello"),
            SpeechCache.Key(charon with { SpeakingRate = 1.1 }, "Hello"),
            SpeechCache.Key(charon with { Pitch = 1 }, "Hello"),
            SpeechCache.Key(charon with { VolumeGainDb = 2 }, "Hello"),
            SpeechCache.Key(charon, "Hello!"),
        };
        Assert.Equal(keys.Length, keys.Distinct().Count());
        Assert.Equal(SpeechCache.Key(charon, "Hello"), SpeechCache.Key(charon with { }, "Hello"));

        // End to end: re-voicing a theme in configuration synthesises again instead of serving the old clip.
        var (before, _, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));
        await before.SynthesizeAsync(VoiceThemes.Host, "Welcome", Ct);

        var options = SpeechKit.Options(_dir.Path);
        options.Voices[VoiceThemes.Host] = new VoiceThemeOverride { Voice = "en-US-Chirp3-HD-Charon" };
        var (after, _, _) = SpeechKit.Service(_synth, options);
        await after.SynthesizeAsync(VoiceThemes.Host, "Welcome", Ct);

        Assert.Equal(2, _synth.Calls);
        Assert.Equal(["en-US-Chirp3-HD-Puck", "en-US-Chirp3-HD-Charon"], _synth.Requests.Select(r => r.Voice.Voice));
    }

    [Fact]
    public async Task Character_counter_counts_only_synthesised_text()
    {
        var (service, _, usage) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path), null,
            new VoiceTheme("game.voice", "Test", "en-US-Chirp3-HD-Charon"));

        await service.SynthesizeAsync(VoiceThemes.Host, " Hello ", Ct);     // 5
        await service.SynthesizeAsync(VoiceThemes.Host, "Hello", Ct);       // cache hit: not counted
        await service.SynthesizeAsync("game.voice", "Café 🏁", Ct);         // 6 characters (the flag is one)
        _synth.Behaviour = (_, _, _) => Task.FromException<byte[]>(new SpeechSynthesisException("nope"));
        await Assert.ThrowsAsync<SpeechSynthesisException>(() => service.SynthesizeAsync(VoiceThemes.Host, "Failed", Ct));

        Assert.Equal(11, usage.Total);
        Assert.Equal(new Dictionary<string, long> { [VoiceThemes.Host] = 5, ["game.voice"] = 6 }, usage.ByTheme);
        Assert.Equal("arcbox.host=5, game.voice=6", usage.Describe());
    }

    [Fact]
    public async Task Prewarm_runs_at_most_two_at_a_time_and_returns_at_once()
    {
        var release = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        _synth.Behaviour = (_, _, _) => release.Task;
        var (service, cache, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));

        service.Prewarm(VoiceThemes.Host, ["one", "two", "three", "four", "five", "two", "", "   "]);

        await SpeechKit.Until(() => _synth.Calls == 2, "two prewarms to start");
        await Task.Delay(100, Ct);
        Assert.Equal(2, _synth.Calls);

        release.SetResult([1]);
        await SpeechKit.Until(() => cache.Count == 5, "all five distinct texts to be cached");
        Assert.Equal(5, _synth.Calls);
        Assert.Equal(2, _synth.MaxInFlight);
    }

    [Fact]
    public async Task Prewarm_is_a_no_op_when_unavailable_or_unknown_theme()
    {
        var (unconfigured, _, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path, key: null));
        unconfigured.Prewarm(VoiceThemes.Host, ["Hello"]);
        await Assert.ThrowsAsync<SpeechUnavailableException>(() => unconfigured.SynthesizeAsync(VoiceThemes.Host, "Hello", Ct));

        var (service, _, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));
        service.Prewarm("nobody.voice", ["Hello"]);
        await Assert.ThrowsAsync<UnknownVoiceThemeException>(() => service.SynthesizeAsync("nobody.voice", "Hello", Ct));

        await Task.Delay(50, Ct);
        Assert.Equal(0, _synth.Calls);
    }

    [Fact]
    public async Task Shutdown_cancels_prewarm_without_waiting()
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _synth.Behaviour = async (_, _, ct) =>
        {
            await using var _ = ct.Register(() => cancelled.TrySetResult());
            await Task.Delay(Timeout.Infinite, ct);
            return [];
        };
        var (service, cache, _) = SpeechKit.Service(_synth, SpeechKit.Options(_dir.Path));
        service.Prewarm(VoiceThemes.Host, ["Never finishes", "Queued behind it", "And another"]);
        await SpeechKit.Until(() => _synth.Calls == 2, "prewarms to start");

        var started = DateTime.UtcNow;
        service.Dispose();
        cache.Dispose();
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(1), "Dispose must not wait for background work");

        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), Ct);
        service.Prewarm(VoiceThemes.Host, ["After shutdown"]); // still never throws
        await Task.Delay(50, Ct);
        Assert.Equal(2, _synth.Calls);
    }
}
