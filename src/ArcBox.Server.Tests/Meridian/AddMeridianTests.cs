using ArcBox.Sdk.Testing;
using ArcBox.Server.Content;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Meridian;
using ArcBox.Server.Tests.Speech.Support;
using Meridian.Game;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MeridianPack = Meridian.Logic.RulesPack;

namespace ArcBox.Server.Tests.Meridian;

public sealed class AddMeridianTests
{
    private static ServiceProvider Build(LogSink? log = null, string? contentRoot = null)
    {
        var repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "ArcBox.slnx")))
            repo = Path.GetDirectoryName(repo) ?? throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
        var services = new ServiceCollection();
        services.AddLogging(b =>
        {
            if (log is not null)
                b.AddProvider(log).SetMinimumLevel(LogLevel.Debug);
        });
        services.AddOptions<ContentOptions>().Configure(o => o.Root = contentRoot ?? Path.Combine(repo, "content"));
        services.AddSingleton<IHostEnvironment>(new Env(repo));
        services.AddMeridian();
        services.AddSingleton<IGameRegistry, GameRegistry>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Meridian_is_registered()
    {
        using var sp = Build();
        var registry = sp.GetRequiredService<IGameRegistry>();
        Assert.Contains(registry.Manifests, m => m.Id == MeridianGame.Id && m.MinPlayers == 3 && m.MaxPlayers == 6);
        Assert.IsType<MeridianGame>(registry.Create(MeridianGame.Id));
    }

    [Fact]
    public async Task The_pack_loads_from_the_content_root_as_the_server_starts()
    {
        using var sp = Build();
        var pack = sp.GetRequiredService<Lazy<MeridianPack>>();
        Assert.False(pack.IsValueCreated);
        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);
        for (var waited = 0; waited < 300 && !pack.IsValueCreated; waited++)
            await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.Equal(8, pack.Value.Crew.Count);
    }

    [Fact]
    public async Task A_broken_pack_doesnt_stop_the_server_and_fails_the_first_room_with_every_error()
    {
        // M2 review focus 9.
        var log = new LogSink();
        var empty = Directory.CreateTempSubdirectory("arcbox-no-meridian").FullName;
        try
        {
            using var sp = Build(log, empty);
            foreach (var service in sp.GetServices<IHostedService>())
                await service.StartAsync(TestContext.Current.CancellationToken);
            for (var waited = 0; waited < 300 && !log.Entries.Any(e => e.Level == LogLevel.Warning); waited++)
                await Task.Delay(100, TestContext.Current.CancellationToken);
            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("Meridian content", StringComparison.Ordinal));

            var game = (MeridianGame)sp.GetRequiredService<IGameRegistry>().Create(MeridianGame.Id);
            var error = Assert.Throws<global::Meridian.Logic.ContentException>(() => new GameHarness<MeridianGame>(game, 3).Start());
            Assert.Equal(5, error.Errors.Count);   // every missing file, not just the first
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "ArcBox.Server.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
