using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games;
using ArcBox.Server.Tests.Support;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Tests.Tactics;

/// <summary>
/// A scene's votes through a real room on a fake clock (overworld 5d, Task 7): the room's viewId check in front of the game, which drops a
/// tap meant for a view that has changed, and lets every tap on a view that hasn't through.
/// </summary>
public sealed class StoryRoomTests
{
    private const string Vip = "c1";

    private static string ContentBase()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return Path.Combine(dir.FullName, "content", "tactics", "base");
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    }

    private static readonly Lazy<RulesPack> Pack = new(() => RulesPack.Load([Path.Combine(ContentBase(), "rules")]));
    private static readonly Lazy<LoreBook> Lore = new(() => LoreBook.Load(ContentBase()));

    private static readonly string[] Phones = ["c1", "c2", "c3"];

    private static JsonElement Input(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    /// <summary>A tile in reach of the ford choice's spot, with the mayor's beat done: the ford choice is on offer.</summary>
    private static Tile NearTheFord()
    {
        var region = Pack.Value.RegionFor("ashen_ford")!;
        var ground = Pack.Value.GroundFor("ashen_ford")!.Terrain;
        var spot = region.Def.Beats.Single(b => b.Beat == "ch1_the_ford_choice").At;
        var reached = WorldWalk.Reach(ground, region.Def.Spawns.Start, ClassDef.DefaultJump);
        return reached.Where(t => t != spot && Math.Abs(t.X - spot.X) <= 3 && Math.Abs(t.Y - spot.Y) <= 3 && StoryRules.InReach(ground, t, spot))
            .OrderBy(t => Math.Abs(t.X - spot.X) + Math.Abs(t.Y - spot.Y)).ThenBy(t => t).First();
    }

    /// <summary>The TV, three phones, a campaign beside the ford, everyone with a class, and the ford choice begun.</summary>
    private static async Task<RoomFixture> AtTheFordAsync()
    {
        var rules = Pack.Value;
        var seed = new TacticsSeed([], ["ch1_mayors_request"], At: NearTheFord());
        var f = new RoomFixture(rules: new TacticsCampaignRules(rules), game: new GameRegistration(() => new TacticsGame(rules, lore: Lore.Value, seed: seed)));
        await f.HostAsync();
        for (var i = 1; i <= Phones.Length; i++)
            await f.JoinAsync($"P{i}", $"c{i}");
        await f.Room.SelectGameAsync(Vip, TacticsGame.Id);
        await f.Room.CreateCampaignAsync(Vip, "Embers");
        for (var i = 2; i <= Phones.Length; i++)
            await f.Room.ClaimMemberAsync($"c{i}", new ClaimMemberRequest(null));
        await f.Room.StartGameAsync(Vip);
        foreach (var conn in Phones)
            await SendAsync(f, conn, Input(new { action = "pickClass", data = new { @class = "fighter" } }));
        await SendAsync(f, Lead(f), Input(new { action = "interact" }));
        Assert.Equal("Story", Host(f).GetProperty("phase").GetString());
        return f;
    }

    private static Task SendAsync(RoomFixture f, string conn, JsonElement payload) => f.Room.SendInputAsync(conn, f.LastViewId(conn), payload);

    private static JsonElement Host(RoomFixture f) => (JsonElement)f.Sender.Of("host", ClientMethods.HostView).Last().Args[1]!;

    private static JsonElement Phone(RoomFixture f, string conn) => (JsonElement)f.Sender.Of(conn, ClientMethods.ControllerView).Last().Args[0]!;

    private static string Screen(RoomFixture f, string conn) => Phone(f, conn).GetProperty("screen").GetString()!;

    /// <summary>The leader's phone: the one whose line has Skip, or whose question it is to choose.</summary>
    private static string Lead(RoomFixture f)
    {
        foreach (var conn in Phones)
        {
            var data = Phone(f, conn).GetProperty("data");
            if (data.TryGetProperty("leader", out var leader) && leader.GetBoolean()
                || data.TryGetProperty("canSkip", out var skip) && skip.GetBoolean()
                || Screen(f, conn) == "vote" && !data.GetProperty("party").GetBoolean() && data.GetProperty("canChoose").GetBoolean())
                return conn;
        }
        throw new InvalidOperationException("Nobody leads.");
    }

    private static string? State(RoomFixture f) => Host(f).GetProperty("story") is { ValueKind: JsonValueKind.Object } story ? story.GetProperty("state").GetString() : null;

    private static string? Node(RoomFixture f) => Host(f).GetProperty("story") is { ValueKind: JsonValueKind.Object } story ? story.GetProperty("line").GetProperty("id").GetString() : null;

    /// <summary>Lets time pass a tenth of a second at a time until <paramref name="done"/> holds.</summary>
    private static async Task UntilAsync(RoomFixture f, Func<bool> done)
    {
        for (var i = 0; i < 3_000 && !done(); i++)
        {
            f.Time.Advance(TimeSpan.FromMilliseconds(100));
            await f.Room.FlushAsync();
        }
        Assert.True(done());
    }

    /// <summary>On to the party vote: the leader asks about the cases, and the lines play until the vote opens.</summary>
    private static async Task ToTheVoteAsync(RoomFixture f, Action? beforeTheVote = null)
    {
        await UntilAsync(f, () => State(f) == "asking");
        await SendAsync(f, Lead(f), Input(new { action = "vote", data = new { option = "cases" } }));
        await UntilAsync(f, () => Node(f) is { } line && line.StartsWith("ch1_ford_cases_", StringComparison.Ordinal));
        // The line before the vote's: what a phone shows just before the question opens.
        await UntilAsync(f, () => Node(f) is { } line && line.StartsWith("ch1_ford_vote_", StringComparison.Ordinal) || State(f) == "asking");
        beforeTheVote?.Invoke();
        await UntilAsync(f, () => State(f) == "asking");
    }

    private static int Voted(RoomFixture f) => Host(f).GetProperty("story").GetProperty("ask").GetProperty("voted").GetInt32();

    [Fact]
    public async Task A_stale_vote_after_a_new_line_is_dropped()
    {
        await using var f = await AtTheFordAsync();
        string? stale = null;
        await ToTheVoteAsync(f, () => stale = f.LastViewId("c2"));
        Assert.Equal("vote", Screen(f, "c2"));
        Assert.NotEqual(stale, f.LastViewId("c2"));

        await f.Room.SendInputAsync("c2", stale!, Input(new { action = "vote", data = new { option = "parley" } }));     // a tap on the line before
        Assert.Equal(0, Voted(f));

        await SendAsync(f, "c2", Input(new { action = "vote", data = new { option = "parley" } }));
        Assert.Equal(1, Voted(f));
    }

    [Fact]
    public async Task A_vote_storm_through_a_real_room_counts_everyone()
    {
        await using var f = await AtTheFordAsync();
        await ToTheVoteAsync(f);
        var ids = Phones.ToDictionary(c => c, f.LastViewId);

        // All three in the same tick, each on the view it has: none of them changes anyone else's.
        foreach (var conn in Phones)
            await f.Room.SendInputAsync(conn, ids[conn], Input(new { action = "vote", data = new { option = conn == "c1" ? "force" : "parley" } }));

        Assert.Equal(3, Voted(f));
        var options = Host(f).GetProperty("story").GetProperty("ask").GetProperty("options").EnumerateArray().ToDictionary(o => o.GetProperty("id").GetString()!);
        Assert.Equal(1, options["force"].GetProperty("voters").GetArrayLength());
        Assert.Equal(2, options["parley"].GetProperty("voters").GetArrayLength());
    }

    private static JsonElement Cast(JsonElement host) => host.GetProperty("explore").GetProperty("cast");

    /// <summary>
    /// The scene's cast (the user's answer of 2026-11-01, item 21) through a real room: the TV is told who stands where, and a TV that comes
    /// back mid-scene is told the same, tile for tile, in its first view.
    /// </summary>
    [Fact]
    public async Task A_TV_that_comes_back_mid_scene_sees_the_cast()
    {
        await using var f = await AtTheFordAsync();
        var cast = Cast(Host(f));
        Assert.Equal(["halden_varrow", "dagna_holt", "bree_tallow"], cast.EnumerateArray().Select(c => c.GetProperty("id").GetString()));

        await f.Room.BindHostAsync("host2", "host-token");
        var back = (JsonElement)f.Sender.Of("host2", ClientMethods.HostView).Last().Args[1]!;
        Assert.Equal("Story", back.GetProperty("phase").GetString());
        Assert.Equal(cast.GetRawText(), Cast(back).GetRawText());
    }
}
