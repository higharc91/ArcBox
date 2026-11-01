using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games;
using ArcBox.Server.Tests.Support;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Tests.Tactics;

/// <summary>
/// Hold and release through a real room on a fake clock (overworld 5b §1): what reaches whom, and every way a stop can be lost.
/// The game's own tests feed it input directly; these go through the checks in front of it, the viewId and the rate limit.
/// </summary>
public sealed class WalkingRoomTests
{
    private const string Vip = "c1";

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(100);

    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    /// <summary>The bytes a phone sends: a stop is an explicit null.</summary>
    private static JsonElement Walk(string? dir) =>
        JsonDocument.Parse("{\"action\":\"walk\",\"data\":{\"dir\":" + JsonSerializer.Serialize(dir) + "}}").RootElement.Clone();

    private static JsonElement Input(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    /// <summary>A direction with at least <paramref name="steps"/> plain, straight steps ahead of <paramref name="from"/>: 300 ms each.</summary>
    private static string OpenWay(JsonElement from, int steps = 12)
    {
        var terrain = Pack.Value.GroundFor("ashen_ford")!.Terrain;
        foreach (var dir in Enum.GetValues<WalkDir>())
        {
            var at = new Tile(from[0].GetInt32(), from[1].GetInt32());
            var taken = 0;
            while (taken < steps && WorldWalk.Next(terrain, at, dir, ClassDef.DefaultJump, null) is { Slid: false, Cost: 1, Diagonal: false } move)
            {
                at = move.To;
                taken++;
            }
            if (taken == steps)
                return dir.ToString().ToLowerInvariant();
        }
        throw new InvalidOperationException("The party's start has no open way.");
    }

    /// <summary>The TV and <paramref name="phones"/> phones, c1 to cN, a campaign, and everyone out in the world with a class.</summary>
    private static async Task<RoomFixture> WorldAsync(int phones = 3)
    {
        var rules = Pack.Value;
        var f = new RoomFixture(rules: new TacticsCampaignRules(rules), game: new GameRegistration(() => new TacticsGame(rules)));
        await f.HostAsync();
        for (var i = 1; i <= phones; i++)
            await f.JoinAsync($"P{i}", $"c{i}");
        await f.Room.SelectGameAsync(Vip, TacticsGame.Id);
        await f.Room.CreateCampaignAsync(Vip, "Embers");
        for (var i = 2; i <= phones; i++)
            await f.Room.ClaimMemberAsync($"c{i}", new ClaimMemberRequest(null));
        await f.Room.StartGameAsync(Vip);
        for (var i = 1; i <= phones; i++)
            await SendAsync(f, $"c{i}", Input(new { action = "pickClass", data = new { @class = "fighter" } }));
        return f;
    }

    private static Task SendAsync(RoomFixture f, string conn, JsonElement payload) => f.Room.SendInputAsync(conn, f.LastViewId(conn), payload);

    private static async Task PassAsync(RoomFixture f, TimeSpan time)
    {
        f.Time.Advance(time);
        await f.Room.FlushAsync();
    }

    private static JsonElement Party(RoomFixture f) =>
        ((JsonElement)f.Sender.Of("host", ClientMethods.HostView).Last().Args[1]!).GetProperty("explore");

    private static int Steps(RoomFixture f) => Party(f).GetProperty("seq").GetInt32();

    private static bool Held(RoomFixture f) => Party(f).GetProperty("held").GetBoolean();

    private static int Sent(RoomFixture f, string conn, string method) => f.Sender.Of(conn, method).Count;

    private static JsonElement Phone(RoomFixture f, string conn) => (JsonElement)f.Sender.Of(conn, ClientMethods.ControllerView).Last().Args[0]!;

    private static bool Leads(RoomFixture f, string conn) => Phone(f, conn).GetProperty("data").GetProperty("leader").GetBoolean();

    /// <summary>The leader's phone. Profiles are random here, so it needn't be the VIP's.</summary>
    private static string Lead(RoomFixture f) => new[] { "c1", "c2", "c3" }.Single(c => Leads(f, c));

    private static string[] Others(RoomFixture f) => [.. new[] { "c1", "c2", "c3" }.Where(c => !Leads(f, c))];

    [Fact]
    public async Task A_walk_step_sends_the_host_a_view_and_no_phone_anything()
    {
        await using var f = await WorldAsync();
        var way = OpenWay(Party(f).GetProperty("at"));
        var lead = Lead(f);
        string[] phones = ["c1", "c2", "c3"];
        var views = phones.Select(c => Sent(f, c, ClientMethods.ControllerView)).ToArray();
        var hostViews = Sent(f, "host", ClientMethods.HostView);

        await SendAsync(f, lead, Walk(way));

        Assert.Equal(1, Steps(f));          // inside the press's own call
        Assert.Equal(hostViews + 1, Sent(f, "host", ClientMethods.HostView));

        hostViews = Sent(f, "host", ClientMethods.HostView);
        await SendAsync(f, lead, Walk(way));
        Assert.Equal(hostViews, Sent(f, "host", ClientMethods.HostView));      // a ping is no news

        for (var i = 0; i < 30; i++)
        {
            await PassAsync(f, Tick);
            if (i % 2 == 0)
                await SendAsync(f, lead, Walk(way));
        }

        Assert.True(Steps(f) >= 10);
        Assert.Equal(hostViews + Steps(f) - 1, Sent(f, "host", ClientMethods.HostView));   // one view a step, no more

        await SendAsync(f, lead, Walk(null));
        foreach (var conn in Others(f))
            await SendAsync(f, conn, Walk(null));

        Assert.False(Held(f));
        Assert.Equal(views, phones.Select(c => Sent(f, c, ClientMethods.ControllerView)));
    }

    [Fact]
    public async Task A_stale_view_id_stop_is_dropped_and_the_lease_ends_it()
    {
        await using var f = await WorldAsync();
        var way = OpenWay(Party(f).GetProperty("at"));
        var (lead, other) = (Lead(f), Others(f)[0]);
        var old = f.LastViewId(lead);
        await SendAsync(f, lead, Walk(way));

        await f.Room.ConnectionLostAsync(other);     // a new view for everyone, under the leader's thumb
        Assert.NotEqual(old, f.LastViewId(lead));
        Assert.True(Held(f));

        await f.Room.SendInputAsync(lead, old, Walk(null));
        Assert.True(Held(f));                       // dropped

        var passed = TimeSpan.Zero;
        while (Held(f) && passed < TimeSpan.FromSeconds(2))
        {
            await PassAsync(f, Tick);
            passed += Tick;
        }

        Assert.False(Held(f));
        Assert.InRange(passed, TimeSpan.FromMilliseconds(750), TimeSpan.FromMilliseconds(800));
        Assert.Equal(3, Steps(f));                  // at 0, 300 and 600 ms: the worst case the design allows

        await PassAsync(f, TimeSpan.FromSeconds(5));
        Assert.Equal(3, Steps(f));
    }

    [Fact]
    public async Task A_thumb_held_across_a_new_view_keeps_walking_when_the_pad_says_so_again()
    {
        await using var f = await WorldAsync();
        var way = OpenWay(Party(f).GetProperty("at"));
        var (lead, other) = (Lead(f), Others(f)[0]);
        await SendAsync(f, lead, Walk(way));
        await PassAsync(f, Tick);

        await f.Room.ConnectionLostAsync(other);
        await SendAsync(f, lead, Walk(way));         // the pad, on the new viewId

        for (var i = 0; i < 12; i++)
        {
            await PassAsync(f, Tick);
            if (i % 2 == 1)
                await SendAsync(f, lead, Walk(way));
        }

        Assert.True(Held(f));
        Assert.True(Steps(f) >= 4);
        Assert.True(Leads(f, lead));
    }

    [Fact]
    public async Task A_stale_ping_cant_hold_the_party_up()
    {
        await using var f = await WorldAsync();
        var way = OpenWay(Party(f).GetProperty("at"));
        var (lead, other) = (Lead(f), Others(f)[0]);
        var old = f.LastViewId(lead);
        await SendAsync(f, lead, Walk(way));
        await f.Room.ConnectionLostAsync(other);

        for (var i = 0; i < 20 && Held(f); i++)
        {
            await PassAsync(f, Tick);
            await f.Room.SendInputAsync(lead, old, Walk(way));     // a phone that missed the new view
        }

        Assert.False(Held(f));
    }

    [Fact]
    public async Task Pings_from_every_phone_change_nothing_for_anyone()
    {
        await using var f = await WorldAsync();
        var lead = Lead(f);
        var views = Sent(f, "c2", ClientMethods.ControllerView);
        var hostViews = Sent(f, "host", ClientMethods.HostView);

        for (var i = 0; i < 5; i++)
        {
            await PassAsync(f, TimeSpan.FromSeconds(3));
            foreach (var conn in new[] { "c1", "c2", "c3" })
                await SendAsync(f, conn, Walk(null));
        }

        Assert.Equal(views, Sent(f, "c2", ClientMethods.ControllerView));
        Assert.Equal(hostViews, Sent(f, "host", ClientMethods.HostView));
        Assert.True(Leads(f, lead));
    }

    [Fact]
    public async Task A_phone_that_went_quiet_wins_the_lead_back_with_its_idle_ping()
    {
        await using var f = await WorldAsync();
        var (lead, others) = (Lead(f), Others(f));
        var old = f.LastViewId(lead);

        for (var i = 0; i < 3; i++)
        {
            await PassAsync(f, TimeSpan.FromSeconds(3));
            foreach (var conn in others)                   // the others ping on; the leader has gone quiet
                await SendAsync(f, conn, Walk(null));
        }

        Assert.False(Leads(f, lead));
        var standIn = Lead(f);

        await f.Room.SendInputAsync(lead, old, Walk(null));     // the first ping, before its new view reached it: dropped
        Assert.True(Leads(f, standIn));

        await SendAsync(f, lead, Walk(null));                   // the pad resends on the new view
        Assert.True(Leads(f, standIn));                         // heard, but the stand-in keeps the pad 3 s (WalkTiming.TakebackAfter)
        await PassAsync(f, TimeSpan.FromSeconds(3));

        Assert.True(Leads(f, lead));
        Assert.False(Leads(f, standIn));
    }

    /// <summary>The real hub, so the real limiter: the room's clock is fake, and the limiter's is not.</summary>
    [Fact]
    public async Task Inputs_over_the_rate_limit_dont_break_a_walk()
    {
        await using var f = new ArcServerFactory(withTactics: true);
        await using var tv = await TestClient.ConnectAsync(f);
        await using var ann = await TestClient.ConnectAsync(f);
        await using var bob = await TestClient.ConnectAsync(f);
        var room = await tv.InvokeAsync<CreateRoomResult>("CreateRoom");
        await ann.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Ann"));
        await bob.TakeSeatAsync(new TakeSeatRequest(room.Code, NewName: "Bob"));
        await ann.InvokeAsync("SelectGame", TacticsGame.Id);
        await ann.InvokeAsync<ProfileKey>("CreateCampaign", "Embers");
        await bob.InvokeAsync<ProfileKey?>("ClaimMember", new ClaimMemberRequest(null));
        await ann.InvokeAsync("StartGame");

        static bool Screen(JsonElement view, string screen) =>
            view.TryGetProperty("screen", out var s) && s.GetString() == screen;
        foreach (var phone in new[] { ann, bob })
        {
            var pick = await phone.WaitForAsync(ClientMethods.ControllerView, v => Screen(v, TacticsGame.Screens.PickClass));
            await phone.InvokeAsync("SendInput", pick.GetProperty("viewId").GetString(), Input(new { action = "pickClass", data = new { @class = "fighter" } }));
        }

        // Profiles are random here, so either phone can be the first to lead.
        static bool Final(JsonElement v) => Screen(v, TacticsGame.Screens.Walk) && v.GetProperty("data").GetProperty("party").GetArrayLength() == 2;
        var annView = await ann.WaitForAsync(ClientMethods.ControllerView, Final);
        var bobView = await bob.WaitForAsync(ClientMethods.ControllerView, Final);
        var annLeads = annView.GetProperty("data").GetProperty("leader").GetBoolean();
        var lead = annLeads ? ann : bob;
        var viewId = (annLeads ? annView : bobView).GetProperty("viewId").GetString();
        var start = await tv.WaitForAsync(ClientMethods.HostView, v => v.GetProperty("phase").GetString() == "Exploration");
        var way = OpenWay(start.GetProperty("explore").GetProperty("at"));

        // Far past 20 a second, the stop last of all, as a phone whose bucket has run dry would lose it.
        string? refused = null;
        for (var burst = 0; burst < 5 && refused is null; burst++)
        {
            var sends = Enumerable.Repeat(Walk(way), 100).Append(Walk(null))
                .Select(payload => lead.InvokeAsync("SendInput", viewId, payload));
            await Task.WhenAll(sends);
            refused = await lead.TryInvokeMessageAsync("Peek", new PeekRequest(room.Code, null));
        }
        Assert.Equal("Slow down.", refused);

        var walked = await tv.WaitForAsync(ClientMethods.HostView, v => v.GetProperty("explore").GetProperty("seq").GetInt32() >= 1);
        f.Time.Advance(TimeSpan.FromSeconds(1));      // whichever stop got through or didn't, the lease is over by now
        var stopped = await tv.WaitForAsync(ClientMethods.HostView, v => !v.GetProperty("explore").GetProperty("held").GetBoolean()
            && v.GetProperty("explore").GetProperty("seq").GetInt32() >= 1);
        var steps = stopped.GetProperty("explore").GetProperty("seq").GetInt32();
        Assert.True(walked.GetProperty("explore").GetProperty("seq").GetInt32() <= steps);
        Assert.InRange(steps, 1, 5);                  // each burst's press is one step: the flood itself moved nobody

        // Once the bucket has refilled, the same phone on the same view just walks on.
        await Task.Delay(TimeSpan.FromSeconds(1.1), TestContext.Current.CancellationToken);
        await lead.InvokeAsync("SendInput", viewId, Walk(way));
        var again = await tv.WaitForAsync(ClientMethods.HostView, v => v.GetProperty("explore").GetProperty("seq").GetInt32() > steps);
        Assert.True(again.GetProperty("explore").GetProperty("held").GetBoolean());
    }
}
