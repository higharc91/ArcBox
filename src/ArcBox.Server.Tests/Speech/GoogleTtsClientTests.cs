using System.Net;
using System.Text.Json;
using ArcBox.Server.Speech;
using ArcBox.Server.Tests.Redline;
using ArcBox.Server.Tests.Speech.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ArcBox.Server.Tests.Speech;

public class GoogleTtsClientTests : IDisposable
{
    private readonly TempDir _dir = new();
    private readonly LogSink _logs = new();
    private readonly FakeHandler _google = new();
    private readonly List<string> _bodies = [];

    public void Dispose() => _dir.Dispose();

    /// <summary>The real DI wiring (AddSpeech) with the fake handler as the primary handler and every log captured.</summary>
    private ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Speech:Google:ApiKey"] = SpeechKit.Key,
                ["Speech:CacheDir"] = _dir.Path,
            })
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(_logs));
        services.AddSpeech(config);
        services.AddHttpClient<ISpeechSynthesizer, GoogleTtsClient>().ConfigurePrimaryHttpMessageHandler(() => _google);
        return services.BuildServiceProvider();
    }

    private void RespondWith(Func<HttpRequestMessage, HttpResponseMessage> respond) =>
        _google.Respond = req =>
        {
            lock (_bodies)
                _bodies.Add(req.Content!.ReadAsStringAsync().GetAwaiter().GetResult());
            return respond(req);
        };

    [Fact]
    public async Task Google_request_shape()
    {
        using var sp = Build();
        RespondWith(_ => SpeechKit.GoogleOk([1, 2, 3]));

        await sp.GetRequiredService<ISpeechService>().SynthesizeAsync("arcbox.host", "  Hello there.  ", TestContext.Current.CancellationToken);

        var (req, _) = Assert.Single(_google.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal($"https://texttospeech.googleapis.com/v1/text:synthesize?key={SpeechKit.Key}", req.RequestUri!.ToString());
        Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);

        using var body = JsonDocument.Parse(Assert.Single(_bodies));
        var root = body.RootElement;
        Assert.Equal("Hello there.", root.GetProperty("input").GetProperty("text").GetString());
        Assert.Equal("en-US", root.GetProperty("voice").GetProperty("languageCode").GetString());
        Assert.Equal("en-US-Chirp3-HD-Puck", root.GetProperty("voice").GetProperty("name").GetString());
        var audioConfig = root.GetProperty("audioConfig");
        Assert.Equal("MP3", audioConfig.GetProperty("audioEncoding").GetString());
        Assert.Single(audioConfig.EnumerateObject()); // nothing but the encoding for the default theme
        Assert.Equal(3, root.EnumerateObject().Count());

        // A transport error that quotes the URL (key included) must still not leak the key into logs or exceptions.
        _google.Respond = req => throw new HttpRequestException($"Connection refused ({req.RequestUri})");
        var e = await Assert.ThrowsAsync<SpeechSynthesisException>(() =>
            sp.GetRequiredService<ISpeechService>().SynthesizeAsync("arcbox.host", "Another line", TestContext.Current.CancellationToken));

        Assert.DoesNotContain(SpeechKit.Key, e.ToString());
        Assert.Contains("REDACTED", e.Message);
        Assert.NotEmpty(_logs.Entries);
        Assert.Contains(_logs.Texts, t => t.Contains("Speech synthesis failed", StringComparison.Ordinal));
        Assert.DoesNotContain(_logs.Texts, t => t.Contains(SpeechKit.Key, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Decodes_audioContent()
    {
        using var sp = Build();
        byte[] mp3 = [0xFF, 0xFB, 0x90, 0x64, 0x00];
        RespondWith(_ => SpeechKit.GoogleOk(mp3));
        var client = sp.GetRequiredService<ISpeechSynthesizer>();
        var voice = new ResolvedVoice("en-US-Chirp3-HD-Charon", "en-US", null, null, null);

        Assert.Equal(mp3, await client.SynthesizeAsync("Hi", voice, TestContext.Current.CancellationToken));

        RespondWith(_ => SpeechKit.GoogleError(HttpStatusCode.BadRequest, "This voice does not support pitch parameters at this time."));
        var e = await Assert.ThrowsAsync<SpeechSynthesisException>(() => client.SynthesizeAsync("Hi", voice, TestContext.Current.CancellationToken));
        Assert.Equal("Google TTS HTTP 400: INVALID_ARGUMENT This voice does not support pitch parameters at this time.", e.Message);

        RespondWith(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        await Assert.ThrowsAsync<SpeechSynthesisException>(() => client.SynthesizeAsync("Hi", voice, TestContext.Current.CancellationToken));

        RespondWith(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"audioContent\":\"not base64!\"}") });
        await Assert.ThrowsAsync<SpeechSynthesisException>(() => client.SynthesizeAsync("Hi", voice, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Unsupported_audioConfig_fields_are_not_sent()
    {
        using var sp = Build(
            ("Speech:Voices:test.chirp:Voice", "en-US-Chirp3-HD-Charon"),
            ("Speech:Voices:test.chirp:SpeakingRate", "1.25"),
            ("Speech:Voices:test.chirp:Pitch", "2"),
            ("Speech:Voices:test.chirp:VolumeGainDb", "3"),
            ("Speech:Voices:test.neural:Voice", "en-US-Neural2-D"),
            ("Speech:Voices:test.neural:SpeakingRate", "1.25"),
            ("Speech:Voices:test.neural:Pitch", "2"),
            ("Speech:Voices:test.neural:VolumeGainDb", "3"));
        RespondWith(_ => SpeechKit.GoogleOk([1]));
        var speech = sp.GetRequiredService<ISpeechService>();

        await speech.SynthesizeAsync("test.chirp", "One", TestContext.Current.CancellationToken);
        await speech.SynthesizeAsync("test.chirp", "Two", TestContext.Current.CancellationToken);
        await speech.SynthesizeAsync("test.neural", "Three", TestContext.Current.CancellationToken);

        var chirp = JsonDocument.Parse(_bodies[0]).RootElement.GetProperty("audioConfig");
        Assert.Equal(["audioEncoding", "speakingRate"], chirp.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(1.25, chirp.GetProperty("speakingRate").GetDouble());

        var neural = JsonDocument.Parse(_bodies[2]).RootElement.GetProperty("audioConfig");
        Assert.Equal(["audioEncoding", "pitch", "speakingRate", "volumeGainDb"], neural.EnumerateObject().Select(p => p.Name).Order());

        // One warning for the Chirp theme, however many requests it serves; none for the Neural2 theme.
        var warnings = _logs.Entries.Where(e => e.Level == LogLevel.Warning && e.Text.Contains("does not support")).ToList();
        var warning = Assert.Single(warnings);
        Assert.Contains("test.chirp", warning.Text);
        Assert.Contains("Pitch, VolumeGainDb", warning.Text);
    }

    [Theory]
    [InlineData("en-US-Chirp3-HD-Charon", true, false, false)]
    [InlineData("en-GB-Chirp3-HD-Puck", true, false, false)]
    [InlineData("en-US-Chirp-HD-F", false, false, false)]
    [InlineData("en-US-Neural2-D", true, true, true)]
    [InlineData("en-US-Wavenet-A", true, true, true)]
    [InlineData("en-US-Standard-B", true, true, true)]
    [InlineData("en-US-Studio-O", false, false, false)]
    public void Voice_capabilities_by_family(string voice, bool rate, bool pitch, bool gain) =>
        Assert.Equal(new VoiceCapabilities(rate, pitch, gain), VoiceCapabilities.For(voice));
}
