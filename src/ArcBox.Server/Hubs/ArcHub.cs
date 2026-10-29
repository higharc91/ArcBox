using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace ArcBox.Server.Hubs;

// Hub methods only validate input, find the room and post to its inbox (spec §5.3).
public sealed class ArcHub(RoomManager rooms) : Hub
{
    public Task<CreateRoomResult> CreateRoom() => WithHubErrors(async () =>
    {
        var previous = rooms.RoomFor(Context.ConnectionId);
        var (_, result) = await rooms.CreateRoomAsync(Context.ConnectionId);
        if (previous is not null)
        {
            await LeaveAsync(previous);
        }
        return result;
    });

    public Task ResumeHost(string code, string hostToken) => WithHubErrors(async () =>
    {
        var room = await FindRoomAsync(code);
        await room.BindHostAsync(Context.ConnectionId, hostToken);
        await MoveToAsync(room);
    });

    /// <summary>What this phone may take, without taking anything. The caller isn't placed in the room.</summary>
    public Task<RoomPeek> Peek(PeekRequest request) => WithHubErrors(async () =>
        await (await FindRoomAsync(request.Code)).PeekAsync(request.ProfileKeys ?? []));

    public Task<JoinResult> TakeSeat(TakeSeatRequest request) => WithHubErrors(async () =>
    {
        var room = await FindRoomAsync(request.Code);
        var result = await room.TakeSeatAsync(Context.ConnectionId, request);
        await MoveToAsync(room);
        return result;
    });

    // Kept for one release so a stale phone tab still works.
    public Task<JoinResult> Join(string code, string name) => TakeSeat(new TakeSeatRequest(code, NewName: name ?? ""));

    public Task Rejoin(string code, string playerId, string secret) => WithHubErrors(async () =>
    {
        var room = await FindRoomAsync(code);
        await room.RejoinAsync(Context.ConnectionId, new PlayerId(playerId), secret);
        await MoveToAsync(room);
    });

    public Task SelectGame(string gameId) =>
        WithHubErrors(() => CurrentRoom().SelectGameAsync(Context.ConnectionId, gameId));

    public Task SelectCampaign(string campaignId) =>
        WithHubErrors(() => CurrentRoom().SelectCampaignAsync(Context.ConnectionId, campaignId ?? ""));

    /// <summary>The VIP creates a campaign for the selected game and becomes its owner; returns the VIP's new profile key.</summary>
    public Task<ProfileKey> CreateCampaign(string name) =>
        WithHubErrors(() => CurrentRoom().CreateCampaignAsync(Context.ConnectionId, name ?? ""));

    /// <summary>"Who are you in this campaign?": a member, or a null member for "I'm new here" (optionally under a new name). Lobby only.</summary>
    public Task<ProfileKey?> ClaimMember(ClaimMemberRequest request) =>
        WithHubErrors(() => CurrentRoom().ClaimMemberAsync(Context.ConnectionId,
            request ?? throw new HubException("Pick a seat or enter a name.")));

    public Task StartGame() =>
        WithHubErrors(() => CurrentRoom().StartGameAsync(Context.ConnectionId));

    public Task ReturnToLobby() =>
        WithHubErrors(() => CurrentRoom().ReturnToLobbyAsync(Context.ConnectionId));

    public Task Kick(string playerId) =>
        WithHubErrors(() => CurrentRoom().KickAsync(Context.ConnectionId, new PlayerId(playerId)));

    public Task SendInput(string viewId, JsonElement payload) =>
        WithHubErrors(() => CurrentRoom().SendInputAsync(Context.ConnectionId, viewId, payload));

    /// <summary>The TV tells the running game something, e.g. that an animation finished (spec §3.7). Host only.</summary>
    public Task HostSignal(string kind, JsonElement payload) =>
        WithHubErrors(() => CurrentRoom().HostSignalAsync(Context.ConnectionId, kind, payload));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var room = rooms.RoomFor(Context.ConnectionId);
        if (room is not null)
        {
            await LeaveAsync(room);
        }
        rooms.Untrack(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    // A room that left memory (a restart, or parked when idle) is rebuilt here from its stored record.
    private async Task<Room> FindRoomAsync(string code) =>
        await rooms.FindAsync(code) ?? throw new HubException("Room not found.");

    private Room CurrentRoom() =>
        rooms.RoomFor(Context.ConnectionId) ?? throw new HubException("You're not in a room.");

    // A connection is in at most one room. The old room is left only after the new one
    // has accepted the caller, so a failed Join or Rejoin doesn't strand them.
    private async Task MoveToAsync(Room room)
    {
        var previous = rooms.RoomFor(Context.ConnectionId);
        if (previous is not null && previous != room)
        {
            await LeaveAsync(previous);
        }
        rooms.Track(Context.ConnectionId, room);
    }

    private async Task LeaveAsync(Room room)
    {
        try
        {
            await room.ConnectionLostAsync(Context.ConnectionId);
        }
        catch (RoomException)
        {
            // The room has already closed, so there is nothing to leave.
        }
    }

    // Room errors are user-facing; HubException is the only exception whose message
    // SignalR forwards to the caller.
    private static async Task WithHubErrors(Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (RoomException ex)
        {
            throw new HubException(ex.Message, ex);
        }
    }

    private static async Task<T> WithHubErrors<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (RoomException ex)
        {
            throw new HubException(ex.Message, ex);
        }
    }
}
