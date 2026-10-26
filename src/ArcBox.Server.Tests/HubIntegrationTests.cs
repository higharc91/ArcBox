using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games.Redline;
using ArcBox.Server.Tests.Support;
using Counter.Logic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ArcBox.Server.Tests;

public class HubIntegrationTests
{
    [Fact]
    public async Task Healthz_and_placeholder_page_respond()
    {
        using var f = new ArcServerFactory();
        var http = f.CreateClient();

        Assert.Equal("ok", await http.GetStringAsync("/healthz", TestContext.Current.CancellationToken));
        Assert.Contains("ArcBox server is running", await http.GetStringAsync("/", TestContext.Current.CancellationToken));
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
        await host.WaitForAsync(ClientMethods.HostView, v => v.GetProperty("rows")[0].GetProperty("count").GetInt32() == 1);

        f.Time.Advance(CounterGame.Duration);
        var ended = await ann.WaitForAsync(ClientMethods.GameEnded);
        Assert.Equal("Ann", ended.GetProperty("scores")[0].GetProperty("name").GetString());
        Assert.Equal(1, ended.GetProperty("scores")[0].GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task Room_errors_reach_client_as_exact_messages()
    {
        using var f = new ArcServerFactory();
        await using var c = await TestClient.ConnectAsync(f);

        Assert.Equal("Room not found.", await c.ThrowsHubAsync("Join", "ZZZZ", "Ann"));
        Assert.Equal("Room not found.", await c.ThrowsHubAsync("Rejoin", "ZZZZ", "p1", "secret"));
        Assert.Equal("Room not found.", await c.ThrowsHubAsync("ResumeHost", "ZZZZ", "token"));
        Assert.Equal("You're not in a room.", await c.ThrowsHubAsync("StartGame"));

        var room = await c.InvokeAsync<CreateRoomResult>("CreateRoom");
        Assert.Equal("Pick a game first.", await c.ThrowsHubAsync("StartGame"));
        Assert.Equal("Room not found.", await c.ThrowsHubAsync("ResumeHost", room.Code, "wrong-token"));
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
        Assert.Equal("Game crashed", (await ann.WaitForAsync(ClientMethods.GameEnded)).GetProperty("message").GetString());
        await ann.WaitForAsync(ClientMethods.LobbyChanged, l => !l.GetProperty("inGame").GetBoolean()
            && l.GetProperty("lastResults").GetProperty("message").GetString() == "Game crashed");
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
        Assert.All(messages, Assert.Null);

        f.Time.Advance(CounterGame.Duration);
        var score = (await ann.WaitForAsync(ClientMethods.GameEnded)).GetProperty("scores")[0].GetProperty("score").GetInt32();
        Assert.InRange(score, 1, 59);
    }

    [Fact]
    public async Task Joining_another_room_leaves_the_old_one()
    {
        using var f = new ArcServerFactory();
        await using var host1 = await TestClient.ConnectAsync(f);
        await using var host2 = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);

        var room1 = await host1.InvokeAsync<CreateRoomResult>("CreateRoom");
        var room2 = await host2.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.InvokeAsync<JoinResult>("Join", room1.Code, "Ann");
        await ann.InvokeAsync<JoinResult>("Join", room2.Code, "Ann");

        await host1.WaitForAsync(ClientMethods.LobbyChanged, l => l.GetProperty("players").GetArrayLength() == 1
            && !l.GetProperty("players")[0].GetProperty("connected").GetBoolean());

        // Commands now go to the room this connection joined last.
        await ann.InvokeAsync("SelectGame", "counter");
        await host2.WaitForAsync(ClientMethods.LobbyChanged, l => l.GetProperty("selectedGameId").GetString() == "counter");
    }

    [Fact]
    public async Task Lobby_lists_redline()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);

        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");

        var lobby = await ann.WaitForAsync(ClientMethods.LobbyChanged);
        Assert.Contains("redline", lobby.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("id").GetString()));
    }

    [Fact]
    public void Test_server_never_runs_the_opentdb_pool()
    {
        using var f = new ArcServerFactory();

        Assert.DoesNotContain(f.Services.GetServices<IHostedService>(), s => s is OpenTdbQuestionPool);
    }

    [Fact]
    public async Task Redline_starts_over_signalr()
    {
        using var f = new ArcServerFactory();
        await using var host = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        await using var bob = await TestClient.ConnectAsync(f);

        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.InvokeAsync<JoinResult>("Join", room.Code, "Ann");
        await bob.InvokeAsync<JoinResult>("Join", room.Code, "Bob");   // REDLINE needs at least 2 players
        await ann.InvokeAsync("SelectGame", "redline");
        await ann.InvokeAsync("StartGame");

        var pick = await ann.WaitForAsync(ClientMethods.ControllerView);
        Assert.Equal("choice", pick.GetProperty("kind").GetString());
        Assert.Equal("Pick your driver", pick.GetProperty("prompt").GetString());
        Assert.Equal(22, pick.GetProperty("options").GetArrayLength());

        var hostView = await host.WaitForArgsAsync(ClientMethods.HostView);
        Assert.Equal("redline", hostView[0].GetString());
        Assert.Equal("DriverSelect", hostView[1].GetProperty("phase").GetString());
        Assert.Equal(22, hostView[1].GetProperty("drivers").GetArrayLength());
    }
}
