using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ArcBox.Server.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ArcBox.Server.Tests.Speech.Support;

/// <summary>Collects every log message (with exception text) from every category.</summary>
public sealed class LogSink : ILoggerProvider
{
    public ConcurrentQueue<(string Category, LogLevel Level, string Text)> Entries { get; } = new();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public IEnumerable<string> Texts => Entries.Select(e => e.Text);

    public void Dispose()
    {
    }

    private sealed class Logger(LogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            sink.Entries.Enqueue((category, logLevel, formatter(state, exception) + (exception is null ? "" : " | " + exception)));
    }
}

/// <summary>A synthesizer whose behaviour each test sets; counts calls and can hold them until released.</summary>
public sealed class ScriptedSynthesizer : ISpeechSynthesizer
{
    private int _calls;
    private int _inFlight;
    private int _maxInFlight;

    public Func<string, ResolvedVoice, CancellationToken, Task<byte[]>> Behaviour { get; set; } =
        (text, _, _) => Task.FromResult(Encoding.UTF8.GetBytes("mp3:" + text));

    public int Calls => Volatile.Read(ref _calls);

    public int MaxInFlight => Volatile.Read(ref _maxInFlight);

    public ConcurrentQueue<(string Text, ResolvedVoice Voice)> Requests { get; } = new();

    public async Task<byte[]> SynthesizeAsync(string text, ResolvedVoice voice, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        Requests.Enqueue((text, voice));
        var now = Interlocked.Increment(ref _inFlight);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxInFlight)) && Interlocked.CompareExchange(ref _maxInFlight, now, seen) != seen)
        {
        }

        try
        {
            return await Behaviour(text, voice, ct);
        }
        finally
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}

/// <summary>A temp directory deleted on dispose.</summary>
public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "arcbox-tts-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}

public static class SpeechKit
{
    public const string Key = "test-secret-key-123";

    public static SpeechOptions Options(string cacheDir, string? key = Key) => new()
    {
        CacheDir = cacheDir,
        Google = { ApiKey = key },
    };

    /// <summary>A SpeechService over <paramref name="synth"/> with the platform themes plus <paramref name="extra"/>.</summary>
    public static (SpeechService Service, SpeechCache Cache, SpeechUsage Usage) Service(
        ISpeechSynthesizer synth,
        SpeechOptions options,
        ILoggerFactory? logs = null,
        params ArcBox.Sdk.Speech.VoiceTheme[] extra)
    {
        logs ??= NullLoggerFactory.Instance;
        var opts = Microsoft.Extensions.Options.Options.Create(options);
        var registrations = new List<VoiceThemeRegistration> { new([SpeechServiceCollectionExtensions.HostTheme]) };
        if (extra.Length > 0)
            registrations.Add(new VoiceThemeRegistration(extra));
        var registry = new VoiceThemeRegistry(registrations, opts, logs.CreateLogger<VoiceThemeRegistry>());
        var cache = new SpeechCache(opts, logs.CreateLogger<SpeechCache>());
        var usage = new SpeechUsage();
        var service = new SpeechService(registry, synth, cache, usage, TimeProvider.System, opts, logs.CreateLogger<SpeechService>());
        return (service, cache, usage);
    }

    /// <summary>A Google success response carrying <paramref name="audio"/>.</summary>
    public static HttpResponseMessage GoogleOk(byte[] audio) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new { audioContent = Convert.ToBase64String(audio) }), Encoding.UTF8, "application/json"),
    };

    public static HttpResponseMessage GoogleError(HttpStatusCode status, string message) => new(status)
    {
        Content = new StringContent(
            JsonSerializer.Serialize(new { error = new { code = (int)status, message, status = "INVALID_ARGUMENT" } }),
            Encoding.UTF8,
            "application/json"),
    };

    /// <summary>Polls <paramref name="condition"/> for up to 5 s.</summary>
    public static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Timed out waiting for " + what);
            await Task.Delay(10);
        }
    }
}

/// <summary>Installed by <see cref="Tests.Support.ArcServerFactory"/>: fails every request, so no test can reach Google.</summary>
public sealed class NoNetworkHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("Tests must not call the network.");
}
