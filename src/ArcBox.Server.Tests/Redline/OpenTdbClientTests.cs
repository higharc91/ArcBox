using System.Net;
using System.Text;
using ArcBox.Server.Games.Redline;
using Redline.Logic;

namespace ArcBox.Server.Tests.Redline;

public class OpenTdbClientTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Redline", "Fixtures", name));

    private static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (OpenTdbClient Client, FakeHandler Handler) Create(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new FakeHandler { Respond = respond };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://opentdb.com") };
        return (new OpenTdbClient(http), handler);
    }

    private static string Enc(string s) => Uri.EscapeDataString(s);

    private static string Q(string type, string difficulty, string category, string text, string correct, params string[] incorrect) =>
        $$"""
        {"type":"{{Enc(type)}}","difficulty":"{{Enc(difficulty)}}","category":"{{Enc(category)}}","question":"{{Enc(text)}}","correct_answer":"{{Enc(correct)}}","incorrect_answers":[{{string.Join(",", incorrect.Select(i => $"\"{Enc(i)}\""))}}]}
        """;

    private static string Batch(params string[] questions) =>
        $$"""{"response_code":0,"results":[{{string.Join(",", questions)}}]}""";

    [Fact]
    public async Task Decodes_fixture_including_unicode_and_quotes()
    {
        var (client, _) = Create(_ => Json(Fixture("opentdb-ok.json")));

        var result = await client.FetchAsync(null, 50, CancellationToken.None);

        var ok = Assert.IsType<OpenTdbResult.Ok>(result);
        Assert.Equal(3, ok.Questions.Count);

        var first = ok.Questions[0];
        Assert.Equal("Video Games", first.Category);
        Assert.Equal(Difficulty.Medium, first.Difficulty);
        Assert.Equal("In \"Portal\", what is the name of the AI that guides the player?", first.Text);
        Assert.Equal("GLaDOS", first.Correct);
        Assert.Equal(["Wheatley", "Cortana", "SHODAN"], first.Incorrect);
        Assert.Equal(QuestionOrigin.OpenTdb, first.Origin);

        var second = ok.Questions[1];
        Assert.Equal(Difficulty.Easy, second.Difficulty);
        Assert.Equal("Geography", second.Category);
        Assert.Contains("Café", second.Text);

        var third = ok.Questions[2];
        Assert.Equal(Difficulty.Hard, third.Difficulty);
        Assert.Equal("Computers", third.Category);
        Assert.All(ok.Questions, q => Assert.Equal(3, q.Incorrect.Count));
    }

    [Theory]
    [InlineData(3, "TokenEmpty")]
    [InlineData(4, "TokenEmpty")]
    [InlineData(5, "RateLimited")]
    [InlineData(1, "Failed")]
    [InlineData(2, "Failed")]
    public async Task Maps_response_codes(int code, string expected)
    {
        var (client, _) = Create(_ => Json($$"""{"response_code":{{code}},"results":[]}"""));

        var result = await client.FetchAsync("tok", 50, CancellationToken.None);

        Assert.Equal(expected, result.GetType().Name);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimited")]
    [InlineData(HttpStatusCode.InternalServerError, "Failed")]
    public async Task Maps_http_429_500(HttpStatusCode status, string expected)
    {
        var (client, _) = Create(_ => Json("{}", status));

        var result = await client.FetchAsync(null, 50, CancellationToken.None);

        Assert.Equal(expected, result.GetType().Name);
    }

    [Fact]
    public async Task Maps_timeout_to_failed_when_caller_not_cancelled()
    {
        var (client, _) = Create(_ => throw new TaskCanceledException("timed out"));

        var result = await client.FetchAsync(null, 50, CancellationToken.None);

        Assert.IsType<OpenTdbResult.Failed>(result);
    }

    [Fact]
    public async Task Maps_real_httpclient_timeout_to_failed()
    {
        var handler = new SlowHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://opentdb.com"), Timeout = TimeSpan.FromMilliseconds(50) };
        var client = new OpenTdbClient(http);

        var result = await client.FetchAsync(null, 50, CancellationToken.None);

        Assert.IsType<OpenTdbResult.Failed>(result);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var (client, _) = Create(_ => Json(Fixture("opentdb-ok.json")));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.FetchAsync(null, 50, cts.Token));
    }

    [Fact]
    public async Task Maps_http_exception_and_bad_json_to_failed()
    {
        var (thrower, _) = Create(_ => throw new HttpRequestException("boom"));
        Assert.IsType<OpenTdbResult.Failed>(await thrower.FetchAsync(null, 50, CancellationToken.None));

        var (garbage, _) = Create(_ => Json("not json at all"));
        Assert.IsType<OpenTdbResult.Failed>(await garbage.FetchAsync(null, 50, CancellationToken.None));
    }

    [Fact]
    public async Task Request_uses_amount_type_encode_and_token()
    {
        var (client, handler) = Create(_ => Json(Fixture("opentdb-ok.json")));

        await client.FetchAsync("abc123", 50, CancellationToken.None);
        await client.FetchAsync(null, 20, CancellationToken.None);

        Assert.Equal(2, handler.Requests.Count);
        var withToken = handler.Requests[0].Req.RequestUri!;
        Assert.Equal("/api.php", withToken.AbsolutePath);
        Assert.Equal("?amount=50&type=multiple&encode=url3986&token=abc123", withToken.Query);
        Assert.Equal("?amount=20&type=multiple&encode=url3986", handler.Requests[1].Req.RequestUri!.Query);
    }

    [Fact]
    public async Task RequestToken_returns_token_and_uses_request_command()
    {
        var (client, handler) = Create(_ =>
            Json("""{"response_code":0,"response_message":"Token Generated Successfully!","token":"abc"}"""));

        var token = await client.RequestTokenAsync(CancellationToken.None);

        Assert.Equal("abc", token);
        var uri = handler.Requests.Single().Req.RequestUri!;
        Assert.Equal("/api_token.php", uri.AbsolutePath);
        Assert.Equal("?command=request", uri.Query);
    }

    [Fact]
    public async Task RequestToken_returns_null_on_bad_code_or_failure()
    {
        var (badCode, _) = Create(_ => Json("""{"response_code":2,"response_message":"x","token":"abc"}"""));
        Assert.Null(await badCode.RequestTokenAsync(CancellationToken.None));

        var (http500, _) = Create(_ => Json("{}", HttpStatusCode.InternalServerError));
        Assert.Null(await http500.RequestTokenAsync(CancellationToken.None));

        var (thrower, _) = Create(_ => throw new HttpRequestException("boom"));
        Assert.Null(await thrower.RequestTokenAsync(CancellationToken.None));

        var (garbage, _) = Create(_ => Json("<html>"));
        Assert.Null(await garbage.RequestTokenAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Malformed_questions_are_dropped_rest_kept()
    {
        var body = Batch(
            Q("multiple", "easy", "Geography", "Only two wrong?", "A", "B", "C"),
            Q("multiple", "easy", "Geography", "   ", "A", "B", "C", "D"),
            Q("multiple", "easy", "Geography", "Case clash?", "Paris", "paris", "Rome", "Oslo"),
            Q("multiple", "hard", "Science: Nature", "Good one?", "Yes", "No", "Maybe", "Never"));
        var (client, _) = Create(_ => Json(body));

        var result = await client.FetchAsync(null, 50, CancellationToken.None);

        var ok = Assert.IsType<OpenTdbResult.Ok>(result);
        var only = Assert.Single(ok.Questions);
        Assert.Equal("Good one?", only.Text);
        Assert.Equal("Nature", only.Category);
        Assert.Equal(Difficulty.Hard, only.Difficulty);
    }

    [Fact]
    public async Task Other_malformed_shapes_are_dropped()
    {
        var body = Batch(
            Q("boolean", "easy", "Geography", "True or false?", "True", "False", "x", "y"),
            Q("multiple", "impossible", "Geography", "Bad difficulty?", "A", "B", "C", "D"),
            Q("multiple", "easy", "Geography", "Blank answer?", "A", "B", " ", "D"),
            Q("multiple", "easy", "Geography", "Dup wrong?", "A", "B", "b", "D"),
            Q("multiple", "easy", "Geography", "Four wrong?", "A", "B", "C", "D", "E"),
            Q("multiple", "easy", "General Knowledge", "Fine?", "A", "B", "C", "D"));
        var (client, _) = Create(_ => Json(body));

        var ok = Assert.IsType<OpenTdbResult.Ok>(await client.FetchAsync(null, 50, CancellationToken.None));

        Assert.Equal("Fine?", Assert.Single(ok.Questions).Text);
        Assert.Equal("General Knowledge", ok.Questions[0].Category);
    }

    private sealed class SlowHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
