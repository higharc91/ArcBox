using System.Text.Json;
using ArcBox.Sdk;
using ArcBox.Sdk.Protocol;
using ArcBox.Server.Games;
using ArcBox.Server.Tests.Support;
using Tactics.Game;
using Tactics.Logic;

namespace ArcBox.Server.Tests.Tactics;

/// <summary>
/// A fight in the world through a real room on a fake clock (overworld 5c), with no TV signal at all: every beat waits its fallback, the
/// opening one the walk-in's. The game's own tests drive it directly; this goes through the room's checks, its timers and its views.
/// </summary>
public sealed class StageRoomTests
{
    private const string Region = "ashen_ford";

    /// <summary>Four steps from River Crossing's stamp at (100, 52), screen east along the bank: the tile tools/tv-e2e.sh seeds.</summary>
    private static readonly Tile ByTheStamp = new(96, 61);

    private static readonly string[] Phones = ["c1", "c2", "c3", "c4"];

    private static readonly Lazy<string> BasePack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return Path.Combine(dir.FullName, "content", "tactics", "base");
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    private static readonly Lazy<RulesPack> Pack = new(() => RulesPack.Load([Path.Combine(BasePack.Value, "rules")]));

    private static readonly Lazy<LoreBook> Lore = new(() => LoreBook.Load(BasePack.Value));

    /// <summary>After the parley, by the ford: River Crossing is the beat on offer, as the e2e run's <c>Tactics:Seed</c> has it.</summary>
    private static readonly TacticsSeed Seed = new(["ch1_skarrow_spared"], ["ch1_mayors_request", "ch1_the_ford_choice"], Region, ByTheStamp);

    private static JsonElement Input(object o) => JsonSerializer.SerializeToElement(o, ArcJson.Options);

    private static JsonElement Walk(string? dir) =>
        JsonDocument.Parse("{\"action\":\"walk\",\"data\":{\"dir\":" + JsonSerializer.Serialize(dir) + "}}").RootElement.Clone();

    private static Task SendAsync(RoomFixture f, string conn, JsonElement payload) => f.Room.SendInputAsync(conn, f.LastViewId(conn), payload);

    private static async Task PassAsync(RoomFixture f, TimeSpan time)
    {
        f.Time.Advance(time);
        await f.Room.FlushAsync();
    }

    private static JsonElement Host(RoomFixture f) => (JsonElement)f.Sender.Of("host", ClientMethods.HostView).Last().Args[1]!;

    private static string Phase(RoomFixture f) => Host(f).GetProperty("phase").GetString()!;

    private static JsonElement Phone(RoomFixture f, string conn) => (JsonElement)f.Sender.Of(conn, ClientMethods.ControllerView).Last().Args[0]!;

    private static string? Screen(JsonElement view) => view.TryGetProperty("screen", out var s) ? s.GetString() : null;

    private static Tile TileOf(JsonElement at) => new(at[0].GetInt32(), at[1].GetInt32());

    [Fact]
    public async Task A_stage_battle_runs_with_no_host_signals()
    {
        var rules = Pack.Value;
        await using var f = new RoomFixture(rules: new TacticsCampaignRules(rules),
            game: new GameRegistration(() => new TacticsGame(rules, lore: Lore.Value, seed: Seed)));
        await f.HostAsync();
        for (var i = 0; i < Phones.Length; i++)
            await f.JoinAsync($"P{i + 1}", Phones[i]);
        await f.Room.SelectGameAsync("c1", TacticsGame.Id);
        await f.Room.CreateCampaignAsync("c1", "Embers");
        foreach (var conn in Phones.Skip(1))
            await f.Room.ClaimMemberAsync(conn, new ClaimMemberRequest(null));
        await f.Room.StartGameAsync("c1");
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];
        for (var i = 0; i < Phones.Length; i++)
            await SendAsync(f, Phones[i], Input(new { action = "pickClass", data = new { @class = classes[i] } }));

