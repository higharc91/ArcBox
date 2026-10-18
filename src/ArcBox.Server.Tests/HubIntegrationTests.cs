using ArcBox.Sdk.Protocol;
using ArcBox.Server.Rooms;
using ArcBox.Server.Tests.Support;
using Counter.Logic;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ArcBox.Server.Tests;

public class HubIntegrationTests
{
    [Fact]
    public async Task Healthz_and_placeholder_page_respond()
    {
        using var f = new ArcServerFactory();
        using var http = f.CreateClient();
        var ct = TestContext.Current.CancellationToken;
        Assert.Equal("ok", await http.GetStringAsync("/healthz", ct));
        Assert.Contains("ArcBox server is running", await http.GetStringAsync("/", ct));
    }

    [Fact]
    public async Task Full_counter_round_over_signalr()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.InvokeAsync<JoinResult>("Join", room.Code.ToLowerInvariant(), "Ann");
        await ann.InvokeAsync("SelectGame", "counter");
        await ann.InvokeAsync("StartGame");

        var view = await ann.WaitForAsync(ClientMethods.ControllerView);
        Assert.Equal("buttons", view.GetProperty("kind").GetString());

        await ann.InvokeAsync("SendInput", view.GetProperty("viewId").GetString(), new { pressed = "press" });
        await host.WaitForAsync(ClientMethods.HostView, a => a.GetProperty("rows")[0].GetProperty("count").GetInt32() == 1);

        f.Time.Advance(CounterGame.Duration);
        var ended = await ann.WaitForAsync(ClientMethods.GameEnded);
        Assert.Equal("Ann", ended.GetProperty("scores")[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task Room_errors_reach_client_as_exact_messages()
    {
        using var f = new ArcServerFactory();
        await using var c = await TestClient.ConnectAsync(f);
        Assert.Equal("Room not found.", await c.ThrowsHubAsync("Join", "ZZZZ", "Ann"));
        Assert.Equal("You're not in a room.", await c.ThrowsHubAsync("StartGame"));
    }

    [Fact]
    public async Task Disconnect_then_rejoin_on_new_connection_gets_snapshot()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        var ann = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var join = await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");
        await host.InvokeAsync("SelectGame", "counter");
        await host.InvokeAsync("StartGame");

        await ann.DisposeAsync();
        await host.WaitForAsync(ClientMethods.LobbyChanged, l => !l.GetProperty("players")[0].GetProperty("connected").GetBoolean());

        await using var ann2 = await TestClient.ConnectAsync(f);
        await ann2.InvokeAsync("Rejoin", room.Code, join.PlayerId.Value, join.Secret);
        Assert.Equal("buttons", (await ann2.WaitForAsync(ClientMethods.ControllerView)).GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Host_resume_on_new_connection_keeps_room()
    {
        using var f = new ArcServerFactory();
        var host = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await host.DisposeAsync();

        await using var host2 = await TestClient.ConnectAsync(f);
        await host2.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        Assert.Equal(room.Code, (await host2.WaitForAsync(ClientMethods.LobbyChanged)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Crashing_game_over_signalr_returns_everyone_to_lobby()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");
        await host.InvokeAsync("SelectGame", "thrower");
        await host.InvokeAsync("StartGame");

        var v = await ann.WaitForAsync(ClientMethods.ControllerView);
        await ann.InvokeAsync("SendInput", v.GetProperty("viewId").GetString(), new { pressed = "boom" });
        Assert.Equal("Game crashed", (await host.WaitForAsync(ClientMethods.GameEnded)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task Flooding_non_input_methods_is_rate_limited()
    {
        using var f = new ArcServerFactory();
        await using var c = await TestClient.ConnectAsync(f);
        await c.InvokeAsync<CreateRoomResult>("CreateRoom");
        var messages = await Task.WhenAll(Enumerable.Range(0, 40).Select(_ => c.TryInvokeMessageAsync("SelectGame", "counter")));
        Assert.Contains("Slow down.", messages);
    }

    [Fact]
    public async Task Flooding_input_is_dropped_silently()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");
        await host.InvokeAsync("SelectGame", "counter");
        await host.InvokeAsync("StartGame");
        var viewId = (await ann.WaitForAsync(ClientMethods.ControllerView)).GetProperty("viewId").GetString();

        var messages = await Task.WhenAll(Enumerable.Range(0, 60).Select(_ => ann.TryInvokeMessageAsync("SendInput", viewId, new { pressed = "press" })));
        Assert.All(messages, m => Assert.Null(m));

        f.Time.Advance(CounterGame.Duration);
        var ended = await ann.WaitForAsync(ClientMethods.GameEnded);
        Assert.InRange(ended.GetProperty("scores")[0].GetProperty("score").GetInt32(), 1, 59);
    }

    [Fact]
    public async Task Joining_another_room_leaves_the_previous_one()
    {
        using var f = new ArcServerFactory();
        await using var hostA = await TestClient.ConnectAsync(f);
        await using var hostB = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var a = await hostA.InvokeAsync<CreateRoomResult>("CreateRoom");
        var b = await hostB.InvokeAsync<CreateRoomResult>("CreateRoom");

        await ann.InvokeAsync<JoinResult>("Join", a.Code, "Ann");
        await ann.InvokeAsync<JoinResult>("Join", b.Code, "Ann");

        await hostA.WaitForAsync(ClientMethods.LobbyChanged, l => !l.GetProperty("players")[0].GetProperty("connected").GetBoolean());
    }

    [Fact]
    public async Task Failed_join_elsewhere_keeps_the_current_room()
    {
        using var f = new ArcServerFactory();
        await using var hostA = await TestClient.ConnectAsync(f);
        await using var hostB = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var a = await hostA.InvokeAsync<CreateRoomResult>("CreateRoom");
        var b = await hostB.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.InvokeAsync<JoinResult>("Join", a.Code, "Ann");

        Assert.Equal("Name must be 1–12 characters.", await ann.ThrowsHubAsync("Join", b.Code, "   "));

        // Still bound in room A as its VIP.
        Assert.Null(await ann.TryInvokeMessageAsync("SelectGame", "counter"));
    }

    // A connection is tracked to one room at a time. If creating a second room didn't
    // release the first, the first would keep a "connected" host forever and never expire.
    [Fact]
    public async Task Creating_a_second_room_releases_the_first()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        var first = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var second = await host.InvokeAsync<CreateRoomResult>("CreateRoom");

        f.Time.Advance(new RoomOptions().IdleTimeout);
        await f.Services.GetRequiredService<RoomManager>().SweepAsync();

        Assert.Equal("Room not found.", await ann.ThrowsHubAsync("Join", first.Code, "Ann"));
        await ann.InvokeAsync<JoinResult>("Join", second.Code, "Ann");
    }
}
