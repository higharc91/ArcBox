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
        var room = FindRoom(code);
        await room.BindHostAsync(Context.ConnectionId, hostToken);
        await MoveToAsync(room);
    });

    public Task<JoinResult> Join(string code, string name) => WithHubErrors(async () =>
    {
        var room = FindRoom(code);
        var result = await room.JoinAsync(Context.ConnectionId, name);
        await MoveToAsync(room);
        return result;
    });

    public Task Rejoin(string code, string playerId, string secret) => WithHubErrors(async () =>
    {
        var room = FindRoom(code);
        await room.RejoinAsync(Context.ConnectionId, new PlayerId(playerId), secret);
        await MoveToAsync(room);
    });

    public Task SelectGame(string gameId) =>
        WithHubErrors(() => CurrentRoom().SelectGameAsync(Context.ConnectionId, gameId));

    public Task StartGame() =>
        WithHubErrors(() => CurrentRoom().StartGameAsync(Context.ConnectionId));

    public Task ReturnToLobby() =>
        WithHubErrors(() => CurrentRoom().ReturnToLobbyAsync(Context.ConnectionId));

    public Task Kick(string playerId) =>
        WithHubErrors(() => CurrentRoom().KickAsync(Context.ConnectionId, new PlayerId(playerId)));

    public Task SendInput(string viewId, JsonElement payload) =>
        WithHubErrors(() => CurrentRoom().SendInputAsync(Context.ConnectionId, viewId, payload));

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

    private Room FindRoom(string code) =>
        rooms.Find(code) ?? throw new HubException("Room not found.");

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
