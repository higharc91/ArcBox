using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;

namespace Tactics.Game.Tests;

/// <summary>The Exploration phase: where a session goes, who leads, and what's kept. Stepping is <see cref="WalkingTests"/>.</summary>
public sealed class ExplorationTests
{
    private const string Ford = "ashen_ford";

    private static Terrain Ground => Rules.GroundFor(Ford)!.Terrain;

    private static Tile Start => Rules.RegionFor(Ford)!.Def.Spawns.Start;

    /// <summary>A walkable tile a few steps from the start: somewhere else the party can really be.</summary>
    internal static Tile NearStart(int steps = 3)
    {
        var at = Start;
        for (var i = 0; i < steps; i++)
            at = WorldWalk.Steps(Ground, at, ClassDef.DefaultJump).First(t => t != Start);
        return at;
    }

    /// <summary>A document with all four members, and <paramref name="world"/> as the party's place.</summary>
    internal static JsonElement DocumentAt(WorldSave? world, int level = 1)
    {
        var s = new Session();
        s.ToWalking();
        return (TacticsDocument.Read(s.Lease.Document) with { World = world, PartyLevel = level }).ToJson();
    }

    internal static WorldSave Saved(Tile at, string? region = Ford, string? lastLeader = null)
        => new(region!, at, new Tile(at.X + 1, at.Y), lastLeader, ["a_flag"], ["a_beat"]);

    /// <summary>The rules without their region: a pack with no world.</summary>
    internal static readonly Lazy<RulesPack> NoRegion = new(() => EditedRules(rules =>
    {
        var manifest = Path.Combine(rules, "manifest.json");
        var node = JsonNode.Parse(File.ReadAllText(manifest))!;
        var files = node["files"]!.AsArray();
        // A region's things go with it: a things file for a region that isn't loaded is an error.
        foreach (var file in files.Where(f => f!.GetValue<string>() is var name
                     && (name.StartsWith("world/", StringComparison.Ordinal) || name.StartsWith("interactables/", StringComparison.Ordinal))).ToArray())
            files.Remove(file);
        File.WriteAllText(manifest, node.ToJsonString());
        Directory.Delete(Path.Combine(rules, "world"), recursive: true);
        if (Directory.Exists(Path.Combine(rules, "interactables")))
            Directory.Delete(Path.Combine(rules, "interactables"), recursive: true);
    }));

