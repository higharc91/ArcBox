using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games;
using ArcBox.Server.Tests.Support;
using ArcBox.Server.Tests.TestGames;

namespace ArcBox.Server.Tests.Rooms;

/// <summary>Issue #77: a TV that asks for patches gets the change from the view it has, and a whole view whenever it might not have one.</summary>
public class HostPatchTests
{
    private static async Task<RoomFixture> StartedAsync(bool patches)
    {
        var f = new RoomFixture(game: new GameRegistration(() => new PatchGame()));
        await f.HostAsync();
        if (patches)
            await f.Room.UseHostPatchesAsync("host");
        await f.JoinAsync("Ann", "c1");
        await f.Room.SelectGameAsync("host", PatchGame.Id);
        await f.Room.StartGameAsync("host");
        return f;
    }

    private static Task PressAsync(RoomFixture f, string what = "press") =>
        f.Room.SendInputAsync("c1", f.LastViewId("c1"), JsonSerializer.SerializeToElement(new { pressed = what }, ArcJson.Options));

    /// <summary>Every view the host was sent, whole or patched, rebuilt in order as the TV would.</summary>
    private static List<JsonElement> Rebuilt(RoomFixture f, string host = "host")
    {
        var views = new List<JsonElement>();
        JsonElement? current = null;
        foreach (var m in f.Sender.All(host).Where(m => m.Method is ClientMethods.HostView or ClientMethods.HostViewPatch))
        {
            var payload = (JsonElement)m.Args[1]!;
            current = m.Method == ClientMethods.HostView ? payload : HostViewPatch.Apply(current!.Value, payload);
            views.Add(current.Value);
        }
        return views;
    }

    [Fact]
    public async Task A_host_that_didnt_ask_gets_whole_views_as_before()
    {
        await using var f = await StartedAsync(patches: false);
        for (var i = 0; i < 5; i++)
            await PressAsync(f);

        Assert.Empty(f.Sender.Of("host", ClientMethods.HostViewPatch));
        Assert.Equal(5, f.Sender.Of("host", ClientMethods.HostView).Last().Args[1] is JsonElement v ? v.GetProperty("count").GetInt32() : -1);
    }

    [Fact]
    public async Task A_host_that_asked_gets_a_whole_view_first_then_small_patches_that_rebuild_every_view()
    {
        await using var f = await StartedAsync(patches: true);
        for (var i = 1; i <= 6; i++)
            await PressAsync(f);
        await PressAsync(f, "move");

        var whole = f.Sender.Of("host", ClientMethods.HostView);
        var patches = f.Sender.Of("host", ClientMethods.HostViewPatch);
        Assert.Single(whole);
        Assert.Equal(7, patches.Count);
        var full = ((JsonElement)whole[0].Args[1]!).GetRawText().Length;
        Assert.All(patches, p => Assert.True(((JsonElement)p.Args[1]!).GetRawText().Length * 4 < full, "a press patches a field or two, not the view"));

        var last = Rebuilt(f).Last();
        Assert.Equal(6, last.GetProperty("count").GetInt32());
        Assert.Equal(JsonValueKind.Null, last.GetProperty("note").ValueKind);    // 6 % 3 == 0: an explicit null survives the patch
        Assert.Equal("open", last.GetProperty("things")[0].GetProperty("state").GetString());
        Assert.Equal(20, last.GetProperty("things").GetArrayLength());
    }

    [Fact]
    public async Task A_rebound_host_gets_a_whole_view_and_whole_views_until_it_asks_again()
    {
        await using var f = await StartedAsync(patches: true);
        await PressAsync(f);
        await f.Room.BindHostAsync("tv2", "host-token");
        await PressAsync(f);
        Assert.Equal(2, f.Sender.Of("tv2", ClientMethods.HostView).Count);
        Assert.Empty(f.Sender.Of("tv2", ClientMethods.HostViewPatch));

        await f.Room.UseHostPatchesAsync("tv2");
        await PressAsync(f);
        Assert.Single(f.Sender.Of("tv2", ClientMethods.HostViewPatch));
        Assert.Equal(3, Rebuilt(f, "tv2").Last().GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Resend_sends_the_whole_view_and_patches_follow_from_it()
    {
        await using var f = await StartedAsync(patches: true);
        await PressAsync(f);
        await f.Room.ResendHostViewAsync("host");
        await PressAsync(f);

        Assert.Equal(2, f.Sender.Of("host", ClientMethods.HostView).Count);
        Assert.Equal(2, Rebuilt(f).Last().GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task A_new_game_starts_with_a_whole_view()
    {
        await using var f = await StartedAsync(patches: true);
        await PressAsync(f);
        await f.Room.ReturnToLobbyAsync("host");
        await f.Room.StartGameAsync("host");

        var sent = f.Sender.All("host").Where(m => m.Method is ClientMethods.HostView or ClientMethods.HostViewPatch).ToList();
        Assert.Equal(ClientMethods.HostView, sent.Last().Method);
        Assert.Equal(0, ((JsonElement)sent.Last().Args[1]!).GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Only_the_host_can_ask_for_patches_or_a_resend()
    {
        await using var f = await StartedAsync(patches: false);
        await Assert.ThrowsAsync<RoomException>(() => f.Room.UseHostPatchesAsync("c1"));
        await Assert.ThrowsAsync<RoomException>(() => f.Room.ResendHostViewAsync("c1"));
    }
}
