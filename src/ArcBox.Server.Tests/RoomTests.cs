using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Rooms;
using ArcBox.Server.Tests.Support;
using Counter.Logic;
using Xunit;

namespace ArcBox.Server.Tests;

public class RoomTests
{
    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    [Fact]
    public async Task Host_bind_with_wrong_token_fails()
    {
        await using var f = new RoomFixture();
        var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.BindHostAsync("h", "nope"));
        Assert.Equal("Room not found.", ex.Message);
    }

    [Fact]
    public async Task Join_broadcasts_lobby_to_host_and_players()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        var lobby = (LobbyState)f.Sender.Of("host", ClientMethods.LobbyChanged).Last().Args[0]!;
        Assert.Equal("Ann", lobby.Players.Single().Name);
        Assert.NotEmpty(f.Sender.Of("c1", ClientMethods.LobbyChanged));
    }

    [Fact]
    public async Task Vip_can_select_and_start_non_vip_cannot()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        await f.JoinAsync("Bob", "c2");
        var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.SelectGameAsync("c2", "counter"));
        Assert.Equal("Only the VIP or host can do that.", ex.Message);
        await f.Room.SelectGameAsync("c1", "counter");
        await f.Room.StartGameAsync("c1");
        Assert.Equal(RoomStatus.InGame, await f.Room.GetStatusAsync());
        Assert.NotEmpty(f.Sender.Of("c2", ClientMethods.ControllerView));
        Assert.NotEmpty(f.Sender.Of("host", ClientMethods.HostView));
    }

    // Review Focus #4
    [Fact]
    public async Task Double_start_fails_and_game_keeps_running()
    {
        await using var f = await RoomFixture.InGameAsync("counter", players: 2);
        var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.StartGameAsync("host"));
        Assert.Equal("A game is already running.", ex.Message);
        Assert.Equal(RoomStatus.InGame, await f.Room.GetStatusAsync());
    }

    [Fact]
    public async Task Start_checks_player_count()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.Room.SelectGameAsync("host", "counter");
        var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.StartGameAsync("host"));
        Assert.Equal("This game needs 1–8 players.", ex.Message);
    }

    [Fact]
    public async Task Join_during_game_is_rejected()
    {
        await using var f = await RoomFixture.InGameAsync("counter", players: 1);
        var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.JoinAsync("c9", "Late"));
        Assert.Equal("Game in progress, join after this round.", ex.Message);
    }

    [Fact]
    public async Task Rejoin_mid_game_sends_full_controller_snapshot()
    {
        await using var f = await RoomFixture.InGameAsync("counter", players: 2);
        var p = f.Joined["c1"];
        await f.Room.ConnectionLostAsync("c1");
        await f.Room.RejoinAsync("c1b", p.PlayerId, p.Secret);
        var view = (JsonElement)f.Sender.Of("c1b", ClientMethods.ControllerView).Single().Args[0]!;
        Assert.Equal("buttons", view.GetProperty("kind").GetString());
    }

    // Review Focus #1 (room level)
    [Fact]
    public async Task Stale_connection_loss_after_rejoin_keeps_player_connected()
    {
        await using var f = await RoomFixture.InGameAsync("counter", players: 1);
        var p = f.Joined["c1"];
        await f.Room.RejoinAsync("c1b", p.PlayerId, p.Secret);
        await f.Room.ConnectionLostAsync("c1");
        var lobby = (LobbyState)f.Sender.Of("host", ClientMethods.LobbyChanged).Last().Args[0]!;
        Assert.True(lobby.Players.Single().Connected);
    }

    [Fact]
    public async Task Crashing_game_returns_room_to_lobby_with_message()
    {
        await using var f = await RoomFixture.InGameAsync("thrower", players: 1);
        var viewId = f.LastViewId("c1");
        await f.Room.SendInputAsync("c1", viewId, Json(new { pressed = "boom" }));
        Assert.Equal(RoomStatus.Lobby, await f.Room.GetStatusAsync());
        var results = (GameResults)f.Sender.Of("c1", ClientMethods.GameEnded).Single().Args[0]!;
        Assert.Equal("Game crashed", results.Message);
    }

    // Review Focus #3 (room level)
    [Fact]
    public async Task Timer_ending_game_returns_to_lobby()
    {
        await using var f = await RoomFixture.InGameAsync("counter", players: 1);
        f.Time.Advance(CounterGame.Duration);
        await f.Room.FlushAsync();
        Assert.Equal(RoomStatus.Lobby, await f.Room.GetStatusAsync());
        Assert.Single(f.Sender.Of("host", ClientMethods.GameEnded));
    }

    [Fact]
    public async Task Kick_only_in_lobby_and_only_by_host()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        await f.JoinAsync("Bob", "c2");
        await Assert.ThrowsAsync<RoomException>(() => f.Room.KickAsync("c1", new PlayerId("p2")));
        await f.Room.KickAsync("host", new PlayerId("p2"));
        Assert.Single(f.Sender.Of("c2", ClientMethods.Kicked));
    }

    [Fact]
    public async Task Expires_after_idle_timeout_without_host()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.Room.ConnectionLostAsync("host");
        f.Time.Advance(TimeSpan.FromMinutes(9));
        Assert.False(await f.Room.IsExpiredAsync());
        f.Time.Advance(TimeSpan.FromMinutes(1));
        Assert.True(await f.Room.IsExpiredAsync());
    }

    [Fact]
    public async Task ForceClose_notifies_everyone_and_rejects_further_calls()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");
        f.Room.ForceClose("game stopped responding");
        Assert.Equal("game stopped responding", f.Sender.Of("c1", ClientMethods.RoomClosed).Single().Args[0]);
        await Assert.ThrowsAsync<RoomException>(() => f.Room.JoinAsync("c2", "Bob"));
    }
}
