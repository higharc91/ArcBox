using ArcBox.Host.Core.Tests.Support;
using ArcBox.Sdk.Protocol;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace ArcBox.Host.Core.Tests;

public class HostClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_room_and_receive_lobby()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);

        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();

        var lobby = await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Code == room.Code);
        Assert.Empty(lobby.Lobby.Players);
        Assert.Contains(lobby.Lobby.Games, g => g.Id == "counter");
        Assert.Equal(4, room.Code.Length);
        Assert.False(string.IsNullOrEmpty(room.HostToken));
    }

    [Fact]
    public async Task Connect_reports_connecting_then_connected()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);

        await host.ConnectAsync(Ct);

        await events.WaitForAsync<ConnectionEvent>(e => e.State == HostConnectionState.Connecting);
        await events.WaitForAsync<ConnectionEvent>(e => e.State == HostConnectionState.Connected && e.Error is null);
        Assert.Equal(HostConnectionState.Connected, host.State);
    }

    [Fact]
    public async Task Host_sees_player_join()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();

        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;

        var lobby = await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Players.Count == 1);
        var ann = Assert.Single(lobby.Lobby.Players);
        Assert.Equal("Ann", ann.Name);
        Assert.True(ann.IsVip);
        Assert.True(ann.Connected);
    }

    [Fact]
    public async Task Resume_with_bad_token_throws_room_message()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();

        await using var other = f.CreateHostClient();
        await other.ConnectAsync(Ct);

        var wrongToken = await Assert.ThrowsAsync<HubException>(() => other.ResumeHostAsync(room.Code, "wrong-token"));
        Assert.Equal("Room not found.", wrongToken.Message);
        var noRoom = await Assert.ThrowsAsync<HubException>(() => other.ResumeHostAsync("ZZZZ", room.HostToken));
        Assert.Equal("Room not found.", noRoom.Message);
    }

    [Fact]
    public async Task Resume_with_good_token_redelivers_lobby_on_a_new_client()
    {
        using var f = new HostServerFactory();
        var first = f.CreateHostClient();
        await first.ConnectAsync(Ct);
        var room = await first.CreateRoomAsync();
        await first.DisposeAsync();

        await using var second = f.CreateHostClient();
        var events = new EventRecorder(second);
        await second.ConnectAsync(Ct);
        await second.ResumeHostAsync(room.Code, room.HostToken);

        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Code == room.Code);
    }

    [Fact]
    public async Task Reconnect_resumes_host_and_redelivers_lobby()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Players.Count == 1);
        events.Clear();

        await host.SimulateDropAsync();

        await events.WaitForAsync<ConnectionEvent>(e => e.State == HostConnectionState.Reconnecting);
        var lobby = await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Code == room.Code);
        Assert.Equal("Ann", Assert.Single(lobby.Lobby.Players).Name);
        await events.WaitForAsync<ConnectionEvent>(e => e.State == HostConnectionState.Connected);

        // Still the host: host-only commands work on the new connection.
        await host.SelectGameAsync("counter");
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.SelectedGameId == "counter");
    }

    [Fact]
    public async Task Host_view_event_for_counter_game()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;

        await host.SelectGameAsync("counter");
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.CanStart);
        await host.StartGameAsync();

        var view = await events.WaitForAsync<HostViewEvent>();
        Assert.Equal("counter", view.GameId);
        var row = view.View.GetProperty("rows")[0];
        Assert.Equal("Ann", row.GetProperty("name").GetString());
        Assert.Equal(0, row.GetProperty("count").GetInt32());
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.InGame);
    }

    [Fact]
    public async Task SignalAsync_invokes_hub()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await host.SelectGameAsync(SignalEchoGame.Id);
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.CanStart);
        await host.StartGameAsync();
        await events.WaitForAsync<HostViewEvent>();

        await host.SignalAsync("done", new { seq = 41 });

        var view = await events.WaitForAsync<HostViewEvent>(v => v.View.GetProperty("signals").GetArrayLength() == 1);
        Assert.Equal("done:{\"seq\":41}", view.View.GetProperty("signals")[0].GetString());
    }

    [Fact]
    public async Task A_running_game_reaches_the_tv_as_patches_and_raises_whole_views()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await host.SelectGameAsync(SignalEchoGame.Id);
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.CanStart);
        await host.StartGameAsync();
        await events.WaitForAsync<HostViewEvent>();

        for (var i = 1; i <= 5; i++)
            await host.SignalAsync("done", new { seq = i });

        var view = await events.WaitForAsync<HostViewEvent>(v => v.View.GetProperty("signals").GetArrayLength() == 5);
        Assert.Equal("done:{\"seq\":5}", view.View.GetProperty("signals")[4].GetString());
        Assert.Equal(20, view.View.GetProperty("things").GetArrayLength());
        Assert.True(host.PatchCounts.Applied >= 4, $"applied {host.PatchCounts.Applied} patches");
        Assert.Equal(0, host.PatchCounts.Resent);
    }

    [Fact]
    public async Task A_tv_that_doesnt_ask_for_patches_gets_whole_views()
    {
        using var f = new HostServerFactory();
        await using var host = new HostClient(f.Server.BaseAddress, () => f.Server.CreateHandler()) { UsePatches = false };
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await host.SelectGameAsync(SignalEchoGame.Id);
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.CanStart);
        await host.StartGameAsync();
        await events.WaitForAsync<HostViewEvent>();

        await host.SignalAsync("done", new { seq = 1 });
        await host.SignalAsync("done", new { seq = 2 });

        await events.WaitForAsync<HostViewEvent>(v => v.View.GetProperty("signals").GetArrayLength() == 2);
        Assert.Equal((0, 0), host.PatchCounts);
    }

    [Fact]
    public async Task A_resumed_tv_gets_a_whole_view_and_then_patches_again()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await host.SelectGameAsync(SignalEchoGame.Id);
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.CanStart);
        await host.StartGameAsync();
        await host.SignalAsync("done", new { seq = 1 });
        await events.WaitForAsync<HostViewEvent>(v => v.View.GetProperty("signals").GetArrayLength() == 1);

        // A second TV takes the room over, as a relaunched TV does: it has no view, so it's sent the whole one first.
        await using var tv2 = f.CreateHostClient();
        var events2 = new EventRecorder(tv2);
        await tv2.ConnectAsync(Ct);
        await tv2.ResumeHostAsync(room.Code, room.HostToken);
        await events2.WaitForAsync<HostViewEvent>(v => v.View.GetProperty("signals").GetArrayLength() == 1);
        await tv2.SignalAsync("done", new { seq = 2 });
        await tv2.SignalAsync("done", new { seq = 3 });

        await events2.WaitForAsync<HostViewEvent>(v => v.View.GetProperty("signals").GetArrayLength() == 3);
        Assert.True(tv2.PatchCounts.Applied >= 1);
        Assert.Equal(0, tv2.PatchCounts.Resent);
    }

    [Fact]
    public async Task SelectCampaignAsync_selects_one_of_the_games_campaigns()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await host.SelectGameAsync(CampaignTestGame.Id);
        // Only the VIP's phone creates campaigns; each new one is selected straight away.
        await phone.InvokeAsync<ProfileKey>("CreateCampaign", "First", Ct);
        await phone.InvokeAsync<ProfileKey>("CreateCampaign", "Second", Ct);
        var listed = await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Campaigns?.Count == 2 && e.Lobby.Campaign?.Name == "Second");
        var first = listed.Lobby.Campaigns!.Single(c => c.Name == "First");
        events.Clear();   // "First" was selected once already, when it was created

        await host.SelectCampaignAsync(first.Id);

        var selected = await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Campaign?.Id == first.Id);
        Assert.Equal("First", selected.Lobby.Campaign!.Name);
        Assert.Equal("Ann", Assert.Single(selected.Lobby.Campaign.Roster).Card.Title);
    }

    [Fact]
    public async Task SelectCampaignAsync_unknown_campaign_throws_room_message()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        await host.ConnectAsync(Ct);
        await host.CreateRoomAsync();
        await host.SelectGameAsync(CampaignTestGame.Id);

        var error = await Assert.ThrowsAsync<HubException>(() => host.SelectCampaignAsync("no-such-campaign"));
        Assert.Equal("Campaign not found.", error.Message);
    }

    [Fact]
    public async Task Return_to_lobby_ends_the_game_with_results()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, _) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await host.SelectGameAsync("counter");
        await host.StartGameAsync();
        await events.WaitForAsync<HostViewEvent>();

        await host.ReturnToLobbyAsync();

        var ended = await events.WaitForAsync<GameEndedEvent>();
        Assert.Equal("Game ended by host", ended.Results.Message);
        await events.WaitForAsync<LobbyEvent>(e => !e.Lobby.InGame);
    }

    [Fact]
    public async Task Kick_removes_the_player()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        var room = await host.CreateRoomAsync();
        var (phone, join) = await f.JoinAsync(room.Code, "Ann");
        await using var _phone = phone;
        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Players.Count == 1);

        await host.KickAsync(join.PlayerId.Value);

        await events.WaitForAsync<LobbyEvent>(e => e.Lobby.Players.Count == 0);
    }

    [Fact]
    public async Task Calls_before_connect_throw_invalid_operation()
    {
        await using var host = new HostClient(new Uri("http://localhost:1/"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => host.CreateRoomAsync());
    }

    [Theory]
    [InlineData("An unexpected error occurred invoking 'ResumeHost' on the server. HubException: Room not found.", "Room not found.")]
    [InlineData("An unexpected error occurred invoking 'Kick' on the server. HubException: Only the host can do that.", "Only the host can do that.")]
    [InlineData("Plain message", "Plain message")]
    [InlineData("", "")]
    public void Hub_error_message_is_stripped_to_the_room_message(string raw, string expected)
    {
        Assert.Equal(expected, HubErrors.Clean(raw));
    }

    [Fact]
    public async Task HostClient_raises_ContentRequiredEvent()
    {
        using var f = new HostServerFactory();
        await using var host = f.CreateHostClient();
        var events = new EventRecorder(host);
        await host.ConnectAsync(Ct);
        await host.CreateRoomAsync();

        await host.SelectGameAsync("counter");
        var none = await events.WaitForAsync<ContentRequiredEvent>(e => e.GameId == "counter");
        Assert.Empty(none.Packs);

        await host.SelectGameAsync(SignalEchoGame.Id);
        var required = await events.WaitForAsync<ContentRequiredEvent>(e => e.GameId == SignalEchoGame.Id);

        var pack = Assert.Single(required.Packs);
        Assert.Equal(HostServerFactory.PackId, pack.PackId);
        Assert.Equal(64, pack.Hash.Length);
        // The event carries what the cache needs: the hash the server's index lists for that pack.
        using var http = f.CreateContentClient();
        using var index = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync($"content/{SignalEchoGame.Id}/index.json", Ct));
        Assert.Equal(index.RootElement[0].GetProperty("hash").GetString(), pack.Hash);
    }
}
