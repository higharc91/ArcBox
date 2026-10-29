using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Campaigns;
using ArcBox.Server.Games;
using ArcBox.Server.Profiles;
using ArcBox.Server.Rooms;
using ArcBox.Server.Storage;
using ArcBox.Server.Tests.Storage;
using ArcBox.Server.Tests.Support;
using ArcBox.Server.Tests.TestGames;
using Counter.Logic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace ArcBox.Server.Tests.Rooms;

/// <summary>Durable rooms (spec §3.5): membership survives a restart or an idle break; game state does not.</summary>
public sealed class DurableRoomTests
{
    private const string Restarted = "Game interrupted: the server restarted.";
    private const string Idle = "Game interrupted: the room was idle.";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    private static string? Message(JsonElement results) => results.GetProperty("message").GetString();

    private static List<(string Id, string Name, bool Connected)> Players(JsonElement lobby) =>
    [
        .. lobby.GetProperty("players").EnumerateArray()
            .Select(p => (p.GetProperty("id").GetString()!, p.GetProperty("name").GetString()!, p.GetProperty("connected").GetBoolean()))
    ];

    private static List<string?> Log(JsonElement hostView) =>
        [.. hostView.GetProperty("log").EnumerateArray().Select(e => e.GetString())];

    /// <summary>Polls <paramref name="condition"/> until it holds, for up to 5 s.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TestClient.Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }
            await Task.Delay(10, Ct);
        }
    }

    /// <summary>Disconnects <paramref name="clients"/> and waits until the server has let go of each.</summary>
    private static async Task LeaveAsync(ArcServerFactory f, params TestClient[] clients)
    {
        var rooms = f.Services.GetRequiredService<RoomManager>();
        foreach (var client in clients)
        {
            var id = client.ConnectionId!;
            await client.DisposeAsync();
            await WaitUntilAsync(() => rooms.RoomFor(id) is null, $"the server to see {id} leave");
        }
    }

    /// <summary>Advances the rooms clock past the idle timeout and waits for the sweeper to park every room.</summary>
    private static async Task ParkAsync(ArcServerFactory f)
    {
        var rooms = f.Services.GetRequiredService<RoomManager>();
        f.Time.Advance(TimeSpan.FromMinutes(10));
        await WaitUntilAsync(() => rooms.Count == 0, "the idle room to be parked");
    }

    /// <summary>Every column of the room's row as text, after the writer has flushed; null when there is no row.</summary>
    private static async Task<string[]?> RowAsync(IServiceProvider services, string code)
    {
        await services.GetRequiredService<StorageWriter>().FlushAsync(Ct);
        using var connection = services.GetRequiredService<ArcDb>().Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT code, host_token_hash, slots_json, next_slot_id, selected_game, campaign_id, game_running, created_utc, updated_utc FROM rooms WHERE code = $code";
        command.Parameters.AddWithValue("$code", code);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }
        return [.. Enumerable.Range(0, reader.FieldCount).Select(i => reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i))!)];
    }

    /// <summary>A database file in its own temp directory, deleted (with its WAL files) on dispose.</summary>
    private sealed class TempDb : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("arcbox-durable-");

        public string File => Path.Combine(_dir.FullName, "arcbox.db");

        public void Dispose()
        {
            try { _dir.Delete(true); } catch (IOException) { }
        }
    }

    /// <summary>A room manager on an in-memory database, for tests that need games the server doesn't register.</summary>
    private sealed class Harness : IAsyncDisposable
    {
        private readonly ArcDb _db = StorageTestSupport.NewMemoryDb();

        public Harness()
        {
            Writer = new StorageWriter(_db, Time, NullLogger<StorageWriter>.Instance);
            Store = new RoomStore(_db, Writer, Time);
            var registry = new GameRegistry([new(() => new CounterGame()), new(() => new HangerGame())]);
            var rules = new CampaignRulesRegistry([]);
            Services = new RoomServices(registry, Sender, Time, new RoomOptions(), new ProfileService(_db, Writer, Time),
                NullLoggerFactory.Instance, new CampaignService(_db, Writer, rules, Time, NullLogger<CampaignService>.Instance),
                rules, Store);
        }

        public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        public RecordingSender Sender { get; } = new();
        public StorageWriter Writer { get; }
        public RoomStore Store { get; }
        public RoomServices Services { get; }

        public int RowCount(string code)
        {
            using var connection = _db.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM rooms WHERE code = $code";
            command.Parameters.AddWithValue("$code", code);
            return Convert.ToInt32(command.ExecuteScalar());
        }

        public ValueTask DisposeAsync()
        {
            Writer.Dispose();
            _db.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Restart_keeps_code_and_seats()
    {
        using var db = new TempDb();
        CreateRoomResult room;
        JoinResult ann;
        using (var a = new ArcServerFactory(db.File))
        {
            await using var host = await TestClient.ConnectAsync(a);
            await using var p1 = await TestClient.ConnectAsync(a);
            await using var p2 = await TestClient.ConnectAsync(a);
            room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
            ann = await p1.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
            await p2.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Bea"));
            await host.InvokeAsync("SelectGame", "counter");
        }

        using var b = new ArcServerFactory(db.File);
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);

        var lobby = await tv.WaitForAsync(ClientMethods.LobbyChanged);
        Assert.Equal(room.Code, lobby.GetProperty("code").GetString());
        Assert.Equal([("p1", "Ann", false), ("p2", "Bea", false)], Players(lobby));
        Assert.Equal("counter", lobby.GetProperty("selectedGameId").GetString());
        Assert.Equal(0, tv.Unclaimed(ClientMethods.GameEnded));

        await using var phone = await TestClient.ConnectAsync(b);
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        await tv.WaitForAsync(ClientMethods.LobbyChanged, l => Players(l)[0] == ("p1", "Ann", true));

        // Numbering carries on from the stored next slot id.
        await using var cy = await TestClient.ConnectAsync(b);
        Assert.Equal("p3", (await cy.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Cy"))).PlayerId.Value);
    }

    [Fact]
    public async Task Non_resumable_game_returns_to_lobby_with_interrupted_note_once()
    {
        using var db = new TempDb();
        CreateRoomResult room;
        JoinResult ann;
        using (var a = new ArcServerFactory(db.File))
        {
            await using var host = await TestClient.ConnectAsync(a);
            await using var p1 = await TestClient.ConnectAsync(a);
            room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
            ann = await p1.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
            await host.InvokeAsync("SelectGame", "counter");
            await host.InvokeAsync("StartGame");
            await p1.WaitForAsync(ClientMethods.ControllerView);
        }

        using var b = new ArcServerFactory(db.File);
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        Assert.Equal(Restarted, Message(await tv.WaitForAsync(ClientMethods.GameEnded)));
        var lobby = await tv.WaitForAsync(ClientMethods.LobbyChanged);
        Assert.False(lobby.GetProperty("inGame").GetBoolean());
        Assert.Equal(Restarted, Message(lobby.GetProperty("lastResults")));

        await using var phone = await TestClient.ConnectAsync(b);
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        Assert.Equal(Restarted, Message(await phone.WaitForAsync(ClientMethods.GameEnded)));

        // Binding again, on the same or a new connection, doesn't repeat it.
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        await using var tv2 = await TestClient.ConnectAsync(b);
        await tv2.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        await tv2.WaitForAsync(ClientMethods.LobbyChanged);
        await using var phone2 = await TestClient.ConnectAsync(b);
        await phone2.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        await phone2.WaitForAsync(ClientMethods.LobbyChanged);
        await tv2.WaitForAsync(ClientMethods.LobbyChanged, l => Players(l)[0].Connected);

        Assert.Equal(0, tv.Unclaimed(ClientMethods.GameEnded));
        Assert.Equal(0, tv2.Unclaimed(ClientMethods.GameEnded));
        Assert.Equal(0, phone.Unclaimed(ClientMethods.GameEnded));
        Assert.Equal(0, phone2.Unclaimed(ClientMethods.GameEnded));

        // The rebuilt room saved itself back in the lobby, so a second restart has nothing to interrupt.
        Assert.Equal("0", (await RowAsync(b.Services, room.Code))![6]); // game_running
    }

    [Fact]
    public async Task Resumable_game_resumes_with_lease_and_everyone_disconnected()
    {
        using var db = new TempDb();
        CreateRoomResult room;
        JoinResult ann;
        string campaignId;
        using (var a = new ArcServerFactory(db.File))
        {
            await using var host = await TestClient.ConnectAsync(a);
            await using var p1 = await TestClient.ConnectAsync(a);
            await using var p2 = await TestClient.ConnectAsync(a);
            room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
            ann = await p1.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
            await p1.InvokeAsync("SelectGame", "ledger");
            await p1.InvokeAsync<ProfileKey>("CreateCampaign", "Embers");
            var withCampaign = await host.WaitForAsync(ClientMethods.LobbyChanged,
                l => l.TryGetProperty("campaign", out var c) && c.ValueKind == JsonValueKind.Object);
            campaignId = withCampaign.GetProperty("campaign").GetProperty("id").GetString()!;
            await p1.InvokeAsync("StartGame");
            await host.WaitForAsync(ClientMethods.HostView);
        }

        using var b = new ArcServerFactory(db.File);
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);

        var view = await tv.WaitForAsync(ClientMethods.HostView);
        Assert.Contains("resume", Log(view));
        Assert.Equal(campaignId, view.GetProperty("campaignId").GetString());
        Assert.All(view.GetProperty("seats").EnumerateArray(), s => Assert.False(s.GetProperty("connected").GetBoolean()));
        Assert.True((await tv.WaitForAsync(ClientMethods.LobbyChanged)).GetProperty("inGame").GetBoolean());
        Assert.Equal(room.Code, b.Services.GetRequiredService<CampaignService>().LeasedBy(campaignId));
        Assert.Equal(0, tv.Unclaimed(ClientMethods.GameEnded));

        // Players come back through OnPlayerConnectionChanged.
        await using var phone = await TestClient.ConnectAsync(b);
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        await tv.WaitForAsync(ClientMethods.HostView, v => Log(v).Contains("connected:p1:true"));
        Assert.Equal("buttons", (await phone.WaitForAsync(ClientMethods.ControllerView)).GetProperty("kind").GetString());
    }

    // Review Focus 4
    [Fact]
    public async Task Rebuilt_room_does_not_resume_campaign_held_elsewhere()
    {
        using var f = new ArcServerFactory();
        var campaigns = f.Services.GetRequiredService<CampaignService>();
        var host = await TestClient.ConnectAsync(f);
        var p1 = await TestClient.ConnectAsync(f);
        var first = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await p1.TakeSeatAsync(new TakeSeatRequest(first.Code, NewName: "Ann"));
        await p1.InvokeAsync("SelectGame", "ledger");
        var key = await p1.InvokeAsync<ProfileKey>("CreateCampaign", "Embers");
        var campaignId = (await host.WaitForAsync(ClientMethods.LobbyChanged,
            l => l.TryGetProperty("campaign", out var c) && c.ValueKind == JsonValueKind.Object)).GetProperty("campaign").GetProperty("id").GetString()!;
        await p1.InvokeAsync("StartGame");
        await host.WaitForAsync(ClientMethods.HostView);
        Assert.Equal(first.Code, campaigns.LeasedBy(campaignId));

        // Everyone leaves; the first room is parked, which gives the campaign back.
        await LeaveAsync(f, p1, host);
        await ParkAsync(f);
        Assert.Null(campaigns.LeasedBy(campaignId));

        // Another room picks the campaign up.
        await using var host2 = await TestClient.ConnectAsync(f);
        await using var ann2 = await TestClient.ConnectAsync(f);
        var second = await host2.InvokeAsync<CreateRoomResult>("CreateRoom");
        await host2.InvokeAsync("SelectGame", "ledger");
        await host2.InvokeAsync("SelectCampaign", campaignId);
        await ann2.TakeSeatAsync(new TakeSeatRequest(second.Code, Seat: new SeatRef(Member: key.Id), ProfileKeys: [key.Key]));
        await host2.InvokeAsync("StartGame");
        Assert.Equal(second.Code, campaigns.LeasedBy(campaignId));

        // The first room comes back in the lobby rather than resuming a second session.
        await using var tv = await TestClient.ConnectAsync(f);
        await tv.InvokeAsync("ResumeHost", first.Code, first.HostToken);
        var expected = $"Embers is being played in room {second.Code}.";
        Assert.Equal(expected, Message(await tv.WaitForAsync(ClientMethods.GameEnded)));
        var lobby = await tv.WaitForAsync(ClientMethods.LobbyChanged);
        Assert.False(lobby.GetProperty("inGame").GetBoolean());
        Assert.Equal(expected, Message(lobby.GetProperty("lastResults")));
        Assert.Equal(second.Code, campaigns.LeasedBy(campaignId));
        Assert.Equal(0, tv.Unclaimed(ClientMethods.HostView));
    }

    // Review Focus 3
    [Fact]
    public async Task TakeSeat_then_immediate_shutdown_is_persisted()
    {
        using var db = new TempDb();
        CreateRoomResult room;
        JoinResult ann;
        var a = new ArcServerFactory(db.File);
        var host = await TestClient.ConnectAsync(a);
        var p1 = await TestClient.ConnectAsync(a);
        try
        {
            room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
            ann = await p1.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
        }
        finally
        {
            a.Dispose(); // at once: the seat's write may still be queued
            await p1.DisposeAsync();
            await host.DisposeAsync();
        }

        using var b = new ArcServerFactory(db.File);
        await using var phone = await TestClient.ConnectAsync(b);
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        Assert.Equal([("p1", "Ann", true)], Players(await phone.WaitForAsync(ClientMethods.LobbyChanged)));
    }

    [Fact]
    public async Task Idle_room_is_parked_and_rebuilt_on_rejoin()
    {
        using var f = new ArcServerFactory();
        var rooms = f.Services.GetRequiredService<RoomManager>();
        var host = await TestClient.ConnectAsync(f);
        var p1 = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var ann = await p1.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
        await LeaveAsync(f, p1, host);

        await ParkAsync(f);
        Assert.Equal(0, rooms.Count);
        Assert.NotNull(await RowAsync(f.Services, room.Code));

        await using var phone = await TestClient.ConnectAsync(f);
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        var lobby = await phone.WaitForAsync(ClientMethods.LobbyChanged);
        Assert.Equal(room.Code, lobby.GetProperty("code").GetString());
        Assert.Equal([("p1", "Ann", true)], Players(lobby));
        Assert.Equal(1, rooms.Count);
        Assert.Equal(0, phone.Unclaimed(ClientMethods.GameEnded));
    }

    [Fact]
    public async Task Parked_non_resumable_game_says_room_was_idle()
    {
        using var f = new ArcServerFactory();
        var host = await TestClient.ConnectAsync(f);
        var p1 = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var ann = await p1.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
        await host.InvokeAsync("SelectGame", "plain-ledger");
        await host.InvokeAsync("StartGame");
        await p1.WaitForAsync(ClientMethods.ControllerView);
        await LeaveAsync(f, p1, host);
        await ParkAsync(f);

        await using var tv = await TestClient.ConnectAsync(f);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        Assert.Equal(Idle, Message(await tv.WaitForAsync(ClientMethods.GameEnded)));

        await using var phone = await TestClient.ConnectAsync(f);
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        Assert.Equal(Idle, Message(await phone.WaitForAsync(ClientMethods.GameEnded)));
        Assert.False((await phone.WaitForAsync(ClientMethods.LobbyChanged)).GetProperty("inGame").GetBoolean());
    }

    [Fact]
    public async Task Watchdog_close_deletes_row()
    {
        HangerGame.Reset();
        try
        {
            await using var h = new Harness();
            await using var m = new RoomManager(h.Services);
            var (room, created) = await m.CreateRoomAsync("h1");
            await room.JoinAsync("c1", "Ann");
            await room.SelectGameAsync("h1", "hanger");
            await room.StartGameAsync("h1");
            await h.Writer.FlushAsync(Ct);
            Assert.Equal(1, h.RowCount(created.Code));

            _ = room.SendInputAsync("c1", h.Sender.LastViewId("c1"), Json(new { pressed = "x" }));
            Assert.True(HangerGame.Entered.Wait(TimeSpan.FromSeconds(5), Ct));
            h.Time.Advance(TimeSpan.FromSeconds(3));
            await m.SweepAsync();

            Assert.Equal(0, m.Count);
            Assert.False(h.Store.Exists(created.Code));
            await h.Writer.FlushAsync(Ct);
            Assert.Equal(0, h.RowCount(created.Code));
            Assert.Null(await m.FindAsync(created.Code));
        }
        finally
        {
            HangerGame.Gate.Set();
        }
    }

    [Fact]
    public async Task Rows_older_than_RetainFor_are_deleted()
    {
        using var f = new ArcServerFactory();
        var store = f.Services.GetRequiredService<RoomStore>();
        var host = await TestClient.ConnectAsync(f);
        var old = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await LeaveAsync(f, host);
        await ParkAsync(f);
        Assert.True(store.Exists(old.Code));

        f.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        await WaitUntilAsync(() => !store.Exists(old.Code), "the sweeper to delete the old row");
        Assert.Null(await RowAsync(f.Services, old.Code));

        await using var tv = await TestClient.ConnectAsync(f);
        Assert.Equal("Room not found.", await tv.ThrowsHubAsync("ResumeHost", old.Code, old.HostToken));

        // A room saved since is kept.
        var fresh = await tv.InvokeAsync<CreateRoomResult>("CreateRoom");
        f.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(0, store.DeleteOlderThan(f.Time.GetUtcNow() - TimeSpan.FromHours(24)));
        Assert.NotNull(await RowAsync(f.Services, fresh.Code));
    }

    [Fact]
    public async Task Live_room_keeps_its_row_past_RetainFor()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var saved = (await RowAsync(f.Services, room.Code))![8];

        // The host stays connected, so the room is never idle; the sweeper saves it again rather than deleting it.
        f.Time.Advance(TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        string[]? row = null;
        var deadline = DateTime.UtcNow + TestClient.Timeout;
        while ((row = await RowAsync(f.Services, room.Code)) is not null && row[8] == saved && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10, Ct);
        }

        Assert.NotNull(row);
        Assert.NotEqual(saved, row[8]);
        Assert.Equal(1, f.Services.GetRequiredService<RoomManager>().Count);
    }

    [Fact]
    public async Task Stored_record_has_no_plaintext_secrets()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var p1 = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var ann = await p1.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));

        var row = string.Join("|", (await RowAsync(f.Services, room.Code))!);

        Assert.DoesNotContain(ann.Secret, row);
        Assert.DoesNotContain(room.HostToken, row);
        Assert.Contains(Secrets.Hash(ann.Secret), row);
        Assert.Contains(Secrets.Hash(room.HostToken), row);
    }

    [Fact]
    public async Task Concurrent_rebuilds_make_one_room()
    {
        await using var h = new Harness();
        string code;
        await using (var before = new RoomManager(h.Services))
        {
            var (room, created) = await before.CreateRoomAsync("h1");
            await room.JoinAsync("c1", "Ann");
            code = created.Code;
        }

        await using var after = new RoomManager(h.Services);
        using var go = new ManualResetEventSlim(false);
        var finds = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => { go.Wait(Ct); return after.FindAsync(code.ToLowerInvariant()); }, Ct))
            .ToArray();
        go.Set();
        var found = await Task.WhenAll(finds);

        Assert.NotNull(found[0]);
        Assert.All(found, r => Assert.Same(found[0], r));
        Assert.Equal(1, after.Count);
    }
}
