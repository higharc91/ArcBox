using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
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
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _runTask;
    private readonly ArcDb _db;
    private readonly StorageWriter _writer;

    public RoomFixture()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Sender = new RecordingSender();
        var registry = new GameRegistry([
            new(() => new CounterGame()),
            new(() => new ThrowerGame()),
            new(() => new LedgerGame())
        ]);
        _db = StorageTestSupport.NewMemoryDb();
        _writer = new StorageWriter(_db, Time, NullLogger<StorageWriter>.Instance);
        var services = new RoomServices(registry, Sender, Time, new RoomOptions(),
            new ProfileService(_db, _writer, Time), NullLoggerFactory.Instance);
        Room = new Room("TEST", "host-token", services);
        _runTask = Task.Run(() => Room.RunAsync(_cts.Token));
    }

    public Room Room { get; }
    public RecordingSender Sender { get; }
    public FakeTimeProvider Time { get; }
    public Dictionary<string, JoinResult> Joined { get; } = new();

    public async Task HostAsync()
    {
        await Room.BindHostAsync("host", "host-token");
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
        _cts.Cancel();
        try
        {
            await _runTask;
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _writer.Dispose();
        _db.Dispose();
    }
}
