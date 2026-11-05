using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using ArcBox.Server.Content;
using ArcBox.Server.Games;
using ArcBox.Server.Games.Tactics;
using ArcBox.Server.Tests.Speech.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Tests.Tactics;

public class AddTacticsTests
{
    /// <summary>The server's own wiring, with the content root at the repository's <c>content</c> folder.</summary>
    private static ServiceProvider Build(LogSink? log = null, string environment = "Test", Dictionary<string, string?>? settings = null,
        string? contentRoot = null)
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
        services.AddSingleton<IHostEnvironment>(new Env(repo) { EnvironmentName = environment });
        if (settings is not null)
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        services.AddTactics();
        services.AddSingleton<IGameRegistry, GameRegistry>();
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// The pack loads as the server starts, not in the first room's game call: a call over the rooms' 2 s watchdog closes the room as
    /// hung, and the pack takes about 1.4 s to load on an idle machine, which a loaded one ran over in the end-to-end runs.
    /// </summary>
    [Fact]
    public async Task The_pack_and_lore_load_as_the_server_starts()
    {
        using var sp = Build();
        var (pack, lore) = (sp.GetRequiredService<Lazy<RulesPack>>(), sp.GetRequiredService<Lazy<LoreBook>>());
        Assert.False(pack.IsValueCreated);

        foreach (var service in sp.GetServices<IHostedService>())
            await service.StartAsync(TestContext.Current.CancellationToken);

        for (var waited = 0; waited < 300 && !(pack.IsValueCreated && lore.IsValueCreated); waited++)
            await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.True(pack.IsValueCreated && lore.IsValueCreated);
    }

    [Fact]
    public async Task A_pack_that_wont_load_is_only_logged_at_start_and_still_fails_the_first_room()
    {
        var log = new LogSink();
        var empty = Directory.CreateTempSubdirectory("arcbox-no-content").FullName;
        try
        {
            using var sp = Build(log, contentRoot: empty);
            var hosted = sp.GetServices<IHostedService>().ToList();
            foreach (var service in hosted)
                await service.StartAsync(TestContext.Current.CancellationToken);
            for (var waited = 0; waited < 300 && !log.Entries.Any(e => e.Level == LogLevel.Warning); waited++)
                await Task.Delay(100, TestContext.Current.CancellationToken);

            Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Text.Contains("Tactics content", StringComparison.Ordinal));
            Assert.ThrowsAny<Exception>(() => sp.GetRequiredService<Lazy<RulesPack>>().Value);
            foreach (var service in hosted)
                await service.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    /// <summary>
    /// Review (rules NOTE 2 and 3): lore the game would play but never show (<see cref="LoreBook.Warnings"/>) is logged as the server
    /// starts, as <c>tools/tactics-lore/check.py</c> refuses it. Here the base pack's art and rules, with a lore pack of one such scene.
    /// </summary>
    [Fact]
    public async Task Lore_that_can_never_show_an_option_is_logged_at_start()
    {
        var log = new LogSink();
        var repo = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(repo, "ArcBox.slnx")))
            repo = Path.GetDirectoryName(repo)!;
        var real = Path.Combine(repo, "content", TacticsGame.Id, "base");
        var root = Directory.CreateTempSubdirectory("arcbox-lore-warning").FullName;
        try
        {
            var pack = Path.Combine(root, TacticsGame.Id, "base");
            Directory.CreateDirectory(pack);
            foreach (var dir in Directory.EnumerateDirectories(real).Where(d => Path.GetFileName(d) != "lore"))
            {
                var target = Path.Combine(pack, Path.GetFileName(dir));
                try { Directory.CreateSymbolicLink(target, dir); }
                catch (IOException)
                {
                    Directory.CreateDirectory(target);
                    foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        var copy = Path.Combine(target, Path.GetRelativePath(dir, file));
                        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                        File.Copy(file, copy);
                    }
                }
            }
            var lore = Directory.CreateDirectory(Path.Combine(pack, "lore", "scenes")).Parent!.FullName;
            File.WriteAllText(Path.Combine(lore, "pack.json"), """{ "id": "base", "loreVersion": 1, "files": ["arcs.json", "scenes/s.json"] }""");
            File.WriteAllText(Path.Combine(lore, "arcs.json"), """
                { "arcs": [{ "id": "a", "chapter": 1, "region": "r", "beats": [{ "id": "talk", "kind": "story" }] }] }
                """);
            File.WriteAllText(Path.Combine(lore, "scenes", "s.json"), """
                { "scenes": [{ "id": "s", "beat": "talk", "cast": ["narrator"], "start": "hub", "nodes": [
                    { "id": "hub", "lines": [{ "id": "l1", "speaker": "narrator", "text": "Hub." }], "options": [
                      { "id": "again", "label": "Again.", "to": "hub" }, { "id": "on", "label": "On.", "to": "done" } ] },
                    { "id": "done", "lines": [{ "id": "l2", "speaker": "narrator", "text": "Done." }], "end": true } ] }] }
                """);

            using var sp = Build(log, contentRoot: root);
            var hosted = sp.GetServices<IHostedService>().ToList();
            foreach (var service in hosted)
                await service.StartAsync(TestContext.Current.CancellationToken);
            for (var waited = 0; waited < 300 && !log.Entries.Any(e => e.Text.Contains("content loaded", StringComparison.Ordinal)); waited++)
                await Task.Delay(100, TestContext.Current.CancellationToken);

