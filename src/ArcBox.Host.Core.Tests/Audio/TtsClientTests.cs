using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using ArcBox.Host.Core.Audio;
using ArcBox.Host.Core.Tests.Support;

namespace ArcBox.Host.Core.Tests.Audio;

public class TtsClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TtsSession Session = new(new Uri("http://tv.test:5080"), "ABCD", "token-1");

    /// <summary>Answers every request with <paramref name="respond"/>, recording what was sent.</summary>
    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, string? Body)> Seen { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Seen.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
            return await respond(request, ct);
        }
    }

    [Fact]
    public void Request_trims_and_caps_the_text()
    {
        Assert.Equal(new TtsRequestBody("ABCD", "token-1", "Hello there.", "redline.questions"),
            TtsClient.Request(Session, "  Hello there. \n", "redline.questions"));
        Assert.Null(TtsClient.Request(Session, "   ", "redline.questions"));
        Assert.Null(TtsClient.Request(Session, new string('a', TtsClient.MaxChars + 1), "redline.questions"));
        Assert.NotNull(TtsClient.Request(Session, new string('a', TtsClient.MaxChars), "redline.questions"));
    }

    [Fact]
    public async Task Posts_the_session_text_and_theme_and_returns_the_mp3()
    {
        var mp3 = new byte[] { 0xFF, 0xFB, 0x90, 0x00 };
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(mp3) { Headers = { ContentType = new("audio/mpeg") } },
        }));
        var client = new TtsClient(new HttpClient(handler));

        var result = await client.SynthesizeAsync(Session, "Lights out!", "redline.announcer", Ct);

        Assert.Equal(mp3, Assert.IsType<TtsAudio>(result).Mp3);
        var (request, body) = Assert.Single(handler.Seen);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("http://tv.test:5080/api/tts", request.RequestUri!.ToString());
        using var json = JsonDocument.Parse(body!);
        Assert.Equal("ABCD", json.RootElement.GetProperty("code").GetString());
        Assert.Equal("token-1", json.RootElement.GetProperty("hostToken").GetString());
        Assert.Equal("Lights out!", json.RootElement.GetProperty("text").GetString());
        Assert.Equal("redline.announcer", json.RootElement.GetProperty("voice").GetString());
    }

    [Fact]
    public async Task Gives_up_after_the_timeout()
    {
        var handler = new FakeHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new TtsClient(new HttpClient(handler)) { Timeout = TimeSpan.FromMilliseconds(200) };

        var sw = Stopwatch.StartNew();
        var result = await client.SynthesizeAsync(Session, "Slow", "redline.questions", Ct);

        Assert.Equal(TtsFailure.Timeout, Assert.IsType<TtsFailed>(result).Failure);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"took {sw.Elapsed}");
    }

    [Fact]
    public async Task Network_errors_and_empty_bodies_are_failures_not_exceptions()
    {
        var broken = new TtsClient(new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("refused"))));
        Assert.Equal(TtsFailure.Network, Assert.IsType<TtsFailed>(await broken.SynthesizeAsync(Session, "x", "a.b", Ct)).Failure);

        var empty = new TtsClient(new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)))));
        Assert.Equal(TtsFailure.Unavailable, Assert.IsType<TtsFailed>(await empty.SynthesizeAsync(Session, "x", "a.b", Ct)).Failure);
    }

    [Fact]
    public async Task Text_the_server_would_refuse_is_never_sent()
    {
        var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        var client = new TtsClient(new HttpClient(handler));

        var result = await client.SynthesizeAsync(Session, new string('a', 400), "a.b", Ct);

        Assert.Equal(TtsFailure.BadRequest, Assert.IsType<TtsFailed>(result).Failure);
        Assert.Empty(handler.Seen);
    }

    [Fact]
    public async Task Retry_after_is_read_from_a_429()
    {
        var handler = new FakeHandler((_, _) =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(12));
            return Task.FromResult(r);
        });
        var failed = Assert.IsType<TtsFailed>(await new TtsClient(new HttpClient(handler)).SynthesizeAsync(Session, "x", "a.b", Ct));

        Assert.Equal(TtsFailure.RateLimited, failed.Failure);
        Assert.Equal(TimeSpan.FromSeconds(12), failed.RetryAfter);
        Assert.Equal(429, failed.Status);
    }

    // ---- Against the real server (no Google key configured: /api/tts is a 503 here) --------------------------

    private static async Task<(HostServerFactory F, TtsSession Session)> RoomAsync()
    {
        var f = new HostServerFactory();
        var host = f.CreateHostClient();
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        return (f, new TtsSession(f.Server.BaseAddress, room.Code, room.HostToken));
    }

    [Fact]
    public async Task Real_server_without_a_key_answers_unavailable()
    {
        var (f, session) = await RoomAsync();
        using var _ = f;
        var client = new TtsClient(f.CreateClient());

        var failed = Assert.IsType<TtsFailed>(await client.SynthesizeAsync(session, "Which U.S. state has the longest coastline?", "redline.questions", Ct));

        Assert.Equal(TtsFailure.Unavailable, failed.Failure);
        Assert.Equal(503, failed.Status);
    }

    [Fact]
    public async Task Real_server_refuses_a_wrong_host_token()
    {
        var (f, session) = await RoomAsync();
        using var _ = f;
        var client = new TtsClient(f.CreateClient());

        var failed = Assert.IsType<TtsFailed>(await client.SynthesizeAsync(session with { HostToken = "nope" }, "Hello", "arcbox.host", Ct));

        Assert.Equal(TtsFailure.Unauthorized, failed.Failure);
        Assert.Null(await client.VoicesAsync(session with { HostToken = "nope" }, Ct));
    }

    [Fact]
    public async Task Real_server_lists_the_voice_themes_with_fallback_hints()
    {
        var (f, session) = await RoomAsync();
        using var _ = f;
        var client = new TtsClient(f.CreateClient());

        var voices = await client.VoicesAsync(session, Ct);

        Assert.NotNull(voices);
        Assert.Equal(1.1f, voices["redline.announcer"].Rate);
        Assert.Equal(1.05f, voices["redline.announcer"].Pitch);
        Assert.Equal(1f, voices["redline.questions"].Rate);
        Assert.True(voices.ContainsKey("arcbox.host"));
    }

    [Fact]
    public async Task Voices_from_an_unreachable_server_are_null()
    {
        var client = new TtsClient(new HttpClient(new FakeHandler((_, _) => throw new HttpRequestException("refused"))));
        Assert.Null(await client.VoicesAsync(Session, Ct));

        var garbage = new TtsClient(new HttpClient(new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("not json", Encoding.UTF8, "application/json"),
        }))));
        Assert.Null(await garbage.VoicesAsync(Session, Ct));
    }
}