    /// <summary>Loads a copy of the rules after <paramref name="change"/> has edited it.</summary>
    internal static RulesPack EditedRules(Action<string> change)
    {
        var copy = Directory.CreateTempSubdirectory("tactics-edited-rules-test-");
        try
        {
            var from = Path.Combine(ContentRules(), "rules");
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
            {
                var to = Path.Combine(copy.FullName, Path.GetRelativePath(from, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(file, to);
            }
            change(copy.FullName);
            return RulesPack.Load([copy.FullName], ArtManifests.Load(ContentRules()));
        }
        finally
        {
            copy.Delete(recursive: true);
        }
    }

    /// <summary>Ashen Ford with a wall painted around <paramref name="tile"/>, in ground open enough that nothing else is cut off.</summary>
    private static RulesPack WalledIn(Tile tile)
    {
        (Tile, int, byte)[] walls = [.. new[] { new Tile(tile.X + 1, tile.Y), new Tile(tile.X - 1, tile.Y), new Tile(tile.X, tile.Y + 1), new Tile(tile.X, tile.Y - 1) }
            .Select(wall => (wall, 2, (byte)(Rules.RegionFor(Ford)!.TileAt(wall)!.Flags | TileFlags.Impassable)))];
        return Painted(walls);
    }

    /// <summary>Ashen Ford with the given bytes of its tiles rewritten: byte 0 of a tile is its height, 2 its flags.</summary>
    internal static RulesPack Painted(params (Tile At, int Byte, byte Value)[] paint) => EditedRules(rules =>
    {
        var path = Path.Combine(rules, "world", "ashen_ford.json");
        var region = JsonNode.Parse(File.ReadAllText(path))!;
        foreach (var (at, offset, value) in paint)
        {
            var (cx, cy) = (at.X / WorldRegion.ChunkSize, at.Y / WorldRegion.ChunkSize);
            var chunk = region["chunks"]!.AsArray().Single(c => c!["x"]!.GetValue<int>() == cx && c["y"]!.GetValue<int>() == cy)!;
            var tiles = Convert.FromBase64String(chunk["tiles"]!.GetValue<string>());
            tiles[(at.Y % WorldRegion.ChunkSize * WorldRegion.ChunkSize + at.X % WorldRegion.ChunkSize) * 3 + offset] = value;
            chunk["tiles"] = Convert.ToBase64String(tiles);
        }
        File.WriteAllText(path, region.ToJsonString());
    });

    /// <summary>A tile in the middle of a five-tile square of level ground the party can reach, away from anything placed on the map.</summary>
    internal static Tile OpenGround()
    {
        var ground = Ground;
        var def = Rules.RegionFor(Ford)!.Def;
        var reached = WorldWalk.Reach(ground, Start, ClassDef.DefaultJump);
        var marked = def.Beats.Select(b => b.At).Concat([def.Spawns.Start, def.Spawns.Recovery]).ToArray();
        return reached.Order().First(t =>
        {
            var square = from dx in Enumerable.Range(-2, 5) from dy in Enumerable.Range(-2, 5) select new Tile(t.X + dx, t.Y + dy);
            return square.All(s => reached.Contains(s) && ground.HeightAt(s) == ground.HeightAt(t))
                && marked.All(m => Math.Abs(m.X - t.X) + Math.Abs(m.Y - t.Y) > 2)
                && !def.Stamps.Any(s => t.X >= s.At.X - 2 && t.Y >= s.At.Y - 2 && t.X < s.At.X + 16 && t.Y < s.At.Y + 14);
        });
    }

    internal static string ContentRules()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return Path.Combine(dir.FullName, "content", "tactics", "base");
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    }

    [Fact]
    public void Exploration_came_after_the_battle_phases_so_old_values_keep_their_order()
    {
        Assert.Equal(TacticsPhase.Exploration, Enum.GetValues<TacticsPhase>()[5]);     // and Story after it (StoryTests)
        Assert.Equal(5, (int)TacticsPhase.Exploration);
    }

    [Fact]
    public void After_muster_the_party_is_at_the_start_spawn()
    {
        var s = new Session();
        s.ToWalking();

        Assert.Equal(Start, s.Host.Explore!.At);
        Assert.Equal(Ford, s.Host.Explore.Region);
        Assert.Equal("The Ashen Ford", s.Host.Explore.Name);
        Assert.False(s.Host.Explore.Held);
        Assert.Empty(s.Host.Explore.Legs);
        Assert.Equal(Start, TacticsDocument.Read(s.Lease.Document).World!.At);
    }

    // ---- The battle list behind the Development switch (the user's decision) ------------------------------------------

    private static bool CanBattle(Session s, int seat) => s.Screen(seat)!.Data.GetProperty("canBattle").GetBoolean();

    [Fact]
    public void With_the_switch_off_the_world_offers_no_battle_list()
    {
        var s = new Session(game: new TacticsGame(Rules));
        s.ToWalking();

        Assert.False(CanBattle(s, 0));
        s.Send(0, "battles");

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Screen(0)!.Header);       // no toast for a button that isn't there
    }

    [Fact]
    public void With_the_switch_on_the_vip_can_choose_a_battle_from_the_world()
    {
        var s = new Session(game: new TacticsGame(Rules, battleList: true));
        s.ToWalking();

        Assert.True(CanBattle(s, 0));
        Assert.False(CanBattle(s, 1));          // the VIP's alone, as before
        s.Send(1, "battles");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        s.Send(0, "battles");
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
    }

    /// <summary>Results' "Carry on" goes back to walking whether or not the list is offered: it's how every fight ends.</summary>
    [Fact]
    public void Results_carry_on_to_the_world_with_the_switch_off()
    {
        var s = StageBattleTests.Fighting(StageBattleTests.Force);
        for (var i = 0; i < 4; i++)
            s.Harness.SetConnected(i, false);
        s.PlayOut();
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        s.Harness.SetConnected(0, true);

        s.Send(0, "next");

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.False(CanBattle(s, 0));
    }

