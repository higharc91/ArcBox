using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Campaigns;
using ArcBox.Server.Games;
using ArcBox.Server.Profiles;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using ArcBox.Server.Tests.Storage;
using ArcBox.Server.Tests.TestGames;
using Counter.Logic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Support;

public sealed class RoomFixture : IAsyncDisposable
{
    private readonly List<(Room Room, CancellationTokenSource Cts, Task Run)> _rooms = [];
    private readonly ArcDb _db;
    private readonly StorageWriter _writer;
    private readonly RoomServices _services;

    /// <param name="ledger">The manifest <c>ledger</c> is registered with (it uses campaigns); the default is <see cref="LedgerGame.Campaign"/>.</param>
    /// <param name="seed">Runs against the empty database before the profile and campaign services load it.</param>
    public RoomFixture(GameManifest? ledger = null, Action<ArcDb>? seed = null)
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Sender = new RecordingSender();
        var registry = new GameRegistry([
            new(() => new CounterGame()),
            new(() => new ThrowerGame()),
            new(() => new LedgerGame(ledger ?? LedgerGame.Campaign)),
            new(() => new LedgerGame(LedgerGame.Plain))
        ]);
        _db = StorageTestSupport.NewMemoryDb();
        seed?.Invoke(_db);
        _writer = new StorageWriter(_db, Time, NullLogger<StorageWriter>.Instance);
        var rules = new CampaignRulesRegistry([new LedgerRules()]);
        Profiles = new ProfileService(_db, _writer, Time);
        Campaigns = new CampaignService(_db, _writer, rules, Time, NullLogger<CampaignService>.Instance);
        _services = new RoomServices(registry, Sender, Time, new RoomOptions(), Profiles, NullLoggerFactory.Instance,
            Campaigns, rules, new RoomStore(_db, _writer, Time));
        Room = Run("TEST");
    }

    public Room Room { get; }
    public RecordingSender Sender { get; }
    public FakeTimeProvider Time { get; }
    public ProfileService Profiles { get; }
    public CampaignService Campaigns { get; }
    public Dictionary<string, JoinResult> Joined { get; } = new();

    public async Task HostAsync()
    {
        await Room.BindHostAsync("host", "host-token");
    }

    /// <summary>Another running room on the same services, with its host bound as <c>host-{code}</c>.</summary>
    public async Task<Room> NewRoomAsync(string code)
    {
        var room = Run(code);
        await room.BindHostAsync($"host-{code}", "host-token");
        return room;
    }

    /// <summary>Stops <paramref name="room"/>'s run loop, as the room manager does when it removes a room.</summary>
    public async Task StopAsync(Room room)
    {
        var (_, cts, run) = _rooms.Single(r => r.Room == room);
        await cts.CancelAsync();
        await run;
    }

    public async Task<JoinResult> JoinAsync(string name, string conn)
    {
        var result = await Room.JoinAsync(conn, name);
        Joined[conn] = result;
        return result;
    }

    public string LastViewId(string conn) => Sender.LastViewId(conn);

    public static async Task<RoomFixture> InGameAsync(string gameId, int players = 1)
    {
        var fixture = new RoomFixture();
        await fixture.HostAsync();
        for (int i = 1; i <= players; i++)
        {
            await fixture.JoinAsync($"P{i}", $"c{i}");
        }

        await fixture.Room.SelectGameAsync("host", gameId);
        await fixture.Room.StartGameAsync("host");
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var (_, cts, run) in _rooms)
        {
            await cts.CancelAsync();
            try
            {
                await run;
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _writer.Dispose();
        _db.Dispose();
    }

    private Room Run(string code)
    {
        var room = new Room(code, "host-token", _services);
        var cts = new CancellationTokenSource();
        _rooms.Add((room, cts, Task.Run(() => room.RunAsync(cts.Token))));
        return room;
    }
}