            var warning = Assert.Single(log.Entries, e => e.Level == LogLevel.Warning);
            Assert.Contains("Tactics lore:", warning.Text, StringComparison.Ordinal);
            Assert.Contains("the option 'again' of the node 'hub' of the scene 's' leads back to its own node", warning.Text, StringComparison.Ordinal);
            foreach (var service in hosted)
                await service.StopAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            // The links first, on their own, so nothing of the real content can go with the copy.
            foreach (var link in Directory.EnumerateDirectories(Path.Combine(root, TacticsGame.Id, "base")).Where(d => new DirectoryInfo(d).LinkTarget is not null))
                Directory.Delete(link);
            Directory.Delete(root, recursive: true);
        }
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

    [Fact]
    public void The_game_reads_the_base_packs_lore()
    {
        using var sp = Build();
        var game = Assert.IsType<TacticsGame>(sp.GetRequiredService<IGameRegistry>().Create(TacticsGame.Id));

        Assert.Equal("ch1_mayors_request", game.Lore.ArcFor("ashen_ford")?.Beats[0].Id);
    }

    private static readonly Dictionary<string, string?> Seeded = new()
    {
        ["Tactics:Seed:Flags:0"] = "ch1_skarrow_slain",
        ["Tactics:Seed:Beats:0"] = "ch1_mayors_request",
        ["Tactics:Seed:Beats:1"] = "ch1_the_ford_choice",
    };

    /// <summary>A new local session of the game the server makes, and the story its campaign starts with.</summary>
    private static WorldSave? StartedWorld(string environment, Dictionary<string, string?>? settings)
    {
        using var sp = Build(environment: environment, settings: settings);
        var game = Assert.IsType<TacticsGame>(sp.GetRequiredService<IGameRegistry>().Create(TacticsGame.Id));
        new GameHarness<TacticsGame>(game, playerCount: 1).Start();
        return game.Document.World;
    }

    [Fact]
    public void A_seed_starts_a_new_campaign_part_way_through_its_story_in_development()
    {
        var world = StartedWorld("Development", Seeded);

        Assert.NotNull(world);
        Assert.Equal(["ch1_skarrow_slain"], world.Flags);
        Assert.Equal(["ch1_mayors_request", "ch1_the_ford_choice"], world.Beats);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void A_seed_is_ignored_outside_development(string environment)
    {
        var world = StartedWorld(environment, Seeded);

        Assert.NotNull(world);
        Assert.Empty(world.Flags);
        Assert.Empty(world.Beats);
    }

    [Fact]
    public void Development_with_no_seed_starts_at_the_beginning()
        => Assert.Empty(StartedWorld("Development", new Dictionary<string, string?>())!.Beats);

    /// <summary>Whether the walk screen of a new local session of the game the server makes offers its VIP the battle list.</summary>
    private static bool OffersBattleList(string environment, Dictionary<string, string?>? settings)
    {
        using var sp = Build(environment: environment, settings: settings);
        var game = Assert.IsType<TacticsGame>(sp.GetRequiredService<IGameRegistry>().Create(TacticsGame.Id));
        var h = new GameHarness<TacticsGame>(game, playerCount: 1);
        h.Start();
        h.Input(0, new { action = "pickClass", data = new { @class = "fighter" } });
        var walk = Assert.IsType<CustomView>(h.PlayerView(0));
        Assert.Equal(TacticsGame.Screens.Walk, walk.Screen);
        h.Input(0, new { action = "battles" });
        var offered = walk.Data.GetProperty("canBattle").GetBoolean();
        Assert.Equal(offered ? TacticsPhase.EncounterPick : TacticsPhase.Exploration, game.Phase);     // the action goes with the button
        return offered;
    }

    private static readonly Dictionary<string, string?> BattleListOn = new() { [TacticsServiceCollectionExtensions.BattleListSetting] = "true" };

    [Fact]
    public void The_battle_list_is_offered_in_development_when_switched_on()
        => Assert.True(OffersBattleList("Development", BattleListOn));

    [Fact]
    public void The_battle_list_is_off_in_development_by_default()
    {
        Assert.False(OffersBattleList("Development", new Dictionary<string, string?>()));
        Assert.False(OffersBattleList("Development", null));
        Assert.False(OffersBattleList("Development", new() { [TacticsServiceCollectionExtensions.BattleListSetting] = "false" }));
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void The_battle_list_is_never_offered_outside_development(string environment)
        => Assert.False(OffersBattleList(environment, BattleListOn));

    private static IConfigurationSection Section(Dictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build().GetSection(TacticsServiceCollectionExtensions.SeedSection);

    [Fact]
    public void A_seed_reads_its_region_and_tile()
    {
        var seed = TacticsServiceCollectionExtensions.SeedFrom(Section(new()
        {
            ["Tactics:Seed:Region"] = "ashen_ford", ["Tactics:Seed:At:0"] = "96", ["Tactics:Seed:At:1"] = "60",
        }));

        Assert.NotNull(seed);
        Assert.Equal(("ashen_ford", new global::Tactics.Logic.Tile(96, 60)), (seed.Region, seed.At));
        Assert.Empty(seed.Flags);
        Assert.Empty(seed.Beats);
    }

    [Fact]
    public void No_seed_section_is_no_seed()
        => Assert.Null(TacticsServiceCollectionExtensions.SeedFrom(Section(new() { ["Tactics:Other"] = "x" })));

    [Fact]
    public void A_seed_tile_that_isnt_two_numbers_is_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => TacticsServiceCollectionExtensions.SeedFrom(Section(new()
        {
            ["Tactics:Seed:At:0"] = "96", ["Tactics:Seed:At:1"] = "60", ["Tactics:Seed:At:2"] = "1",
        })));

        Assert.Contains("Tactics:Seed:At", error.Message, StringComparison.Ordinal);
    }

    private sealed class Env(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "ArcBox.Server.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
