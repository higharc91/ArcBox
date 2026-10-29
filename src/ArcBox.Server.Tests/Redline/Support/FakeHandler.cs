namespace ArcBox.Server.Tests.Redline;

/// <summary>Records every request with a timestamp and answers through <see cref="Respond"/>.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } =
        _ => new HttpResponseMessage(System.Net.HttpStatusCode.OK);

    public List<(HttpRequestMessage Req, DateTimeOffset At)> Requests { get; } = [];

    public TimeProvider? Clock { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add((request, Clock?.GetUtcNow() ?? DateTimeOffset.UtcNow));
        }

        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Respond(request));
    }
}
