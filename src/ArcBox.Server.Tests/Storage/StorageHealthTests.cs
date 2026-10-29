using System.Net;
using ArcBox.Server.Storage;
using ArcBox.Server.Tests.Support;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Storage;

public class StorageHealthTests
{
    private sealed class Failing : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(HealthCheckResult.Unhealthy("down"));
    }

    [Fact]
    public async Task Healthz_is_ok_when_healthy()
    {
        using var f = new ArcServerFactory();

        var response = await f.CreateClient().GetAsync("/healthz", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Healthz_is_degraded_but_200_when_the_writer_is_backed_up()
    {
        var ct = TestContext.Current.CancellationToken;
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        using var f = new ArcServerFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddSingleton<TimeProvider>(time)));
        var http = f.CreateClient();
        using var release = new ManualResetEventSlim();
        var writer = f.Services.GetRequiredService<StorageWriter>();
        writer.Write("stuck", _ => release.Wait(TimeSpan.FromSeconds(30), ct));
        time.Advance(TimeSpan.FromSeconds(11));

        var response = await http.GetAsync("/healthz", ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("degraded", await response.Content.ReadAsStringAsync(ct));
        release.Set();
    }

    [Fact]
    public async Task Healthz_is_503_when_a_check_is_unhealthy()
    {
        var ct = TestContext.Current.CancellationToken;
        using var f = new ArcServerFactory().WithWebHostBuilder(b => b.ConfigureTestServices(s =>
            s.AddHealthChecks().AddCheck<Failing>("failing")));

        var response = await f.CreateClient().GetAsync("/healthz", ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("unhealthy", await response.Content.ReadAsStringAsync(ct));
    }

    [Fact]
    public async Task A_second_factory_on_the_same_file_sees_the_first_ones_data()
    {
        var ct = TestContext.Current.CancellationToken;
        var dir = Directory.CreateTempSubdirectory("arcbox-restart-");
        try
        {
            var file = Path.Combine(dir.FullName, "arcbox.db");
            using (var first = new ArcServerFactory(file))
            {
                first.CreateClient(); // starts the host
                var writer = first.Services.GetRequiredService<StorageWriter>();
                writer.Write("p", c =>
                {
                    using var command = c.CreateCommand();
                    command.CommandText = "INSERT INTO profiles (id, display_name, created_utc, last_seen_utc) VALUES ('pr_a', 'Ann', 't', 't')";
                    command.ExecuteNonQuery();
                });
                await writer.FlushAsync(ct);
            }

            using var second = new ArcServerFactory(file);
            second.CreateClient();
            using var connection = second.Services.GetRequiredService<ArcDb>().Open();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT display_name FROM profiles";
            Assert.Equal("Ann", count.ExecuteScalar());
        }
        finally
        {
            try { dir.Delete(true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task A_write_queued_just_before_the_factory_is_disposed_is_still_on_disk()
    {
        // WebApplicationFactory.Dispose disposes the container (and the writer) before it stops the host,
        // so the shutdown drain runs after Dispose; the write must survive that.
        var dir = Directory.CreateTempSubdirectory("arcbox-dispose-");
        try
        {
            var file = Path.Combine(dir.FullName, "arcbox.db");
            using (var f = new ArcServerFactory(file))
            {
                f.CreateClient();
                f.Services.GetRequiredService<StorageWriter>().Write("p", c =>
                {
                    using var command = c.CreateCommand();
                    command.CommandText = "INSERT INTO profiles (id, display_name, created_utc, last_seen_utc) VALUES ('pr_a', 'Ann', 't', 't')";
                    command.ExecuteNonQuery();
                });
            }

            using var db = StorageTestSupport.NewDb(new StorageOptions { Path = file });
            Assert.Equal("Ann", db.Scalar<string>("SELECT display_name FROM profiles"));
        }
        finally
        {
            try { dir.Delete(true); } catch (IOException) { }
        }
    }
}
