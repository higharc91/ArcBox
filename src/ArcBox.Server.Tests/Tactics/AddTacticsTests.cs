using ArcBox.Sdk.Campaigns;
using ArcBox.Server.Content;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Tactics;
using ArcBox.Server.Tests.Speech.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tactics.Game;

namespace ArcBox.Server.Tests.Tactics;

public class AddTacticsTests
{
    /// <summary>The server's own wiring, with the content root at the repository's <c>content</c> folder.</summary>
    private static ServiceProvider Build(LogSink? log = null)
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
        services.AddOptions<ContentOptions>().Configure(o => o.Root = Path.Combine(repo, "content"));
        services.AddSingleton<IHostEnvironment>(new Env(repo));
        services.AddTactics();
        services.AddSingleton<IGameRegistry, GameRegistry>();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddTactics_lists_the_campaign_game_and_its_rules()
    {
        using var sp = Build();

        var manifest = Assert.Single(sp.GetRequiredService<IGameRegistry>().Manifests, m => m.Id == TacticsGame.Id);
        Assert.True(manifest.UsesCampaigns);
        Assert.True(manifest.ResumesAfterRestart);
        Assert.Contains(sp.GetServices<ICampaignRules>(), r => r.GameId == TacticsGame.Id);
    }

    [Fact]
    public void An_engine_failure_the_game_catches_is_logged_as_an_error_with_what_reproduces_it()
    {
        var log = new LogSink();
        using var sp = Build(log);
        var game = Assert.IsType<TacticsGame>(sp.GetRequiredService<IGameRegistry>().Create(TacticsGame.Id));

        Assert.NotNull(game.FaultLogged);
        game.FaultLogged(new EngineFault("ai", "river_crossing", 108, 41, 3, 2, "{\"$type\":\"EndTurn\",\"actor\":\"goblin1\"}",
            "{\"round\":3}", new InvalidOperationException("hero1 and goblin1 share the tile (4, 5).")));

        var entries = log.Entries.Where(e => e.Category.EndsWith(nameof(TacticsGame), StringComparison.Ordinal)).ToList();
        // The error says what failed and how to get there again, without the state, which goes to Debug.
        var error = Assert.Single(entries, e => e.Level == LogLevel.Error).Text;
        Assert.Contains("seed 108", error);
        Assert.Contains("after beat 41", error);
        Assert.Contains("goblin1", error);
        Assert.Contains("share the tile", error);
        Assert.DoesNotContain("{\"round\":3}", error);
        var debug = Assert.Single(entries, e => e.Level == LogLevel.Debug).Text;
        Assert.Contains("seed 108", debug);
        Assert.Contains("{\"round\":3}", debug);
    }

    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "ArcBox.Server.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