    /// <summary>A pack with no region has nowhere else to fight, so its battle list is there whatever the switch says.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_pack_with_no_region_keeps_its_battle_list_either_way(bool battleList)
    {
        var s = new Session(game: new TacticsGame(NoRegion.Value, battleList: battleList));
        s.Harness.StartCampaign(s.Lease);
        for (var i = 0; i < 4; i++)
            s.Send(i, "pickClass", new { @class = "fighter" });

        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        Assert.Equal(TacticsGame.Screens.PickEncounter, s.Screen(0)!.Screen);
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Null(s.Host.Battle!.Stage);
        s.PlayOut();
        s.Send(0, "next");
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
    }

    [Fact]
    public void A_pack_with_no_region_goes_to_the_battle_list()
    {
        var s = new Session(game: new TacticsGame(NoRegion.Value));
        s.Harness.StartCampaign(s.Lease);
        for (var i = 0; i < 4; i++)
            s.Send(i, "pickClass", new { @class = "fighter" });

        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        Assert.Null(s.Host.Explore);
        Assert.Null(TacticsDocument.Read(s.Lease.Document).World);
        Assert.Empty(s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_pack_with_no_region_takes_no_walking_input_and_keeps_a_world_it_cant_use()
    {
        var world = Saved(new Tile(1, 2));
        var s = new Session(document: DocumentAt(world), game: new TacticsGame(NoRegion.Value));
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);

        s.Send(0, "backToWalk");
        s.Send(0, "walk", new { dir = "e" });
        s.Send(0, "endSession");

        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        Assert.False(s.Harness.IsOver);
        Assert.Null(s.Host.Notice);
        Assert.Empty(s.Harness.ActiveTimers);
        Assert.Equal(new Tile(1, 2), TacticsDocument.Read(s.Lease.Document).World!.At);
    }

    [Fact]
    public void The_party_resumes_where_it_stopped()
    {
        var there = NearStart();
        var s = new Session(document: DocumentAt(Saved(there)));
        s.Harness.Resume(s.Lease);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(there, s.Host.Explore!.At);
        Assert.Equal(new Tile(there.X + 1, there.Y), s.Host.Explore.Facing);
        Assert.Null(s.Host.Notice);

        var again = new Session(document: DocumentAt(Saved(there)));
        again.Harness.StartCampaign(again.Lease);
        Assert.Equal(there, again.Host.Explore!.At);
    }

    public static TheoryData<int, int> TilesNobodyCanStandOn()
    {
        var ground = Ground;
        var data = new TheoryData<int, int> { { -1, 5 }, { 5000, 5 } };
        var blocked = Enumerable.Range(0, ground.Map.Width * ground.Map.Height)
            .Select(i => new Tile(i % ground.Map.Width, i / ground.Map.Width)).First(t => !ground.Walkable(t));
        data.Add(blocked.X, blocked.Y);
        return data;
    }

    [Theory]
    [MemberData(nameof(TilesNobodyCanStandOn))]
    public void A_saved_tile_that_cant_be_stood_on_goes_to_the_start_with_a_notice(int x, int y)
    {
        var s = new Session(document: DocumentAt(Saved(new Tile(x, y))));
        s.Harness.Resume(s.Lease);

        Assert.Equal(Start, s.Host.Explore!.At);
        Assert.NotNull(s.Host.Notice);
        var saved = TacticsDocument.Read(s.Lease.Document).World!;
        Assert.Equal(Start, saved.At);                 // once: the next restart has nothing to say
        Assert.Equal(["a_flag"], saved.Flags);         // the place is lost, the story isn't
        Assert.Equal(["a_beat"], saved.Beats);

        var next = new Session(document: s.Lease.Document);
        next.Harness.Resume(next.Lease);
        Assert.Null(next.Host.Notice);
    }

    [Fact]
    public void A_walkable_tile_a_repaint_cut_off_from_the_start_goes_to_the_start_too()
    {
        var island = OpenGround();
        var repainted = new TacticsGame(WalledIn(island));

        var s = new Session(document: DocumentAt(Saved(island)), game: repainted);
        s.Harness.Resume(s.Lease);

        Assert.Equal(Start, s.Host.Explore!.At);
        Assert.NotNull(s.Host.Notice);
    }

    [Fact]
    public void A_document_whose_world_names_no_region_goes_to_the_start()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart(), region: null)));
        s.Harness.StartCampaign(s.Lease);

        Assert.Equal(Ford, s.Host.Explore!.Region);
        Assert.Equal(Start, s.Host.Explore.At);
        Assert.NotNull(s.Host.Notice);
        var saved = TacticsDocument.Read(s.Lease.Document).World!;
        Assert.Equal(Ford, saved.Region);
        Assert.Equal(["a_flag"], saved.Flags);
    }

    [Fact]
    public void A_document_with_a_bare_world_doesnt_crash()
    {
        var node = JsonNode.Parse(DocumentAt(null).GetRawText())!;
        node["world"] = new JsonObject { ["region"] = null };

        var s = new Session(document: JsonDocument.Parse(node.ToJsonString()).RootElement);
        s.Harness.Resume(s.Lease);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(Start, s.Host.Explore!.At);
    }

    [Fact]
    public void A_region_the_pack_no_longer_has_goes_to_the_start_with_a_notice()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart(), region: "a_lost_land")));
        s.Harness.StartCampaign(s.Lease);

        Assert.Equal(Ford, s.Host.Explore!.Region);
        Assert.Equal(Start, s.Host.Explore.At);
        Assert.NotNull(s.Host.Notice);
    }

    [Fact]
    public void An_old_document_with_no_world_starts_at_the_spawn_without_a_notice()
    {
        var s = new Session(document: DocumentAt(null));
        s.Harness.StartCampaign(s.Lease);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(Start, s.Host.Explore!.At);
        Assert.Null(s.Host.Notice);
    }

    [Fact]
    public void The_lead_rotates_each_session_by_profile_not_seat()
    {
        var first = new Session();
        first.ToWalking();
        Assert.Equal("pr_member1", first.Game.Leader!.Profile!.Value.Value);

        var document = first.Lease.Document;
        foreach (var (reseated, expected) in new[] { (true, "pr_member2"), (false, "pr_member3"), (true, "pr_member4"), (false, "pr_member1") })
        {
            var night = new Session(document: document, reseated: reseated);
            night.Harness.StartCampaign(night.Lease);

            Assert.Equal(expected, night.Game.Leader!.Profile!.Value.Value);
            Assert.Equal(expected, TacticsDocument.Read(night.Lease.Document).World!.LastLeader);   // saved at once
            document = night.Lease.Document;
        }
    }

    /// <summary>The room's order of things: every seat is online for Start, and the absent ones drop just after it returns.</summary>
    private static (string? Leader, string? Saved, JsonElement Document) Night(JsonElement document, int absentSeat)
    {
        var s = new Session(document: document);
        s.Harness.StartCampaign(s.Lease);
        s.Harness.SetConnected(absentSeat, false);
        return (s.Game.Leader?.Profile?.Value, TacticsDocument.Read(s.Lease.Document).World!.LastLeader, s.Lease.Document);
    }

    [Fact]
    public void Someone_absent_every_night_doesnt_stop_the_lead_rotating()
    {
        var document = DocumentAt(Saved(NearStart(), lastLeader: "pr_member1"));

        // Member 2, in seat 1, never comes. Each night's leader has to be new, and has to be who's saved.
        foreach (var expected in new[] { "pr_member3", "pr_member4", "pr_member1", "pr_member3" })
        {
            var (leader, saved, next) = Night(document, absentSeat: 1);

            Assert.Equal(expected, leader);
            Assert.Equal(expected, saved);
            document = next;
        }
    }

    [Fact]
    public void An_absent_member_arriving_late_takes_no_crown()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart(), lastLeader: "pr_member1")));
        s.Harness.StartCampaign(s.Lease);
        s.Harness.SetConnected(1, false);
        Assert.Equal("pr_member3", s.Game.Leader!.Profile!.Value.Value);

        s.Harness.SetConnected(1, true);

        Assert.Equal("pr_member3", s.Game.Leader!.Profile!.Value.Value);
    }

    [Fact]
    public void Everyone_absent_at_start_leaves_the_rotation_where_it_was()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart(), lastLeader: "pr_member1")));
        s.Harness.StartCampaign(s.Lease);

        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);

        Assert.Null(s.Game.Leader);
        Assert.Equal("pr_member1", TacticsDocument.Read(s.Lease.Document).World!.LastLeader);
    }

    [Fact]
    public void A_session_leader_who_has_been_heard_from_keeps_their_session_through_a_drop()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart(), lastLeader: "pr_member1")));
        s.Harness.StartCampaign(s.Lease);
        s.Send(1, "walk", new { dir = (string?)null });

        s.Harness.SetConnected(1, false);
        Assert.Equal("pr_member3", s.Game.Leader!.Profile!.Value.Value);
        s.Harness.SetConnected(1, true);
        s.Harness.Advance(TimeSpan.FromSeconds(3));       // the stand-in keeps the pad a moment: see WalkTiming.TakebackAfter

        Assert.Equal("pr_member2", s.Game.Leader!.Profile!.Value.Value);
        Assert.Equal("pr_member2", TacticsDocument.Read(s.Lease.Document).World!.LastLeader);
    }

    [Fact]
    public void A_character_filled_in_for_the_only_one_here_gives_the_party_a_leader()
    {
        var doc = TacticsDocument.Read(DocumentAt(Saved(NearStart())));
        var s = new Session(document: (doc with { Members = Ordinal.Map<MemberRecord>() }).ToJson());
        s.Harness.Resume(s.Lease);
        s.Harness.SetConnected(0, true);
        Assert.Null(s.Game.Leader);

        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });

        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        Assert.Equal("Player1", s.Host.Explore!.Leader);
    }

    [Fact]
    public void Resuming_doesnt_rotate_the_lead()
    {
        var s = new Session();
        s.ToWalking();

        var resumed = new Session(document: s.Lease.Document);
        resumed.Harness.Resume(resumed.Lease);
        resumed.Harness.SetConnected(0, true);

        Assert.Equal("pr_member1", resumed.Game.Leader!.Profile!.Value.Value);
        Assert.Equal("pr_member1", TacticsDocument.Read(resumed.Lease.Document).World!.LastLeader);
    }

    [Fact]
    public void The_first_lead_waits_for_a_character()
    {
        var s = new Session();
        s.Harness.StartCampaign(s.Lease);
        Assert.Null(s.Game.Leader);

        s.Send(2, "pickClass", new { @class = "rogue" });
        for (var i = 0; i < 4; i++)
            if (i != 2)
                s.Send(i, "pickClass", new { @class = "fighter" });

        Assert.Equal("pr_member1", s.Game.Leader!.Profile!.Value.Value);
    }

    [Fact]
    public void A_member_with_no_character_cant_lead_and_a_member_gone_from_the_campaign_doesnt_break_the_order()
    {
        var doc = TacticsDocument.Read(DocumentAt(Saved(NearStart(), lastLeader: "pr_member15")));   // sorts between 1 and 2, and isn't a member
        doc = doc with { Members = doc.Members.Remove("pr_member2") };

        var s = new Session(document: doc.ToJson());
        s.Harness.StartCampaign(s.Lease);

        Assert.Equal(TacticsPhase.Muster, s.Game.Phase);   // the new night's muster still asks member 2 to choose
        Assert.Equal("pr_member3", s.Game.Leader!.Profile!.Value.Value);
    }

    [Fact]
    public void Only_the_vip_opens_the_battle_list()
    {
        var s = new Session();
        s.ToWalking();

        s.Send(1, "battles");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);

        s.Send(0, "battles");
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);
        Assert.Equal(TacticsGame.Screens.PickEncounter, s.Screen(0)?.Screen);
        Assert.IsType<WaitView>(s.Harness.PlayerView(1));
    }

    [Fact]
    public void Back_to_walking_is_the_vips_and_lands_on_the_same_tile()
    {
        var s = new Session();
        s.ToWalking();
        s.Send(0, "battles");

        s.Send(1, "backToWalk");
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);

        s.Send(0, "backToWalk");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(Start, s.Host.Explore!.At);
        Assert.Equal(TacticsGame.Screens.Walk, s.Screen(0)?.Screen);
    }

    [Fact]
    public void A_doubled_tap_on_the_way_in_or_out_does_nothing_more()
    {
        var s = new Session();
        s.ToWalking();

        s.Send(0, "battles");
        s.Send(0, "battles");
        Assert.Equal(TacticsPhase.EncounterPick, s.Game.Phase);

        s.Send(0, "backToWalk");
        s.Send(0, "backToWalk");
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
    }

    [Fact]
    public void Next_after_results_goes_back_to_walking_on_the_same_tile()
    {
        // A fight on a map the region doesn't stamp is on a plain board, so the party is where it was. (One on a stamped map stands
        // the party where the fight left it: StageEndTests.)
        var unstamped = Rules.Encounters.Values.First(e => Rules.RegionFor(Ford)!.StampFor(e.Map) is null && e.Party.Max >= 4).Id;
        var there = NearStart();
        var s = new Session(document: DocumentAt(Saved(there)));
        s.Harness.StartCampaign(s.Lease);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        s.Send(0, "battles");
        s.Send(0, "pickEncounter", new { encounter = unstamped });
        for (var p = 0; p < 4; p++)
            s.Harness.SetConnected(p, false);
        s.PlayOut();
        Assert.Equal(TacticsPhase.Results, s.Game.Phase);
        s.Harness.SetConnected(0, true);

        s.Send(0, "next");

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(there, s.Host.Explore!.At);
        Assert.Equal(TacticsGame.Screens.Walk, s.Screen(0)?.Screen);
        Assert.Equal(there, TacticsDocument.Read(s.Lease.Document).World!.At);
    }

    [Fact]
    public void End_session_saves_and_ends_the_game()
    {
        var s = new Session();
        s.ToWalking();

        s.Send(1, "endSession");
        Assert.False(s.Harness.IsOver);

        s.Send(0, "endSession");

        Assert.True(s.Harness.IsOver);
        Assert.Contains("level 1", s.Harness.Results!.Message, StringComparison.Ordinal);
        Assert.Equal(Start, TacticsDocument.Read(s.Lease.Document).World!.At);
    }

    [Fact]
    public void End_session_means_nothing_away_from_the_walk_screen()
    {
        var s = new Session();
        s.ToBattle();

        s.Send(0, "endSession");
        s.Send(0, "battles");
        s.Send(0, "backToWalk");

        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.Battle, s.Game.Phase);
        for (var p = 0; p < 4; p++)
            Assert.Null(s.Screen(p)?.Header);    // a late tap isn't a mistake to scold anyone for
    }

    [Fact]
    public void A_member_with_no_character_picks_one_without_stopping_the_party()
    {
        var doc = TacticsDocument.Read(DocumentAt(Saved(NearStart())));
        var s = new Session(document: (doc with { Members = doc.Members.Remove("pr_member4") }).ToJson());
        s.Harness.Resume(s.Lease);
        s.Harness.SetConnected(0, true);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);

        s.Harness.SetConnected(3, true);
        Assert.Equal(TacticsGame.Screens.PickClass, s.Screen(3)?.Screen);
        s.Send(3, "pickClass", new { @class = "nonsense" });
        Assert.NotNull(s.Screen(3)?.Header);
        s.Send(3, "pickClass", new { @class = "wizard" });

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.NotNull(s.Game.Document.For(s.Profiles[3]!.Value));
        var walk = s.Screen(3);
        Assert.Equal(TacticsGame.Screens.Walk, walk?.Screen);
        Assert.Null(walk?.Header);                   // a walk view never holds a toast
    }

    [Fact]
    public void A_resume_with_nobody_here_runs_no_timers()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart())));
        s.Harness.Resume(s.Lease);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Null(s.Game.Leader);
        Assert.Empty(s.Harness.ActiveTimers);
        s.Harness.Advance(TimeSpan.FromMinutes(5));
        Assert.Empty(s.Harness.ActiveTimers);
    }

    [Fact]
    public void A_resume_with_a_member_who_never_chose_still_reaches_the_world()
    {
        var s = new Session();
        s.Harness.Resume(s.Lease);

        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        s.Harness.SetConnected(1, true);
        Assert.Equal(TacticsGame.Screens.PickClass, s.Screen(1)?.Screen);
    }

    [Fact]
    public void A_battle_saved_mid_fight_still_continues()
    {
        var s = new Session();
        s.ToBattle();

        var again = new Session(document: s.Lease.Document);
        again.Harness.StartCampaign(again.Lease);

        Assert.Equal(TacticsPhase.Battle, again.Game.Phase);
        Assert.Equal(Ford, again.Host.Explore!.Region);       // a TV joining the fight learns the region
        Assert.DoesNotContain(TacticsGame.Timers.Lead, again.Harness.ActiveTimers);
        Assert.Equal("pr_member2", TacticsDocument.Read(again.Lease.Document).World!.LastLeader);
    }

    [Fact]
    public void A_battle_input_at_exploration_does_nothing_and_says_nothing()
    {
        var s = new Session();
        s.ToWalking();
        var before = JsonSerializer.Serialize<ControllerView>(s.Harness.PlayerView(0), ArcJson.Options);

        s.Send(0, "pickEncounter", new { encounter = "river_crossing" });
        s.Send(0, "endTurn");
        s.Send(0, "move", new { path = new[] { new[] { 1, 1 } } });
        s.Send(0, "react", new { choice = "x" });
        s.Send(0, "continue");
        s.Send(0, "next");
        s.Send(0, "end");

        Assert.False(s.Harness.IsOver);
        Assert.Equal(TacticsPhase.Exploration, s.Game.Phase);
        Assert.Equal(before, JsonSerializer.Serialize<ControllerView>(s.Harness.PlayerView(0), ArcJson.Options));
    }

    [Fact]
    public void The_walk_view_is_what_the_phone_was_promised()
    {
        var s = new Session();
        s.ToWalking();

        var vip = s.Screen(0)!;
        Assert.Equal(TacticsGame.Screens.Walk, vip.Screen);
        Assert.Null(vip.DeadlineUtc);
        Assert.True(vip.Data.GetProperty("leader").GetBoolean());
        Assert.True(vip.Data.GetProperty("vip").GetBoolean());
        Assert.True(vip.Data.GetProperty("canBattle").GetBoolean());
        Assert.Equal("Player1", vip.Data.GetProperty("leaderName").GetString());
        Assert.Equal("The Ashen Ford", vip.Data.GetProperty("region").GetString());
        Assert.False(string.IsNullOrEmpty(vip.Data.GetProperty("objective").GetString()));
        Assert.Equal("/c/ABCD", vip.Data.GetProperty("portal").GetString());
        Assert.Equal(1, vip.Data.GetProperty("me").GetProperty("level").GetInt32());

        var other = s.Screen(1)!.Data;
        Assert.False(other.GetProperty("leader").GetBoolean());
        Assert.False(other.GetProperty("vip").GetBoolean());
        Assert.False(other.GetProperty("canBattle").GetBoolean());
        var party = other.GetProperty("party").EnumerateArray().ToArray();
        Assert.Equal(4, party.Length);
        Assert.Equal([true, false, false, false], party.Select(p => p.GetProperty("leads").GetBoolean()));
        Assert.All(party, p => Assert.True(p.GetProperty("here").GetBoolean()));
        Assert.Equal("Fighter", party[0].GetProperty("cls").GetString());
    }

    [Fact]
    public void The_banner_works_with_nobody_leading()
    {
        var s = new Session(document: DocumentAt(Saved(NearStart())));
        s.Harness.Resume(s.Lease);

        Assert.False(string.IsNullOrEmpty(s.Host.Banner));
        Assert.Null(s.Host.Explore!.Leader);
    }
}
