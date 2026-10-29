using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Rooms;
using ArcBox.Server.Tests.Support;

namespace ArcBox.Server.Tests;

public class HostSignalTests
{
    private const string NotHost = "Only the host can do that.";
    private const string Invalid = "Invalid signal.";

    private static JsonElement Json(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    private static bool LogEndsWith(JsonElement hostView, string entry)
    {
        var log = hostView.GetProperty("log");
        return log.GetArrayLength() > 0 && log[log.GetArrayLength() - 1].GetString() == entry;
    }

    /// <summary>A string payload whose raw JSON text (quotes included) is exactly <paramref name="bytes"/> UTF-8 bytes.</summary>
    private static JsonElement PayloadOfBytes(int bytes) => Json(new string('x', bytes - 2));

    private static async Task<(TestClient Host, TestClient Phone)> StartAsync(ArcServerFactory f, string gameId)
    {
        var host = await TestClient.ConnectAsync(f);
        var code = (await host.InvokeAsync<CreateRoomResult>("CreateRoom")).Code;
        var phone = await TestClient.ConnectAsync(f);
        await phone.InvokeAsync<JoinResult>("Join", code, "Ann");
        await host.InvokeAsync("SelectGame", gameId);
        await host.InvokeAsync("StartGame");
        return (host, phone);
    }

    [Fact]
    public async Task Host_signal_reaches_game()
    {
        using var f = new ArcServerFactory();
        var (host, phone) = await StartAsync(f, "plain-ledger");
        await using var _h = host;
        await using var _p = phone;

        await host.InvokeAsync("HostSignal", "done", Json(new { seq = 41 }));

        await host.WaitForAsync(ClientMethods.HostView, v => LogEndsWith(v, "signal:done:{\"seq\":41}"));
    }

    [Fact]
    public async Task Phone_cannot_signal()
    {
        using var f = new ArcServerFactory();
        var (host, phone) = await StartAsync(f, "plain-ledger");
        await using var _h = host;
        await using var _p = phone;

        Assert.Equal(NotHost, await phone.ThrowsHubAsync("HostSignal", "done", Json(new { seq = 1 })));
        // The host check comes first, so a phone learns nothing about validation.
        Assert.Equal(NotHost, await phone.ThrowsHubAsync("HostSignal", "a b", Json(new { seq = 1 })));
    }

    [Fact]
    public async Task Bad_kind_and_oversized_payload_rejected()
    {
        using var f = new ArcServerFactory();
        var (host, phone) = await StartAsync(f, "plain-ledger");
        await using var _h = host;
        await using var _p = phone;
        var ok = Json(new { seq = 1 });

        Assert.Equal(Invalid, await host.ThrowsHubAsync("HostSignal", "a b", ok));
        Assert.Equal(Invalid, await host.ThrowsHubAsync("HostSignal", "", ok));
        Assert.Equal(Invalid, await host.ThrowsHubAsync("HostSignal", new string('k', 33), ok));
        Assert.Equal(Invalid, await host.ThrowsHubAsync("HostSignal", "done", PayloadOfBytes(4097)));

        // The limits are inclusive: 32 characters and 4096 bytes get through.
        await host.InvokeAsync("HostSignal", new string('k', 32), PayloadOfBytes(4096));
        await host.WaitForAsync(ClientMethods.HostView,
            v => v.GetProperty("log").EnumerateArray().Any(e => e.GetString()!.StartsWith("signal:" + new string('k', 32) + ":")));
    }

    [Fact]
    public async Task Oversize_is_counted_in_utf8_bytes()
    {
        await using var f = await RoomFixture.InGameAsync("plain-ledger");

        // 1366 three-byte characters plus two quotes is 4100 bytes but only 1368 characters.
        var wide = Json(new string('€', 1366));
        var ex = await Assert.ThrowsAsync<RoomException>(() => f.Room.HostSignalAsync("host", "done", wide));
        Assert.Equal(Invalid, ex.Message);
    }

    [Fact]
    public async Task Non_aware_game_ignores()
    {
        await using var f = await RoomFixture.InGameAsync("counter");
        var hostViews = f.Sender.Of("host", ClientMethods.HostView).Count;

        await f.Room.HostSignalAsync("host", "done", Json(new { seq = 1 }));

        Assert.Equal(hostViews, f.Sender.Of("host", ClientMethods.HostView).Count);
        Assert.Equal(RoomStatus.InGame, await f.Room.GetStatusAsync());
    }

    [Fact]
    public async Task Signal_with_no_game_running_is_ignored()
    {
        await using var f = new RoomFixture();
        await f.HostAsync();
        await f.JoinAsync("Ann", "c1");

        await f.Room.HostSignalAsync("host", "done", Json(new { seq = 1 }));

        Assert.Equal(RoomStatus.Lobby, await f.Room.GetStatusAsync());
    }

    [Fact]
    public async Task Crash_in_OnHostSignal_ends_game_with_Game_crashed()
    {
        await using var f = await RoomFixture.InGameAsync("plain-ledger");

        await f.Room.HostSignalAsync("host", "boom", Json(new { seq = 1 }));

        Assert.Equal(RoomStatus.Lobby, await f.Room.GetStatusAsync());
        var results = (GameResults)f.Sender.Of("host", ClientMethods.GameEnded).Single().Args[0]!;
        Assert.Equal("Game crashed", results.Message);
    }
}
