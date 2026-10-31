using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Rooms;
using ArcBox.Server.Speech;
using ArcBox.Server.Tests.Redline;
using ArcBox.Server.Tests.Speech.Support;
using ArcBox.Server.Tests.Support;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Redline.Logic;

namespace ArcBox.Server.Tests.Speech;

public class SpeechEndpointTests : IAsyncDisposable
{
    private static readonly byte[] Mp3 = [0xFF, 0xFB, 0x90, 0x64, 0x01, 0x02];

    private readonly TempDir _dir = new();
    private readonly ArcServerFactory _root = new();
    private readonly FakeHandler _google = new() { Respond = _ => SpeechKit.GoogleOk(Mp3) };

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        _dir.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>The real server with a (fake) key, a temp cache, the fake Google handler and <paramref name="configure"/>.</summary>
    private WebApplicationFactory<Program> Server(string? key = SpeechKit.Key, Action<SpeechOptions>? configure = null) =>
        _root.WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.PostConfigure<SpeechOptions>(o =>
            {
                o.Google.ApiKey = key;
                o.CacheDir = _dir.Path;
                configure?.Invoke(o);
            });
            s.AddHttpClient<ISpeechSynthesizer, GoogleTtsClient>().ConfigurePrimaryHttpMessageHandler(() => _google);
        }));

    private static async Task<CreateRoomResult> NewRoom(WebApplicationFactory<Program> f)
    {
        var (_, result) = await f.Services.GetRequiredService<RoomManager>().CreateRoomAsync("host-" + Guid.NewGuid().ToString("N"));
        return result;
    }

    private static Task<HttpResponseMessage> Tts(HttpClient http, CreateRoomResult room, string? text, string? voice = null, string? token = null) =>
        http.PostAsJsonAsync("/api/tts", new { code = room.Code, hostToken = token ?? room.HostToken, text, voice }, Ct);

    [Fact]
    public async Task Tts_returns_mp3()
    {
        var f = Server();
        using var http = f.CreateClient();
        var room = await NewRoom(f);

        using var response = await Tts(http, room, "What is the capital of Australia?");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("audio/mpeg", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.Private);
        Assert.Equal(TimeSpan.FromDays(1), response.Headers.CacheControl.MaxAge);
        Assert.Equal(Mp3, await response.Content.ReadAsByteArrayAsync(Ct));

        // Same text again, lower-case code, explicit theme: served from the cache.
        using var again = await http.PostAsJsonAsync("/api/tts",
            new { code = room.Code.ToLowerInvariant(), hostToken = room.HostToken, text = "What is the capital of Australia?", voice = "arcbox.host" }, Ct);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Single(_google.Requests);
    }

    [Fact]
    public async Task Tts_requires_valid_host_token()
    {
        var f = Server();
        using var http = f.CreateClient();
        var room = await NewRoom(f);
        var other = await NewRoom(f);

        using var wrongToken = await Tts(http, room, "Hello", token: other.HostToken);
        using var unknownRoom = await Tts(http, room with { Code = "ZZZZ" }, "Hello");
        using var missing = await http.PostAsJsonAsync("/api/tts", new { text = "Hello" }, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownRoom.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        Assert.Empty(_google.Requests);
    }

    [Fact]
    public async Task Tts_rejects_empty_and_long_text()
    {
        var f = Server();
        using var http = f.CreateClient();
        var room = await NewRoom(f);

        foreach (var text in new[] { null, "", "   \n ", new string('a', 301) })
        {
            using var response = await Tts(http, room, text);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        Assert.Empty(_google.Requests);
        using var longest = await Tts(http, room, "  " + new string('a', 300) + "  "); // 300 after trimming
        Assert.Equal(HttpStatusCode.OK, longest.StatusCode);
    }

    [Fact]
    public async Task Unknown_voice_theme_is_400()
    {
        var f = Server();
        using var http = f.CreateClient();
        var room = await NewRoom(f);

        using var unknown = await Tts(http, room, "Hello", voice: "nobody.voice");
        using var provider = await Tts(http, room, "Hello", voice: "en-US-Chirp3-HD-Charon"); // a provider name is not a theme
        using var known = await Tts(http, room, "Hello", voice: RedlineVoices.Questions);

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, provider.StatusCode);
        Assert.Equal(HttpStatusCode.OK, known.StatusCode);
        Assert.Single(_google.Requests);
    }

    [Fact]
    public async Task Tts_rate_limited_per_room()
    {
        var f = Server(configure: o => o.RequestsPerMinutePerRoom = 3);
        using var http = f.CreateClient();
        var room = await NewRoom(f);
        var other = await NewRoom(f);

        for (var i = 0; i < 3; i++)
        {
            using var ok = await Tts(http, room, "Line " + i);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var limited = await Tts(http, room, "One too many");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero);

        using var otherRoom = await Tts(http, other, "Different room");
        Assert.Equal(HttpStatusCode.OK, otherRoom.StatusCode);
    }

    [Fact]
    public async Task Tts_returns_503_when_disabled_or_unconfigured()
    {
        var unconfigured = Server(key: null);
        using (var http = unconfigured.CreateClient())
        {
            using var response = await Tts(http, await NewRoom(unconfigured), "Hello");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        }

        var disabled = Server(configure: o => o.Enabled = false);
        using (var http = disabled.CreateClient())
        {
            using var response = await Tts(http, await NewRoom(disabled), "Hello");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(TimeSpan.FromSeconds(60), response.Headers.RetryAfter?.Delta);
        }

        Assert.Empty(_google.Requests);
    }

    // Review Focus 1: Google unreachable mid-race; the TV gets a quick 503 and falls back.
    [Fact]
    public async Task Tts_returns_503_when_google_fails()
    {
        var f = Server();
        using var http = f.CreateClient();
        var room = await NewRoom(f);

        _google.Respond = _ => SpeechKit.GoogleError(HttpStatusCode.Forbidden, $"API key {SpeechKit.Key} not valid");
        using var refused = await Tts(http, room, "Hello");
        _google.Respond = _ => throw new HttpRequestException("Name or service not known");
        using var unreachable = await Tts(http, room, "Hello");

        foreach (var response in new[] { refused, unreachable })
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync(Ct);
            Assert.DoesNotContain(SpeechKit.Key, body);
            Assert.DoesNotContain("not valid", body);
        }

        // Not cached: once Google is back, the same text is synthesised.
        _google.Respond = _ => SpeechKit.GoogleOk(Mp3);
        using var recovered = await Tts(http, room, "Hello");
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
        Assert.Equal(3, _google.Requests.Count);
    }

    [Fact]
    public async Task Voices_endpoint_lists_themes_without_provider_details()
    {
        var f = Server();
        using var http = f.CreateClient();
        var room = await NewRoom(f);

        using var anonymous = await http.GetAsync("/api/tts/voices", Ct);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/tts/voices");
        request.Headers.Add(SpeechEndpoints.RoomHeader, room.Code);
        request.Headers.Add(SpeechEndpoints.HostTokenHeader, room.HostToken);
        using var response = await http.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync(Ct);
        Assert.DoesNotContain("Chirp", raw);
        Assert.DoesNotContain("en-US", raw);
        Assert.DoesNotContain(SpeechKit.Key, raw);

        using var doc = JsonDocument.Parse(raw);
        var voices = doc.RootElement.EnumerateArray().ToList();
        Assert.Equal(["arcbox.host", "redline.announcer", "redline.questions", .. global::Tactics.Logic.TacticsVoices.All.Select(t => t.Id)],
            voices.Select(v => v.GetProperty("id").GetString()));
        Assert.All(voices, v => Assert.Equal(["description", "fallback", "id"], v.EnumerateObject().Select(p => p.Name).Order()));
        var announcer = voices[1];
        Assert.Equal(RedlineVoices.All[0].Description, announcer.GetProperty("description").GetString());
        Assert.Equal(1.1f, announcer.GetProperty("fallback").GetProperty("rate").GetSingle());
        Assert.Equal(1.05f, announcer.GetProperty("fallback").GetProperty("pitch").GetSingle());
    }

    [Fact]
    public async Task Redline_questions_are_prewarmed_when_speech_is_available()
    {
        var f = Server();
        var source = f.Services.GetRequiredService<IQuestionSource>();

        var questions = source.Take(3);

        Assert.IsType<ArcBox.Server.Games.Redline.NarratedQuestionSource>(source);
        await SpeechKit.Until(() => Directory.Exists(_dir.Path) && Directory.GetFiles(_dir.Path, "*.mp3").Length == 3,
            "three prewarmed clips");
        Assert.Equal(3, _google.Requests.Count);
        Assert.Equal(3, questions.Count);
    }
}
