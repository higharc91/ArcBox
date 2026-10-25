using ArcBox.Sdk;
using ArcBox.Sdk.Testing;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Redline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Redline.Logic;

namespace ArcBox.Server.Tests.Redline;

public class AddRedlineTests
{
    private static ServiceProvider Build(params (string Key, string Value)[] settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedline(config);
        services.AddSingleton<IGameRegistry, GameRegistry>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddRedline_registers_game_and_source()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRedline(new ConfigurationBuilder()
            .AddInMemoryCollection([new("Redline:OpenTdb:Enabled", "false")]).Build());
        services.AddSingleton<IGameRegistry, GameRegistry>();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));

        using var sp = services.BuildServiceProvider();
        Assert.Contains(sp.GetRequiredService<IGameRegistry>().Manifests, m => m.Id == "redline");
        Assert.IsType<PooledQuestionSource>(sp.GetRequiredService<IQuestionSource>());
        Assert.False(sp.GetRequiredService<IOptions<OpenTdbOptions>>().Value.Enabled);
        Assert.Equal(10, sp.GetRequiredService<IQuestionSource>().Take(10).Count);
    }

    [Fact]
    public void AddRedline_game_runs_a_full_race_on_fallback()
    {
        using var sp = Build(("Redline:OpenTdb:Enabled", "false"));
        var game = sp.GetRequiredService<IGameRegistry>().Create("redline");
        var h = new GameHarness<IGame>(game, playerCount: 3);

        h.Start();
        var attribution = ((RedlineHostView)game.HostView()).Attribution;
        for (var step = 0; step < 2000 && !h.IsOver; step++)
            h.Advance(TimeSpan.FromSeconds(1));

        Assert.True(h.IsOver);
        Assert.Equal("Questions: REDLINE fallback pack", attribution);
        Assert.Equal(3, h.Results!.Scores.Count);
        Assert.Equal(h.Players.Select(p => p.Id.Value).Order(), h.Results.Scores.Select(s => s.PlayerId.Value).Order());
    }

    [Fact]
    public void Enabled_by_default_registers_the_pool_instance_as_the_hosted_service()
    {
        using var sp = Build();

        var hosted = Assert.Single(sp.GetServices<IHostedService>());
        Assert.Same(sp.GetRequiredService<OpenTdbQuestionPool>(), hosted);
        Assert.Equal("https://opentdb.com", sp.GetRequiredService<IOptions<OpenTdbOptions>>().Value.BaseUrl);
        Assert.Same(TimeProvider.System, sp.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void Typed_client_has_base_address_with_trailing_slash_and_10s_timeout()
    {
        using var sp = Build(("Redline:OpenTdb:BaseUrl", "https://example.test/trivia"));

        var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(OpenTdbClient));

        Assert.Equal(new Uri("https://example.test/trivia/"), http.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(10), http.Timeout);
        Assert.Equal(new Uri("https://example.test/trivia/api.php"), new Uri(http.BaseAddress!, "api.php"));
    }

    [Fact]
    public void Typed_client_primary_handler_recycles_pooled_connections()
    {
        using var sp = Build(("Redline:OpenTdb:Enabled", "false"));

        HttpMessageHandler handler = sp.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(nameof(OpenTdbClient));
        while (handler is DelegatingHandler delegating)
            handler = delegating.InnerHandler!;

        var sockets = Assert.IsType<SocketsHttpHandler>(handler);
        Assert.Equal(TimeSpan.FromMinutes(10), sockets.PooledConnectionLifetime);
    }

    [Fact]
    public void Existing_TimeProvider_registration_is_kept()
    {
        var fake = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(fake);
        services.AddRedline(new ConfigurationBuilder().Build());

        using var sp = services.BuildServiceProvider();

        Assert.Same(fake, sp.GetRequiredService<TimeProvider>());
    }

    [Theory]
    [InlineData("MinRequestSpacing", "00:00:00")]
    [InlineData("MaxBackoff", "00:00:01")]   // below the 5.5 s default spacing
    [InlineData("LowWater", "0")]
    [InlineData("LowWater", "151")]          // above the default Target of 150
    [InlineData("DedupeWindow", "0")]
    public void Invalid_options_fail_validation(string key, string value)
    {
        using var sp = Build(("Redline:OpenTdb:Enabled", "false"), ($"Redline:OpenTdb:{key}", value));

        Assert.Throws<OptionsValidationException>(() => sp.GetRequiredService<IOptions<OpenTdbOptions>>().Value);
    }

    [Fact]
    public async Task Invalid_options_fail_host_start_via_ValidateOnStart()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection([new("Redline:OpenTdb:Enabled", "false"), new("Redline:OpenTdb:Target", "0")]).Build();
        using var host = Host.CreateDefaultBuilder()
            .ConfigureServices(s => s.AddRedline(config))
            .Build();

        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync(TestContext.Current.CancellationToken));
    }
}
