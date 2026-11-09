using System.Collections.Immutable;
using System.Text.Json;
using ArcBox.Sdk;
using Tactics.Logic;
using static Tactics.Game.Tests.ExplorationTests;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>Tap to travel: the leader picks a tile on the phone's map, and the party walks there by the pad's own steps.</summary>
public sealed class TravelTests
{
    private const string Step = TacticsGame.Timers.Step;

    private static Terrain Ground => Rules.GroundFor("ashen_ford")!.Terrain;

    private static WorldRegion Region => Rules.RegionFor("ashen_ford")!;

    private static readonly Lazy<HashSet<Tile>> ReachedOnce = new(() => WorldWalk.Reach(Ground, Region.Def.Spawns.Start, ClassDef.DefaultJump));

    private static HashSet<Tile> Reached => ReachedOnce.Value;

    /// <summary>A session in the world at <paramref name="at"/>, everyone here, player 0 leading, with the roaming fights already beaten.</summary>
    private static Session At(Tile at)
    {
        var world = Saved(at) with { MobChapter = 1, MobsBeaten = [.. Rules.Mobs.Values.SelectMany(m => m).Select(m => m.Id)] };
        var s = new Session(document: DocumentAt(world));
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(s.Harness.Players[0].Id, s.Game.Leader!.Id);
        s.Send(0, "walk", new { dir = (string?)null });
        return s;
    }

    private static void Travel(Session s, Tile to, int player = 0) => s.Send(player, "travel", new { x = to.X, y = to.Y });

    private static ExploreView Party(Session s) => s.Host.Explore!;

    private static JsonElement Walk(Session s, int player = 0) => s.Screen(player)!.Data;

    private static bool Travelling(Session s, int player = 0) => Walk(s, player).TryGetProperty("travel", out _);

    private static int Ms(Tile from, WalkMove move)
    {
        var drop = Ground.HeightAt(from) - Ground.HeightAt(move.To);
        return Math.Max(1, (int)Math.Round(300 * move.Cost * (move.Diagonal ? Math.Sqrt(2) : 1) + 60 * Math.Max(0, drop - 2)));
    }

    /// <summary>The party's start and a reachable tile <paramref name="steps"/> or more steps of shortest walk from it, from the start spawn.</summary>
    private static (Tile From, Tile To, ImmutableArray<WalkDir> Taps) Trip(int steps = 8)
    {
        var from = Region.Def.Spawns.Start;
        foreach (var to in Reached.OrderBy(t => Math.Abs(t.X - from.X) + Math.Abs(t.Y - from.Y)).ThenBy(t => t))
            if (WorldWalk.Route(Ground, from, t => t == to, ClassDef.DefaultJump) is { } taps && taps.Length >= steps)
                return (from, to, taps);
        throw new InvalidOperationException("No trip that long.");
    }

    // ---- Walking there --------------------------------------------------------------------------------------------------

    [Fact]
    public void Travel_walks_the_route_one_step_at_a_time_at_the_walking_pace()
    {
        var (from, to, taps) = Trip();
        var s = At(from);

        Travel(s, to);

        Assert.True(Travelling(s));
        Assert.Equal(JsonSerializer.SerializeToElement(to, ArcJson.Options).GetRawText(), Walk(s).GetProperty("travel").GetProperty("to").GetRawText());
        var at = from;
        for (var i = 0; i < taps.Length; i++)
        {
            var move = WorldWalk.Next(Ground, at, taps[i], ClassDef.DefaultJump, null)!.Value;
            Assert.Equal(move.To, Party(s).At);             // each step as the pad would take it, at once for the first
            Assert.Equal(i + 1, Party(s).Seq);
            Assert.Equal(new WalkLeg(i + 1, move.To, Ms(at, move)), Party(s).Legs[^1]);
            Assert.Equal(i < taps.Length - 1, Party(s).Held);     // the last step stops as it starts, as a quick tap's does
            at = move.To;
            if (i == taps.Length - 1)
                break;
            s.Harness.Advance(TimeSpan.FromMilliseconds(Party(s).Legs[^1].Ms - 1));
            Assert.Equal(i + 1, Party(s).Seq);              // never ahead of the step under way
            s.Harness.Advance(TimeSpan.FromMilliseconds(1));
        }

        Assert.Equal(to, Party(s).At);
        Assert.False(Party(s).Held);                         // arrived: stopped, as a released pad stops
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
        Assert.False(Travelling(s));
        s.Harness.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(taps.Length, Party(s).Seq);
        Assert.Equal(to, TacticsDocument.Read(s.Lease.Document).World!.At);     // saved, as any stop is
    }

