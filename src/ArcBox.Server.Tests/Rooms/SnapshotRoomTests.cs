using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Tests.Support;
using ArcBox.Server.Tests.TestGames;
using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Tests.Rooms;

/// <summary>
/// Rooms resume ISnapshotGames from their newest snapshot after a restart (Meridian spec §5.6; M6 plan, Task 2), with
/// <see cref="TallyGame"/>: a counter that saves its count when it changes.
/// </summary>
public sealed class SnapshotRoomTests
{
    private const string Restarted = "Game interrupted: the server restarted.";

    private sealed class TempDb : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("arcbox-snapshot-");
        public string File => Path.Combine(_dir.FullName, "arcbox.db");
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { _dir.Delete(true); } catch (IOException) { }
        }
    }

    private static object Press(string option) => new { pressed = option };

    /// <summary>A room playing tally, with one phone, pressed <paramref name="adds"/> times; then the first server stops.</summary>
    private static async Task<(CreateRoomResult Room, JoinResult Ann, string Transcript)> Play(TempDb db, int adds, bool end = false)
    {
        using var a = new ArcServerFactory(db.File);
        await using var host = await TestClient.ConnectAsync(a);
        await using var phone = await TestClient.ConnectAsync(a);
        var room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
        var ann = await phone.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
        await host.InvokeAsync("SelectGame", "tally");
        await host.InvokeAsync("StartGame");
        for (var i = 1; i <= adds + (end ? 1 : 0); i++)
        {
            var view = await phone.WaitForAsync(ClientMethods.ControllerView);
            await phone.InvokeAsync("SendInput", view.GetProperty("viewId").GetString(), JsonSerializer.SerializeToElement(Press(i <= adds ? "add" : "end")));
        }
        if (end)
        {
            await host.WaitForAsync(ClientMethods.GameEnded);
        }
        else
        {
            await host.WaitForAsync(ClientMethods.HostView, v => v.TryGetProperty("count", out var c) && c.GetInt32() == adds);
        }
        return (room, ann, host.Transcript() + "\n" + phone.Transcript());
    }

    [Fact]
    public async Task A_restart_resumes_the_game_from_its_newest_snapshot()
    {
        using var db = new TempDb();
        var (room, ann, before) = await Play(db, adds: 3);

        using var b = new ArcServerFactory(db.File);
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        var view = await tv.WaitForAsync(ClientMethods.HostView, v => v.TryGetProperty("count", out _));
        Assert.Equal(3, view.GetProperty("count").GetInt32());
        Assert.True(view.GetProperty("resumed").GetBoolean());

        // And it plays on: the phone rejoins its seat and presses again.
        await using var phone = await TestClient.ConnectAsync(b);
        await phone.InvokeAsync("Rejoin", room.Code, ann.PlayerId.Value, ann.Secret);
        var buttons = await phone.WaitForAsync(ClientMethods.ControllerView);
        await phone.InvokeAsync("SendInput", buttons.GetProperty("viewId").GetString(), JsonSerializer.SerializeToElement(Press("add")));
        await tv.WaitForAsync(ClientMethods.HostView, v => v.TryGetProperty("count", out var c) && c.GetInt32() == 4);

        // The snapshot is the server's alone: no message to the TV or a phone ever carried it.
        Assert.DoesNotContain(TallyGame.Secret, before + tv.Transcript() + phone.Transcript());
    }

    [Fact]
    public async Task A_restart_before_the_first_snapshot_interrupts_the_game()
    {
        using var db = new TempDb();
        var (room, _, _) = await Play(db, adds: 0);   // started, but nothing worth saving yet

        using var b = new ArcServerFactory(db.File);
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        Assert.Equal(Restarted, (await tv.WaitForAsync(ClientMethods.GameEnded)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task A_snapshot_that_cant_be_restored_interrupts_the_game()
    {
        using var db = new TempDb();
        var (room, _, _) = await Play(db, adds: 2);
        using (var connection = new SqliteConnection($"Data Source={db.File}"))
        {
            connection.Open();
            using var corrupt = connection.CreateCommand();
            corrupt.CommandText = "UPDATE room_game_state SET snapshot = '{\"from\":\"an older build\"}'";
            Assert.Equal(1, corrupt.ExecuteNonQuery());
        }

        using var b = new ArcServerFactory(db.File);
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        Assert.Equal(Restarted, (await tv.WaitForAsync(ClientMethods.GameEnded)).GetProperty("message").GetString());
    }

    [Fact]
    public async Task An_ended_game_leaves_no_snapshot_and_isnt_resumed()
    {
        using var db = new TempDb();
        var (room, _, _) = await Play(db, adds: 2, end: true);
        using (var connection = new SqliteConnection($"Data Source={db.File}"))
        {
            connection.Open();
            using var count = connection.CreateCommand();
            count.CommandText = "SELECT COUNT(*) FROM room_game_state";
            Assert.Equal(0L, (long)count.ExecuteScalar()!);
        }

        using var b = new ArcServerFactory(db.File);
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        var lobby = await tv.WaitForAsync(ClientMethods.LobbyChanged);
        Assert.False(lobby.GetProperty("inGame").GetBoolean());
        Assert.Equal(0, tv.Unclaimed(ClientMethods.GameEnded));
    }
}
