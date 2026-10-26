using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Rooms;
using Microsoft.AspNetCore.SignalR;

namespace ArcBox.Server.Hubs;

/// <summary>
/// The one hub (spec §5.3). Methods only validate, look up the room and post to its inbox;
/// a <see cref="RoomException"/> reaches the client as a <see cref="HubException"/> with the same message.
/// </summary>
public sealed class ArcHub(RoomManager rooms) : Hub
{
    public Task<CreateRoomResult> CreateRoom() => Guard(async () =>
    {
        await LeaveOtherRoomAsync(null);
        var (_, result) = await rooms.CreateRoomAsync(Context.ConnectionId);
        return result;
    });

    public Task ResumeHost(string code, string hostToken) => Guard(async () =>
    {
        var room = Find(code);
        await LeaveOtherRoomAsync(room);
        await room.BindHostAsync(Context.ConnectionId, hostToken);
        rooms.Track(Context.ConnectionId, room);
    });

    public Task<JoinResult> Join(string code, string name) => Guard(async () =>
    {
        var room = Find(code);
        await LeaveOtherRoomAsync(room);
        var result = await room.JoinAsync(Context.ConnectionId, name);
        rooms.Track(Context.ConnectionId, room);
        return result;
    });

    public Task Rejoin(string code, string playerId, string secret) => Guard(async () =>
    {
        var room = Find(code);
        await LeaveOtherRoomAsync(room);
        await room.RejoinAsync(Context.ConnectionId, new PlayerId(playerId), secret);
        rooms.Track(Context.ConnectionId, room);
    });

    public Task SelectGame(string gameId) => Guard(() => Current().SelectGameAsync(Context.ConnectionId, gameId));

    public Task StartGame() => Guard(() => Current().StartGameAsync(Context.ConnectionId));

    public Task ReturnToLobby() => Guard(() => Current().ReturnToLobbyAsync(Context.ConnectionId));

    public Task Kick(string playerId) => Guard(() => Current().KickAsync(Context.ConnectionId, new PlayerId(playerId)));

    public Task SendInput(string viewId, JsonElement payload) => Guard(() => Current().SendInputAsync(Context.ConnectionId, viewId, payload));

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (rooms.RoomFor(Context.ConnectionId) is { } room)
        {
            await LoseConnectionAsync(room);
        }
        rooms.Untrack(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private Room Find(string code) => rooms.Find(code) ?? throw new HubException("Room not found.");

    private Room Current() => rooms.RoomFor(Context.ConnectionId) ?? throw new HubException("You're not in a room.");

    /// <summary>A connection lives in one room: tell the room it was in that it has gone.</summary>
    private async Task LeaveOtherRoomAsync(Room? next)
    {
        if (rooms.RoomFor(Context.ConnectionId) is { } old && old != next)
        {
            await LoseConnectionAsync(old);
            rooms.Untrack(Context.ConnectionId);
        }
    }

    private async Task LoseConnectionAsync(Room room)
    {
        try
        {
            await room.ConnectionLostAsync(Context.ConnectionId);
        }
        catch (RoomException)
        {
            // The room has already closed.
        }
    }

    private static async Task Guard(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (RoomException ex)
        {
            throw new HubException(ex.Message);
        }
    }

    private static async Task<T> Guard<T>(Func<Task<T>> action)
    {
        try
        {
            return await action();
        }
        catch (RoomException ex)
        {
            throw new HubException(ex.Message);
        }
    }
}
