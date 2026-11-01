using ArcBox.Sdk.Campaigns;
using ArcBox.Server.Content;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Tactics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Tactics.Game;

namespace ArcBox.Server.Tests.Tactics;

public class AddTacticsTests
{
    /// <summary>The server's own wiring, with the content root at the repository's <c>content</c> folder.</summary>
    private static ServiceProvider Build()
    {
        var repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "ArcBox.slnx")))
            repo = Path.GetDirectoryName(repo) ?? throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
        var services = new ServiceCollection();
        services.AddLogging();
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

    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "ArcBox.Server.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