    [Fact]
    public void A_journey_asked_for_mid_step_starts_as_that_step_ends()
    {
        var (from, to, _) = Trip();
        var s = At(from);
        var dir = Enum.GetValues<WalkDir>().First(d => WorldWalk.Next(Ground, from, d, ClassDef.DefaultJump, null) is { Slid: false, Diagonal: false });
        s.Send(0, "walk", new { dir = dir.ToString().ToLowerInvariant() });
        s.Send(0, "walk", new { dir = (string?)null });
        Assert.Equal(1, Party(s).Seq);
        var due = Party(s).Legs[^1].Ms;

        s.Harness.Advance(TimeSpan.FromMilliseconds(100));
        Travel(s, to);
        Assert.Equal(1, Party(s).Seq);       // not a burst: the step under way ends first
        s.Harness.Advance(TimeSpan.FromMilliseconds(due - 100));
        Assert.Equal(2, Party(s).Seq);
        Assert.True(Travelling(s));
    }

    [Fact]
    public void A_press_on_the_pad_stops_the_journey_at_once_and_its_idle_pings_dont()
    {
        var (from, to, taps) = Trip();
        var s = At(from);
        Travel(s, to);
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        s.Send(0, "walk", new { dir = (string?)null });      // the phone's idle ping, with nobody on the pad
        s.Harness.Advance(TimeSpan.FromSeconds(1));
        var seq = Party(s).Seq;
        Assert.True(seq > 2);
        Assert.True(Travelling(s));

        var back = Opposite(taps[seq - 1]);
        s.Send(0, "walk", new { dir = back.ToString().ToLowerInvariant() });
        s.Send(0, "walk", new { dir = (string?)null });

        Assert.False(Travelling(s));
        s.Harness.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(seq + 1, Party(s).Seq);                  // the press's own step, and the journey no further
        Assert.NotEqual(to, Party(s).At);
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
    }

    private static WalkDir Opposite(WalkDir dir) => (WalkDir)(((int)dir + 4) % 8);

    [Fact]
    public void A_pick_that_isnt_ground_goes_to_the_nearest_tile_that_is()
    {
        var start = Region.Def.Spawns.Start;
        var wall = Reached.Where(t => Math.Abs(t.X - start.X) + Math.Abs(t.Y - start.Y) > 12).Order()
            .SelectMany(t => new[] { new Tile(t.X + 1, t.Y), new Tile(t.X, t.Y + 1) })
            .First(t => Ground.Map.InBounds(t) && !Reached.Contains(t));
        var s = At(start);

        Travel(s, wall);

        Assert.True(Travelling(s));
        var to = JsonSerializer.Deserialize<Tile>(Walk(s).GetProperty("travel").GetProperty("to").GetRawText(), ArcJson.Options);
        Assert.Contains(to, Reached);
        Assert.True(Math.Max(Math.Abs(to.X - wall.X), Math.Abs(to.Y - wall.Y)) <= 1);
    }

    [Fact]
    public void Nowhere_to_go_says_so_to_the_leader_and_the_party_stays()
    {
        var far = Enumerable.Range(0, Region.Def.Size[1]).SelectMany(y => Enumerable.Range(0, Region.Def.Size[0]).Select(x => new Tile(x, y)))
            .First(t => !Reached.Any(r => Math.Max(Math.Abs(r.X - t.X), Math.Abs(r.Y - t.Y)) <= TacticsGame.TravelSnap));
        var s = At(Region.Def.Spawns.Start);
        var follower = JsonSerializer.Serialize(s.Harness.PlayerView(1), ArcJson.Options);

        Travel(s, far);

        Assert.Equal(TacticsGame.NoWayThere, Walk(s).GetProperty("travelNote").GetString());
        Assert.False(Travelling(s));
        Assert.Equal(0, Party(s).Seq);
        Assert.DoesNotContain(Step, s.Harness.ActiveTimers);
        Assert.Equal(follower, JsonSerializer.Serialize(s.Harness.PlayerView(1), ArcJson.Options));    // the leader's word alone

        var dir = Enum.GetValues<WalkDir>().First(d => WorldWalk.Next(Ground, Region.Def.Spawns.Start, d, ClassDef.DefaultJump, null) is not null);
        s.Send(0, "walk", new { dir = dir.ToString().ToLowerInvariant() });
        Assert.False(Walk(s).TryGetProperty("travelNote", out _));     // walking on takes it down
    }

    [Fact]
    public void Only_the_leader_can_travel_and_only_to_a_tile_on_the_map()
    {
        var (from, to, _) = Trip();
        var s = At(from);

        Travel(s, to, player: 1);
        s.Send(0, "travel", new { x = -5, y = 3 });
        s.Send(0, "travel", new { x = "a", y = 3 });
        s.Send(0, "travel", new { });
        s.Harness.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(0, Party(s).Seq);
        Assert.False(Travelling(s));
        Assert.False(Walk(s, 1).TryGetProperty("travel", out _));
    }

