using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games;
using ArcBox.Server.Rooms;
using ArcBox.Server.Tests.TestGames;
using Counter.Logic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Support;

public sealed class RoomFixture : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _runTask;

    public RoomFixture()
    {
        Time = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Sender = new RecordingSender();
        var registry = new GameRegistry([
            new(() => new CounterGame()),
            new(() => new ThrowerGame())
        ]);
        var options = new RoomOptions();
        Room = new Room(
            "TEST",
            "host-token",
            registry,
            Sender,
            Time,
            options,
            NullLogger<Room>.Instance
        );
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

    public string LastViewId(string conn)
    {
        var msg = Sender.Of(conn, ClientMethods.ControllerView).Last();
        var element = (JsonElement)msg.Args[0]!;
        return element.GetProperty("viewId").GetString()!;
    }

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
    }
}
