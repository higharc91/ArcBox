using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Campaigns;
using ArcBox.Server.Content;
using ArcBox.Server.Games;
using ArcBox.Server.Profiles;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using ArcBox.Server.Tests.Storage;
using ArcBox.Server.Tests.Support;
using ArcBox.Server.Tests.TestGames;
using Counter.Logic;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace ArcBox.Server.Tests;

public sealed class RoomManagerTests : IDisposable
{
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private readonly RecordingSender sender = new();
    private readonly ArcDb db = StorageTestSupport.NewMemoryDb();
    private readonly StorageWriter writer;

    public RoomManagerTests() => writer = new StorageWriter(db, time, NullLogger<StorageWriter>.Instance);

    public void Dispose()
    {
        writer.Dispose();
        db.Dispose();
    }

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    private RoomManager NewManager(int maxRooms = 20, bool withHanger = false)
    {
        var registrations = new List<GameRegistration>
        {
            new(() => new CounterGame())
        };
        if (withHanger)
        {
            registrations.Add(new(() => new HangerGame()));
        }
        var registry = new GameRegistry(registrations);
        var rules = new CampaignRulesRegistry([]);
        var services = new RoomServices(registry, sender, time, new RoomOptions { MaxRooms = maxRooms },
            new ProfileService(db, writer, time), NullLoggerFactory.Instance,
            new CampaignService(db, writer, rules, time, NullLogger<CampaignService>.Instance), rules,
            new RoomStore(db, writer, time), ContentPackRegistry.Empty);
        return new RoomManager(services);
    }

    [Fact]
    public async Task Creates_rooms_up_to_limit()
    {
        await using var m = NewManager(maxRooms: 2);
        await m.CreateRoomAsync("h1");
        await m.CreateRoomAsync("h2");
        var ex = await Assert.ThrowsAsync<RoomException>(() => m.CreateRoomAsync("h3"));
        Assert.Equal("Server is full, try again later.", ex.Message);
    }

    // Review Focus #5
    [Fact]
    public async Task Find_normalizes_code()
    {
        await using var m = NewManager();
        var (room, r) = await m.CreateRoomAsync("h1");
        Assert.Same(room, await m.FindAsync($" {r.Code.ToLowerInvariant()} "));
    }

    [Fact]
    public async Task Sweep_removes_idle_rooms()
    {
        await using var m = NewManager();
        var (room, _) = await m.CreateRoomAsync("h1");
        await room.ConnectionLostAsync("h1");
        time.Advance(TimeSpan.FromMinutes(10));
        await m.SweepAsync();
        Assert.Equal(0, m.Count);
        Assert.Null(m.RoomFor("h1"));
    }

    [Fact]
    public async Task Watchdog_closes_room_with_hung_game()
    {
        HangerGame.Reset();
        try
        {
            await using var m = NewManager(withHanger: true);
            var (room, _) = await m.CreateRoomAsync("h1");
            await room.JoinAsync("c1", "Ann");
            await room.SelectGameAsync("h1", "hanger");
            await room.StartGameAsync("h1");
            var viewId = sender.LastViewId("c1");
            _ = room.SendInputAsync("c1", viewId, Json(new { pressed = "x" }));
            Assert.True(HangerGame.Entered.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            time.Advance(TimeSpan.FromSeconds(3));
            await m.SweepAsync();
            Assert.Equal(0, m.Count);
            Assert.Equal("game stopped responding", sender.Of("c1", ClientMethods.RoomClosed).Single().Args[0]);
        }
        finally
        {
            HangerGame.Gate.Set();
        }
    }
}