        Assert.Equal("Exploration", Phase(f));
        Assert.Equal(ByTheStamp, TileOf(Host(f).GetProperty("explore").GetProperty("at")));

        // Screen east along the bank, (+1, -1) a step, a tap a step, until a step into the stamp starts the fight: route.cs's way in.
        var lead = Phones.Single(c => Phone(f, c).GetProperty("data").GetProperty("leader").GetBoolean());
        for (var taps = 0; taps < 8 && Phase(f) == "Exploration"; taps++)
        {
            await SendAsync(f, lead, Walk("e"));
            await SendAsync(f, lead, Walk(null));
            await PassAsync(f, TimeSpan.FromMilliseconds(400));
        }

        // The beat's lead-in scene (overworld 5d) runs on its own timers, lines and any vote alike, with nobody answering and no TV.
        Assert.Equal("Story", Phase(f));
        for (var told = TimeSpan.Zero; Phase(f) == "Story" && told < TimeSpan.FromMinutes(15); told += TimeSpan.FromSeconds(1))
            await PassAsync(f, TimeSpan.FromSeconds(1));

        Assert.Equal("Battle", Phase(f));
        var battle = Host(f).GetProperty("battle");
        Assert.Equal("river_crossing", battle.GetProperty("encounter").GetString());
        var stage = battle.GetProperty("stage");
        Assert.Equal(Region, stage.GetProperty("region").GetString());
        Assert.Equal(new Tile(100, 52), TileOf(stage.GetProperty("origin")));
        Assert.Equal(4, stage.GetProperty("heroes").GetArrayLength());
        Assert.Equal(new Tile(99, 58), TileOf(Host(f).GetProperty("explore").GetProperty("at")));    // one tile short of the stamp

        // The phones end every turn and pass on every reaction; the TV never says a word, so the AI goes on its fallbacks alone.
        var beats = new SortedSet<int>();
        var waited = TimeSpan.Zero;
        while (Phase(f) == "Battle" && waited < TimeSpan.FromHours(2))
        {
            foreach (var conn in Phones)
            {
                var view = Phone(f, conn);
                if (Screen(view) == TacticsGame.Screens.Turn)
                    await SendAsync(f, conn, Input(new { action = "endTurn" }));
                else if (Screen(view) == TacticsGame.Screens.React)
                    await SendAsync(f, conn, Input(new { action = "react", data = new { choice = "pass" } }));
            }
            if (Host(f).TryGetProperty("battle", out var now) && now.ValueKind == JsonValueKind.Object)
                foreach (var beat in now.GetProperty("beats").EnumerateArray())
                    beats.Add(beat.GetProperty("seq").GetInt32());
            await PassAsync(f, TimeSpan.FromMilliseconds(500));
            waited += TimeSpan.FromMilliseconds(500);
        }

        Assert.Equal("Results", Phase(f));
        Assert.True(beats.Count > 20, $"Only {beats.Count} beats");
        Assert.Equal(Enumerable.Range(beats.Min, beats.Count), beats);     // every beat went out: none was skipped waiting on the TV

        // Nobody fought back, so the party fell: at the results, the VIP goes on, and the party wakes at the recovery spawn, walking.
        var vip = Phone(f, "c1");
        Assert.Equal(TacticsGame.Screens.Results, Screen(vip));
        Assert.Equal("Lost", vip.GetProperty("data").GetProperty("outcome").GetString());
        await SendAsync(f, "c1", Input(new { action = "next" }));

        Assert.Equal("Exploration", Phase(f));
        var explore = Host(f).GetProperty("explore");
        Assert.Equal("woke", explore.GetProperty("arrived").GetString());
        Assert.Equal(rules.RegionFor(Region)!.Def.Spawns.Recovery, TileOf(explore.GetProperty("at")));
        Assert.All(Phones, c => Assert.Equal(TacticsGame.Screens.Walk, Screen(Phone(f, c))));
    }
}