    [Fact]
    public void The_leader_dropping_ends_the_journey()
    {
        var (from, to, _) = Trip();
        var s = At(from);
        Travel(s, to);
        s.Harness.Advance(TimeSpan.FromMilliseconds(700));
        s.Harness.SetConnected(0, false);
        var seq = Party(s).Seq;

        s.Harness.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(seq, Party(s).Seq);
        Assert.False(Party(s).Held);
    }

    [Fact]
    public void A_restart_finds_the_party_stopped_and_the_journey_gone()
    {
        var (from, to, _) = Trip(70);
        var s = At(from);
        Travel(s, to);
        for (var i = 0; i < 160; i++)
        {
            s.Harness.Advance(TimeSpan.FromMilliseconds(100));     // past the walk's save deadline, still on the way
            if (i % 30 == 0)
                s.Send(0, "walk", new { dir = (string?)null });       // the phone's idle pings, which keep its lead
        }
        Assert.True(Travelling(s));

        var again = new Session(document: s.Lease.Document);
        again.Harness.Resume(again.Lease);
        for (var p = 0; p < 4; p++)
            again.Harness.SetConnected(p, true);
        Assert.Equal(TacticsPhase.Exploration, again.Game.Phase);
        var at = again.Host.Explore!.At;
        again.Harness.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(at, again.Host.Explore!.At);
        Assert.False(again.Host.Explore!.Held);
        Assert.NotEqual(from, at);                          // it got somewhere before the restart, and stays there
    }

    // ---- What stops it ------------------------------------------------------------------------------------------------

    [Fact]
    public void Travelling_into_the_fight_on_offer_starts_it_as_walking_in_would()
    {
        var (entry, _) = StageBattleTests.Entry(back: 3);
        var s = StageBattleTests.Walking(StageBattleTests.Force, entry);
        var lead = StageBattleTests.Lead(s);
        var inside = new Tile(entry.X + 4, entry.Y);
        Assert.True(StageBattleTests.Stage.Holds(inside));

        s.Send(lead, "travel", new { x = inside.X, y = inside.Y });
        for (var i = 0; i < 40 && s.Game.Phase == TacticsPhase.Exploration; i++)
            s.Harness.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.False(StageBattleTests.Stage.Holds(s.Game.Document.World!.At));     // stopped a tile short, facing it, as a held walk does
    }

    [Fact]
    public void A_journey_past_the_fight_on_offer_goes_round_its_stamp()
    {
        var (entry, _) = StageBattleTests.Entry(back: 3);
        var stage = StageBattleTests.Stage;
        var s = StageBattleTests.Walking(StageBattleTests.Force, entry);
        var lead = StageBattleTests.Lead(s);
        var ground = StageBattleTests.Ground;
        var reach = WorldWalk.Reach(ground, entry, ClassDef.DefaultJump);
        // Somewhere the walk could go round the stamp to: a tile across from it, with a way there that never enters it.
        var past = reach.Where(t => !stage.Holds(t) && Math.Abs(t.Y - entry.Y) <= 2 && t.X > entry.X + 2).Order()
            .First(t => WorldWalk.Route(ground, entry, g => g == t, ClassDef.DefaultJump, stage.Holds) is not null);

        s.Send(lead, "travel", new { x = past.X, y = past.Y });
        for (var i = 0; i < 600 && s.Game.Phase == TacticsPhase.Exploration && s.Host.Explore!.At != past; i++)
            s.Harness.Advance(TimeSpan.FromMilliseconds(100));

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(past, s.Host.Explore!.At);
    }

    [Fact]
    public void Travel_is_ignored_with_the_quest_log_open_and_the_log_stops_a_journey()
    {
        var s = ConversationQuestTests.At("halden_varrow", "sq_grain_asked");
        var lead = ConversationQuestTests.Lead(s);
        var at = s.Game.Document.World!.At;
        var to = WorldWalk.Reach(WalkGroundOf(s), at, ClassDef.DefaultJump).Where(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y) is > 5 and < 9).Order().First();
        s.Send(lead, "questLog", new { action = "open" });

        s.Send(lead, "travel", new { x = to.X, y = to.Y });
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(at, s.Host.Explore!.At);

