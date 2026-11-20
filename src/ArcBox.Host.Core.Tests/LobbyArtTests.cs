using System.Net;
using ArcBox.Host.Core;

namespace ArcBox.Host.Core.Tests;

/// <summary>The TV lobby's card art (#405): one image fetched by itself, or the text card.</summary>
public sealed class LobbyArtTests
{
    private static readonly byte[] Png = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    private sealed class Server(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
    {
        public int Requests;
        public readonly List<string> Paths = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Requests);
            Paths.Add(request.RequestUri!.AbsolutePath);
            return answer(request);
        }
    }

    private static (LobbyArt Art, Server Server) With(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer, TimeSpan? timeout = null)
    {
        var server = new Server(answer);
        return (new LobbyArt(new HttpClient(server) { BaseAddress = new Uri("http://arcbox.test:5000/") }, timeout), server);
    }

    private static Task<HttpResponseMessage> Answer(HttpStatusCode code, byte[] body)
        => Task.FromResult(new HttpResponseMessage(code) { Content = new ByteArrayContent(body) });

    [Fact]
    public async Task An_image_the_server_has_is_fetched_once_and_kept()
    {
        var (art, server) = With(_ => Answer(HttpStatusCode.OK, Png));
        const string url = "/content/meridian/base/abc123/ui/logo.f73a.png";
        Assert.Equal(Png, await art.Get(url));
        Assert.Equal(Png, await art.Get(url));
        Assert.Equal(1, server.Requests);   // immutable URLs: fetched once
        Assert.Equal([url], server.Paths);
    }

    [Fact]
    public async Task No_art_named_means_no_request_and_the_text_card()
    {
        var (art, server) = With(_ => Answer(HttpStatusCode.OK, Png));
        Assert.Null(await art.Get(null));
        Assert.Null(await art.Get(""));
        Assert.Equal(0, server.Requests);
    }

    [Fact]
    public async Task A_missing_file_or_something_that_isnt_a_png_gives_the_text_card()
    {
        Assert.Null(await With(_ => Answer(HttpStatusCode.NotFound, [])).Art.Get("/content/x/base/h/missing.png"));
        Assert.Null(await With(_ => Answer(HttpStatusCode.OK, "<html>oops</html>"u8.ToArray())).Art.Get("/content/x/base/h/a.png"));
    }

    [Fact]
    public async Task A_failed_or_slow_fetch_gives_the_text_card_without_throwing()
    {
        Assert.Null(await With(_ => throw new HttpRequestException("refused")).Art.Get("/content/x/base/h/a.png"));
        var (slow, _) = With(async request =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Png) };
        }, timeout: TimeSpan.FromMilliseconds(100));
        Assert.Null(await slow.Get("/content/x/base/h/a.png"));
    }
}
