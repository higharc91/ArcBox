using System.Net;
using System.Text;
using ArcBox.Server.Games.Redline;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Redline;

/// <summary>
/// Fills a real <see cref="OpenTdbQuestionPool"/> with an exact number of unique questions by running its refill loop
/// against a fake OpenTDB on a <see cref="FakeTimeProvider"/>, then stops the loop so the count stays put.
/// </summary>
internal static class SeededPool
{
    private const int Batch = 50;

    /// <param name="count">A positive multiple of 50 (OpenTDB batch size).</param>
    public static async Task<OpenTdbQuestionPool> CreateAsync(int count)
    {
        if (count <= 0 || count % Batch != 0)
            throw new ArgumentOutOfRangeException(nameof(count), "must be a positive multiple of 50");

        var fetch = -1;
        var handler = new FakeHandler
        {
            Respond = req => req.RequestUri!.AbsolutePath == "/api_token.php"
                ? Json("""{"response_code":0,"token":"seed"}""")
                : Json($$"""{"response_code":0,"results":[{{Questions(Interlocked.Increment(ref fetch))}}]}"""),
        };
        var options = new OpenTdbOptions { Target = count, LowWater = count };
        var time = new FakeTimeProvider();
        var pool = new OpenTdbQuestionPool(
            new OpenTdbClient(new HttpClient(handler) { BaseAddress = new Uri("https://opentdb.com/") }),
            Options.Create(options), time, NullLogger<OpenTdbQuestionPool>.Instance);

        await pool.StartAsync(CancellationToken.None);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (pool.Count < count)
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out seeding the pool: {pool.Count}/{count}");
            time.Advance(options.MinRequestSpacing); // wakes whichever delay the loop is parked on
            await Task.Delay(1);
        }

        await pool.StopAsync(CancellationToken.None);
        return pool;
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Questions(int batch) => string.Join(",", Enumerable.Range(batch * Batch, Batch).Select(i =>
        $$"""
        {"type":"multiple","difficulty":"easy","category":"General%20Knowledge","question":"{{Uri.EscapeDataString($"Seed question {i}?")}}","correct_answer":"Right","incorrect_answers":["A","B","C"]}
        """));
}
