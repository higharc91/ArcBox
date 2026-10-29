using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Tests.Support;

namespace ArcBox.Server.Tests.Rooms;

public class SeatPickerTests
{
    private const string Rejoin = "Couldn't rejoin. Please join again.";

    private static SeatRef Slot(string id) => new(Slot: new PlayerId(id));

    private static bool LogEndsWith(JsonElement hostView, string entry)
    {
        var log = hostView.GetProperty("log");
        return log.GetArrayLength() > 0 && log[log.GetArrayLength() - 1].GetString() == entry;
    }

    /// <summary>A room running Ledger with phones p1 (Ann), p2 (Bea), p3 (Cy), all connected.</summary>
    private sealed class Table(ArcServerFactory factory, TestClient host, string code, TestClient[] phones, JoinResult[] joins)
        : IAsyncDisposable
    {
        public ArcServerFactory Factory { get; } = factory;
        public TestClient Host { get; } = host;
        public string Code { get; } = code;
        public TestClient[] Phones { get; } = phones;
        public JoinResult[] Joins { get; } = joins;

        public static async Task<Table> StartAsync(ArcServerFactory f)
        {
            var host = await TestClient.ConnectAsync(f);
            var code = (await host.InvokeAsync<CreateRoomResult>("CreateRoom")).Code;
            string[] names = ["Ann", "Bea", "Cy"];
            var phones = new TestClient[3];
            var joins = new JoinResult[3];
            for (var i = 0; i < 3; i++)
            {
                phones[i] = await TestClient.ConnectAsync(f);
                joins[i] = await phones[i].InvokeAsync<JoinResult>("Join", code, names[i]);
            }

            await host.InvokeAsync("SelectGame", "ledger");
            await host.InvokeAsync("StartGame");
            return new Table(f, host, code, phones, joins);
        }

        /// <summary>Bea's phone drops; waits until the game has been told.</summary>
        public async Task DropBeaAsync()
        {
            await Phones[1].DisposeAsync();
            await Host.WaitForAsync(ClientMethods.HostView, v => LogEndsWith(v, "connected:p2:false"));
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var p in Phones) await p.DisposeAsync();
            await Host.DisposeAsync();
        }
    }

    [Fact]
    public async Task Peek_changes_nothing()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        var lobbiesBefore = f.Sender.Of("host", ClientMethods.LobbyChanged).Count;

        var peek = await f.Room.PeekAsync([]);
        await f.Room.PeekAsync(["some-key"]);

        Assert.Equal(lobbiesBefore, f.Sender.Of("host", ClientMethods.LobbyChanged).Count);
        Assert.Empty(f.Sender.Of("c9", ClientMethods.LobbyChanged));
        var lobby = (LobbyState)f.Sender.Of("host", ClientMethods.LobbyChanged).Last().Args[0]!;
        Assert.Single(lobby.Players);
        Assert.Equal("TEST", peek.Code);
        Assert.True(peek.CanJoinNew);
        Assert.False(peek.InGame);
        Assert.Empty(peek.Offers);
        Assert.Null(peek.Suggested);
    }

    [Fact]
    public async Task Peek_unknown_room()
    {
        using var f = new ArcServerFactory();
        await using var c = await TestClient.ConnectAsync(f);

        Assert.Equal("Room not found.", await c.ThrowsHubAsync("Peek", new PeekRequest("ZZZZ")));
    }

    [Fact]
    public async Task Peek_over_the_hub_does_not_seat_the_caller()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var stranger = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");

        var peek = await stranger.PeekAsync(room.Code.ToLowerInvariant());

        Assert.Equal(room.Code, peek.Code);
        Assert.True(peek.CanJoinNew);
        Assert.Equal("You're not in a room.", await stranger.ThrowsHubAsync("StartGame"));
    }

    [Fact]
    public async Task TakeSeat_new_name_joins()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");

        var result = await ann.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));

        Assert.Equal(room.Code, result.Code);
        Assert.Equal(new PlayerId("p1"), result.PlayerId);
        Assert.False(string.IsNullOrEmpty(result.Secret));
        Assert.Null(result.Profile);
        var lobby = await host.WaitForAsync(ClientMethods.LobbyChanged, l => l.GetProperty("players").GetArrayLength() == 1);
        Assert.Equal("Ann", lobby.GetProperty("players")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Legacy_Join_still_works()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");

        var join = await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");

        Assert.Equal(new PlayerId("p1"), join.PlayerId);
        await using var ann2 = await TestClient.ConnectAsync(f);
        await ann2.InvokeAsync("Rejoin", room.Code, join.PlayerId.Value, join.Secret);
        Assert.Equal("Name must be 1–12 characters.", await ann.ThrowsHubAsync("Join", room.Code, "   "));
    }

    [Fact]
    public async Task Mid_game_peek_offers_only_offline_seats()
    {
        using var f = new ArcServerFactory();
        await using var t = await Table.StartAsync(f);
        await t.DropBeaAsync();
        await using var stranger = await TestClient.ConnectAsync(f);

        var peek = await stranger.PeekAsync(t.Code);

        Assert.True(peek.InGame);
        Assert.False(peek.CanJoinNew);
        Assert.Equal("ledger", peek.GameId);
        Assert.Null(peek.CampaignName);
        Assert.Null(peek.Suggested);
        var offer = Assert.Single(peek.Offers);
        Assert.Equal(Slot("p2"), offer.Ref);
        Assert.Equal(new SeatCard("Bea"), offer.Card);
        Assert.True(offer.Absent);
    }

    [Fact]
    public async Task Taking_offline_seat_rotates_secret_and_reconnects()
    {
        using var f = new ArcServerFactory();
        await using var t = await Table.StartAsync(f);
        await t.DropBeaAsync();
        await using var newPhone = await TestClient.ConnectAsync(f);

        var taken = await newPhone.TakeSeatAsync(new TakeSeatRequest(t.Code, Seat: Slot("p2")));

        Assert.Equal(new PlayerId("p2"), taken.PlayerId);
        Assert.NotEqual(t.Joins[1].Secret, taken.Secret);
        Assert.Equal("buttons", (await newPhone.WaitForAsync(ClientMethods.ControllerView)).GetProperty("kind").GetString());
        await newPhone.WaitForAsync(ClientMethods.LobbyChanged, l => l.GetProperty("players")[1].GetProperty("connected").GetBoolean());
        await t.Host.WaitForAsync(ClientMethods.HostView, v => LogEndsWith(v, "connected:p2:true"));

        // Review focus 2: the phone that lost the seat comes back with its stored secret and is turned away.
        await using var oldPhone = await TestClient.ConnectAsync(f);
        Assert.Equal(Rejoin, await oldPhone.ThrowsHubAsync("Rejoin", t.Code, "p2", t.Joins[1].Secret));

        // The new secret is the one that works.
        await using var third = await TestClient.ConnectAsync(f);
        await third.InvokeAsync("Rejoin", t.Code, "p2", taken.Secret);
    }

    [Fact]
    public async Task Taking_connected_seat_fails()
    {
        using var f = new ArcServerFactory();
        await using var t = await Table.StartAsync(f);
        await using var stranger = await TestClient.ConnectAsync(f);

        Assert.Equal("Someone's already playing as Bea.",
            await stranger.ThrowsHubAsync("TakeSeat", new TakeSeatRequest(t.Code, Seat: Slot("p2"))));
    }

    [Fact]
    public async Task Unknown_seat_fails()
    {
        using var f = new ArcServerFactory();
        await using var t = await Table.StartAsync(f);
        await using var stranger = await TestClient.ConnectAsync(f);

        Assert.Equal(Rejoin, await stranger.ThrowsHubAsync("TakeSeat", new TakeSeatRequest(t.Code, Seat: Slot("p9"))));
    }

    [Fact]
    public async Task Neither_or_both_fails()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var c = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        const string expected = "Pick a seat or enter a name.";

        Assert.Equal(expected, await c.ThrowsHubAsync("TakeSeat", new TakeSeatRequest(room.Code)));
        Assert.Equal(expected, await c.ThrowsHubAsync("TakeSeat",
            new TakeSeatRequest(room.Code, Seat: Slot("p1"), NewName: "Ann")));
    }

    [Fact]
    public async Task New_name_mid_game_fails()
    {
        using var f = new ArcServerFactory();
        await using var t = await Table.StartAsync(f);
        await using var late = await TestClient.ConnectAsync(f);

        Assert.Equal("Game in progress, join after this round.",
            await late.ThrowsHubAsync("TakeSeat", new TakeSeatRequest(t.Code, NewName: "Late")));
    }

    // Review focus 1
    [Fact]
    public async Task Two_phones_take_same_offline_seat_concurrently()
    {
        using var f = new ArcServerFactory();
        await using var t = await Table.StartAsync(f);
        await t.DropBeaAsync();
        await using var first = await TestClient.ConnectAsync(f);
        await using var second = await TestClient.ConnectAsync(f);
        var request = new TakeSeatRequest(t.Code, Seat: Slot("p2"));

        var results = await Task.WhenAll(
            first.TryInvokeMessageAsync("TakeSeat", request),
            second.TryInvokeMessageAsync("TakeSeat", request));

        Assert.Equal(1, results.Count(r => r is null));
        Assert.Equal("Someone's already playing as Bea.", Assert.Single(results, r => r is not null));
        var peek = await first.PeekAsync(t.Code);
        Assert.Empty(peek.Offers);
    }
}
