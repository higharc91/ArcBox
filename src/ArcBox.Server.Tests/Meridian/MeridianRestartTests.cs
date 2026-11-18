using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Tests.Support;
using Microsoft.Data.Sqlite;

namespace ArcBox.Server.Tests.Meridian;

/// <summary>
/// A Meridian game survives a server restart, over the real hub (M6 plan, Task 5; spec success criterion 7). A TV and
/// three phones play into the endgame; the server stops and starts again on the same database; the TV resumes its room,
/// the phones rejoin their seats, and the same game plays on to the Debrief and back to the lobby.
/// </summary>
public sealed class MeridianRestartTests
{
    /// <summary>Every registered scenario (M7 plan, Task 1): a new one gets this test without editing it.</summary>
    public static TheoryData<string> Scenarios => [.. global::Meridian.Logic.ScenarioRegistry.Default.All.Select(s => s.Info.Id).Order(StringComparer.Ordinal)];

    private sealed class TempDb : IDisposable
    {
        private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("arcbox-meridian-restart-");
        public string File => Path.Combine(_dir.FullName, "arcbox.db");
        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            try { _dir.Delete(true); } catch (IOException) { }
        }
    }

    /// <summary>A TV and phones playing as MeridianHubTests' phones do, decided from their screens alone.</summary>
    private sealed class Driver(ArcServerFactory f, TestClient host, IReadOnlyList<TestClient> phones, int seed)
    {
        private readonly Random _random = new(seed);
        private readonly string?[] _acted = new string?[phones.Count];
        private readonly JsonElement?[] _views = new JsonElement?[phones.Count];
        private readonly HashSet<string> _picked = [];
        private int _presented;

        public JsonElement? HostView { get; private set; }
        public JsonElement? Ended { get; private set; }

        /// <summary>Plays until <paramref name="stop"/> holds or the game ends; returns whether it stopped on its own.</summary>
        public async Task<bool> Run(Func<Driver, bool> stop, int steps = 6000)
        {
            for (var step = 0; step < steps && Ended is null; step++)
            {
                if (stop(this))
                {
                    return true;
                }
                var busy = false;
                if (host.TakeLatest(ClientMethods.HostView) is { } view)
                {
                    HostView = view;
                }
                if (HostView is { } shown && shown.TryGetProperty("now", out var now) && now.ValueKind == JsonValueKind.Object
                    && now.GetProperty("seq").GetInt32() is var seq && seq > _presented)
                {
                    _presented = seq;
                    await host.InvokeAsync("HostSignal", "presented", JsonSerializer.SerializeToElement(new { seq }));
                    busy = true;
                }
                for (var p = 0; p < phones.Count; p++)
                {
                    _views[p] = phones[p].TakeLatest(ClientMethods.ControllerView) ?? _views[p];
                    if (phones[p].TakeLatest(ClientMethods.GameEnded) is { } end)
                    {
                        Ended = end;
                    }
                    if (_views[p] is not { } v || v.GetProperty("viewId").GetString() is not { } id || id == _acted[p])
                    {
                        continue;
                    }
                    if (MeridianHubTests.Decide(v, _random, _picked) is { } input)
                    {
                        _acted[p] = id;
                        await phones[p].InvokeAsync("SendInput", id, input);
                        busy = true;
                    }
                }
                if (busy)
                {
                    await Task.Delay(70, TestContext.Current.CancellationToken);   // under the hub's rate limit
                }
                else
                {
                    f.Time.Advance(TimeSpan.FromSeconds(1));
                    await Task.Delay(5, TestContext.Current.CancellationToken);
                }
            }
            return false;
        }
    }

    private static string? Phase(Driver d) => d.HostView?.GetProperty("phase").GetString();

    [Theory]
    [Trait("Category", "Integration")]
    [MemberData(nameof(Scenarios))]
    public async Task A_game_survives_a_server_restart_in_the_endgame(string scenario)
    {
        using var db = new TempDb();
        CreateRoomResult room;
        var seats = new List<JoinResult>();
        string? title;

        // Before: crew pick, Explore, the Breach, and into the endgame.
        using (var a = new ArcServerFactory(db.File, withMeridian: true) { MeridianScenario = scenario })
        {
            await using var host = await TestClient.ConnectAsync(a);
            var phones = new List<TestClient>();
            try
            {
                room = await host.InvokeAsync<CreateRoomResult>("CreateRoom");
                foreach (var name in new[] { "Ann", "Bo", "Cy" })
                {
                    var phone = await TestClient.ConnectAsync(a);
                    seats.Add(await phone.InvokeAsync<JoinResult>("Join", room.Code, name));
                    phones.Add(phone);
                }
                await phones[0].InvokeAsync("SelectGame", "meridian");
                await phones[0].InvokeAsync("StartGame");
                var before = new Driver(a, host, phones, seed: 7);
                Assert.True(await before.Run(d => Phase(d) == "endgame" && d.HostView?.GetProperty("now").ValueKind != JsonValueKind.Object),
                            "the game never reached the endgame");
                title = before.HostView!.Value.GetProperty("scenario").GetProperty("title").GetString();
            }
            finally
            {
                foreach (var phone in phones)
                {
                    await phone.DisposeAsync();
                }
            }
        }

        // After: the same database, a new server. The TV resumes the room; the phones rejoin their seats.
        using var b = new ArcServerFactory(db.File, withMeridian: true) { MeridianScenario = scenario };
        await using var tv = await TestClient.ConnectAsync(b);
        await tv.InvokeAsync("ResumeHost", room.Code, room.HostToken);
        var resumed = await tv.WaitForAsync(ClientMethods.HostView, v => v.TryGetProperty("phase", out _));
        Assert.Equal("Reconnecting…", resumed.GetProperty("banner").GetString());
        Assert.Equal("endgame", resumed.GetProperty("phase").GetString());
        Assert.Equal(title, resumed.GetProperty("scenario").GetProperty("title").GetString());   // the same game, not a new one
        Assert.Equal(0, tv.Unclaimed(ClientMethods.GameEnded));

        var back = new List<TestClient>();
        try
        {
            foreach (var seat in seats)
            {
                var phone = await TestClient.ConnectAsync(b);
                await phone.InvokeAsync("Rejoin", room.Code, seat.PlayerId.Value, seat.Secret);
                back.Add(phone);
            }
            var after = new Driver(b, tv, back, seed: 8);
            await after.Run(_ => false);
            var ended = Assert.IsType<JsonElement>(after.Ended);
            Assert.DoesNotContain("interrupted", ended.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
            await tv.WaitForAsync(ClientMethods.LobbyChanged, l => !l.GetProperty("inGame").GetBoolean());
        }
        finally
        {
            foreach (var phone in back)
            {
                await phone.DisposeAsync();
            }
        }
    }
}