        s.Send(lead, "questLog", new { action = "close" });
        s.Send(lead, "travel", new { x = to.X, y = to.Y });
        Assert.True(s.Host.Explore!.Held);
        s.Send(lead, "questLog", new { action = "open" });
        Assert.False(s.Host.Explore!.Held);
        var stopped = s.Host.Explore!.At;
        s.Harness.Advance(TimeSpan.FromSeconds(3));
        Assert.Equal(stopped, s.Host.Explore!.At);
    }

    [Fact]
    public void Travel_is_ignored_in_a_scene()
    {
        var s = ConversationQuestTests.At("halden_varrow");
        var lead = ConversationQuestTests.Lead(s);
        var at = s.Game.Document.World!.At;
        ConversationQuestTests.Talk(s, "halden_varrow");
        Assert.Equal(TacticsPhase.Story, s.Game.Phase);
        Assert.Equal("offer", s.Game.Document.World!.Story!.Node);
        var to = WorldWalk.Reach(WalkGroundOf(s), at, ClassDef.DefaultJump).Where(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y) is > 5 and < 9).Order().First();

        s.Send(lead, "travel", new { x = to.X, y = to.Y });
        s.Harness.Advance(TimeSpan.FromSeconds(3));

        Assert.Equal(at, s.Host.Explore!.At);
        Assert.False(s.Host.Explore!.Held);
    }

    private static Terrain WalkGroundOf(Session s) => Rules.GroundFor(s.Host.Explore!.Region)!.Terrain;

    // ---- The map --------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_leaders_map_shows_the_region_the_party_and_the_landmarks_and_follows_the_party()
    {
        var (from, to, _) = Trip();
        var s = At(from);
        Assert.False(Walk(s).TryGetProperty("map", out _));

        s.Send(0, "mapOpen");
        var map = Walk(s).GetProperty("map");
        Assert.Equal("ashen_ford", map.GetProperty("region").GetString());
        Assert.Equal(from, JsonSerializer.Deserialize<Tile>(map.GetProperty("at").GetRawText(), ArcJson.Options));
        var pins = JsonSerializer.Deserialize<MapPin[]>(map.GetProperty("pins").GetRawText(), ArcJson.Options)!;
        Assert.NotEmpty(pins);
        Assert.Equal(pins.Length, pins.Select(p => p.At).Distinct().Count());       // one to a tile

        s.Send(1, "mapOpen");
        Assert.False(Walk(s, 1).TryGetProperty("map", out _));      // only the leader has one

        Travel(s, to);
        Assert.False(Walk(s).TryGetProperty("map", out _));         // picking a place closes it
        s.Send(0, "mapOpen");
        s.Harness.Advance(TimeSpan.FromMilliseconds(700));
        var moving = Walk(s).GetProperty("map");
        Assert.Equal(Party(s).At, JsonSerializer.Deserialize<Tile>(moving.GetProperty("at").GetRawText(), ArcJson.Options));
        Assert.Equal(to, JsonSerializer.Deserialize<Tile>(moving.GetProperty("to").GetRawText(), ArcJson.Options));

        s.Send(0, "mapClose");
        Assert.False(Walk(s).TryGetProperty("map", out _));
    }

    [Fact]
    public void The_map_pins_the_towns_shops_inn_and_temple_by_name()
    {
        var s = ShopTests.AtShop(0);
        var lead = StageBattleTests.Lead(s);
        s.Send(lead, "mapOpen");
        var pins = JsonSerializer.Deserialize<MapPin[]>(Walk(s, lead).GetProperty("map").GetProperty("pins").GetRawText(), ArcJson.Options)!;
        Assert.Contains(pins, p => p is { Kind: "inn", Label: "The Last Coal" });
        Assert.Contains(pins, p => p is { Kind: "temple", Label: "Chapel of the Hearth" });
        Assert.Contains(pins, p => p is { Kind: "shop", Label: "Wenna's Remedies" });
    }

    [Fact]
    public void The_map_never_pins_what_the_tv_doesnt_show()
    {
        string[] things =
        [
            """{ "id": "shrine", "kind": "shrine", "at": [6, 8], "text": ["You kneel."] }""",
            """{ "id": "well", "kind": "well", "at": [2, 4], "hidden": 30, "text": ["A hidden well."] }""",
            """{ "id": "seam", "kind": "secret_door", "at": [4, 3], "hidden": 30 }""",
        ];
        var s = Vault.At(new Tile(4, 2), Vault.Load(things));
        s.Send(0, "mapOpen");
        var pins = JsonSerializer.Deserialize<MapPin[]>(Walk(s).GetProperty("map").GetProperty("pins").GetRawText(), ArcJson.Options)!;
        Assert.Contains(pins, p => p.At == new Tile(6, 8));
        Assert.DoesNotContain(pins, p => p.At == new Tile(2, 4) || p.At == new Tile(4, 3));
    }
}
