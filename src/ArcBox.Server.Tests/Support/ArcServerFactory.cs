using ArcBox.Server.Campaigns;
using ArcBox.Server.Games;
using ArcBox.Server.Rooms;
using ArcBox.Server.Speech;
using ArcBox.Server.Tests.Speech.Support;
using ArcBox.Server.Tests.TestGames;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Support;

/// <summary>The real server in memory. Room time runs on <see cref="Time"/>; the global clock is left alone.</summary>
public sealed class ArcServerFactory : WebApplicationFactory<Program>
{
    private readonly string? _databaseFile;
    private readonly bool _withTactics;
    private readonly DirectoryInfo _contentRoot = Directory.CreateTempSubdirectory("arcbox-content-");

    /// <summary>A server on a private in-memory database that lives as long as the factory.</summary>
    public ArcServerFactory()
    {
    }

    /// <summary>A server on <paramref name="databaseFile"/>, so a second factory on the same file is a restart.</summary>
    public ArcServerFactory(string databaseFile) => _databaseFile = databaseFile;

    /// <summary>
    /// With <paramref name="withTactics"/>, the content root also links the repository's <c>content/tactics</c>, so the
    /// real ArcBox Tactics can be played. Off by default: hashing its art at startup would slow every other test.
    /// </summary>
    public ArcServerFactory(bool withTactics) => _withTactics = withTactics;

    /// <summary>The server's <c>Content:Root</c>: a private directory holding one pack, <c>ledger/base</c>.</summary>
    public string ContentRoot => _contentRoot.FullName;

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // AddRedline reads this while Program registers services, so it must be a setting, not a test service.
        builder.UseSetting("Redline:OpenTdb:Enabled", "false");
        // The factory runs as Development, whose Web:Root is web/play/dist when it has been built; tests serve wwwroot.
        builder.UseSetting("Web:Root", "");
        // Like the settings above, read while Program registers services.
        SeedContent();
        builder.UseSetting("Content:Root", _contentRoot.FullName);
        if (_databaseFile is null)
            builder.UseSetting("Storage:ConnectionString", $"Data Source=file:{Guid.NewGuid():N}?mode=memory&cache=shared");
        else
            builder.UseSetting("Storage:Path", _databaseFile);
        builder.ConfigureTestServices(services =>
        {
            // Only the rooms' clock is faked. The unkeyed TimeProvider drives SignalR's
            // own keep-alives and timeouts, which must keep running on real time.
            services.RemoveAllKeyed<TimeProvider>(RoomManager.ClockKey);
            services.AddKeyedSingleton<TimeProvider>(RoomManager.ClockKey, Time);
            services.AddArcGame<ThrowerGame>();
            services.AddArcGame<LedgerGame>();
            services.AddArcGame(_ => new LedgerGame(LedgerGame.Plain));
            services.AddCampaignRules<LedgerRules>();
            // Never reach Google from a test, even on a machine whose user-secrets hold a real key. Speech tests
            // opt back in with their own key, cache directory and fake handler (registered after these).
            services.PostConfigure<SpeechOptions>(o => o.Google.ApiKey = null);
            services.AddHttpClient<ISpeechSynthesizer, GoogleTtsClient>()
                .ConfigurePrimaryHttpMessageHandler(() => new NoNetworkHandler());
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing) return;
        try
        {
            // Unlink the repository's tactics content first, so the recursive delete can never reach into it.
            var link = new DirectoryInfo(Path.Combine(_contentRoot.FullName, "tactics"));
            if (link.Exists && link.LinkTarget is not null)
                link.Delete();
            _contentRoot.Delete(true);
        }
        catch (IOException)
        {
            // A leftover temp directory is harmless.
        }
    }

    private void SeedContent()
    {
        var pack = Path.Combine(_contentRoot.FullName, "ledger", "base");
        Directory.CreateDirectory(Path.Combine(pack, "maps"));
        File.WriteAllText(Path.Combine(pack, "hello.txt"), "hello");
        File.WriteAllText(Path.Combine(pack, "maps", "arena.json"), "{\"w\":8}");
        File.WriteAllText(Path.Combine(pack, "blob.xyzunknown"), "blob");
        if (_withTactics)
        {
            var repo = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(repo.FullName, "ArcBox.slnx")))
                repo = repo.Parent ?? throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
            Directory.CreateSymbolicLink(Path.Combine(_contentRoot.FullName, "tactics"), Path.Combine(repo.FullName, "content", "tactics"));
        }
    }
}
