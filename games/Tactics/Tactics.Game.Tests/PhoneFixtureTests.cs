using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Logic;

namespace Tactics.Game.Tests;

/// <summary>
/// Pins every phone screen the game sends to a checked-in fixture under <c>web/play/test/fixtures/tactics/</c>, which the
/// phone module's Vitest tests render (adapter spec §10). Each fixture is a real view from a real session. Run with
/// <c>UPDATE_FIXTURES=1</c> to rewrite them after a deliberate change, and check the diff.
/// </summary>
public sealed class PhoneFixtureTests
{
    internal static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return LegacyPack(Path.Combine(dir.FullName, "content", "tactics", "base"));
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

    // Pin the pre-shop contract with a pack that has no shops. New shop fixtures use the real base pack.
    private static RulesPack LegacyPack(string content)
    {
        var folder = Directory.CreateTempSubdirectory("tactics-legacy-contract-");
        try
        {
            var source = Path.Combine(content, "rules");
            var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(source, "manifest.json")))!.AsObject();
            var files = manifest["files"]!.AsArray().Select(x => x!.GetValue<string>()).Where(x => !x.StartsWith("shops/", StringComparison.Ordinal)).ToArray();
            manifest["files"] = new JsonArray(files.Select(x => (JsonNode?)JsonValue.Create(x)).ToArray());
            foreach (var file in files)
            {
                var path = Path.Combine(folder.FullName, file);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(Path.Combine(source, file), path);
            }
            File.WriteAllText(Path.Combine(folder.FullName, "manifest.json"), manifest.ToJsonString());
            return RulesPack.Load([folder.FullName], ArtManifests.Load(content));
        }
        finally { folder.Delete(true); }
    }

    [Fact]
    public void Shop_screens_match_their_fixtures()
    {
        var s = ShopTests.AtShop(200);
        var screens = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal) { ["walk-with-gold"] = Node(s.Harness.PlayerView(0)) };
        s.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        screens["shop-open"] = Node(s.Harness.PlayerView(0));
        screens["shop-sell"] = Node(s.Harness.PlayerView(1));
        s.Send(0, "shopBuy", new { shop = "af_market_herbalist", item = "potion_of_healing", count = 3, trade = "fixture-buy" });
        screens["shop-sold-out"] = Node(s.Harness.PlayerView(0));
        var poor = ShopTests.AtShop(0);
        poor.Send(0, "interact", new { target = "shop:af_market_herbalist" });
        screens["shop-poor"] = Node(poor.Harness.PlayerView(0));
        var doc = poor.Game.Document with { Members = poor.Game.Document.Members.Remove(poor.Profiles[1]!.Value.Value) };
        var spectator = new TacticsGameTests.Session(document: doc.ToJson());
        spectator.Harness.Resume(spectator.Lease);
        for (var i = 0; i < 4; i++) spectator.Harness.SetConnected(i, true);
        screens["shop-spectator"] = Node(spectator.Harness.PlayerView(1));
        Check(screens);
    }

    [Fact]
    public void Recovery_screens_match_their_fixtures()
    {
        var camp = RestTests.AtCamp();
        camp.Send(0, "interact", new { target = "fire" });
        var screens = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal) { ["camp-vote"] = Node(camp.Harness.PlayerView(0)) };
        RestTests.Vote(camp);
        screens["camp-job"] = Node(camp.Harness.PlayerView(0));
        camp.Send(0, "campJob", new { job = "tend" });
        screens["camp-target"] = Node(camp.Harness.PlayerView(0));
        camp.Send(0, "campJob", new { job = "cook" });
        for (var i = 1; i < 4; i++) camp.Send(i, "campJob", new { job = "rest" });
        screens["camp-rolled"] = Node(camp.Harness.PlayerView(0));
        var inn = AuthoredService("af_inn");
        screens["walk-ready-to-rest"] = Node(inn.Harness.PlayerView(0));
        inn.Send(0, "interact", new { target = "shop:af_inn" });
        inn.Send(0, "restPropose", new { shop = "af_inn" });
        RestTests.Vote(inn);
        screens["inn-rooms"] = Node(inn.Harness.PlayerView(0));
        var temple = AuthoredService("af_chapel");
        temple.Send(0, "interact", new { target = "shop:af_chapel" });
        screens["temple"] = Node(temple.Harness.PlayerView(0));
        Check(screens);
    }

    private static TacticsGameTests.Session AuthoredService(string id)
    {
        var s = ShopTests.AtShop(100);
        var rules = TacticsGameTests.Rules;
        var shop = rules.ShopsIn("ashen_ford").Single(x => x.Id == id);
        var at = rules.RegionFor("ashen_ford")!.Def.People[shop.Keeper.Person!.Value].At;
        var target = new InteractableDef(id, "stall", at);
        var near = rules.ReachFor("ashen_ford")!.Where(t => t != at && Interactables.InReach(rules.GroundFor("ashen_ford")!.Terrain, t, target)).Order().First();
        var doc = s.Game.Document;
        doc = doc with { World = doc.World! with { At = near, Facing = at, RestReady = true },
            Members = Ordinal.Map(doc.Members.Select(m => (m.Key, m.Value with { Character = m.Value.Character with { Injuries = [new("black_eye"), new("cracked_ribs")] } }))) };
        return RestTests.Again(doc, rules: rules);
    }

    private static string FixtureDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "web", "play", "test", "fixtures", "tactics");
    }

    [Fact]
    public void Every_phone_screen_matches_its_fixture()
    {
        var screens = Capture();
        Assert.Equal([.. new[] { "pickClass", "pickEncounter", "react", "results", "status", "turn", "walk-follower", "walk-leader" }], screens.Keys.Order(StringComparer.Ordinal));
        Assert.True(screens["pickEncounter"]["data"]!["canWalk"]!.GetValue<bool>());       // a world to go back to

        Check(screens);
    }

    /// <summary>
    /// The story's phone screens (overworld 5d §9), from the ford choice played with the base pack's lore: Interact on the walk screen, a line
    /// for a follower and for the leader, the leader's choice and how everyone else sees it, a party vote with a vote in, a tie-break, and a skill check being picked and
    /// rolled. Each is a real view from a real session, as the others are.
    /// </summary>
    [Fact]
    public void Every_story_screen_matches_its_fixture()
    {
        var screens = CaptureStory();
        Assert.Equal([.. new[] { "check-pick", "check-rolled", "choice-follower", "choice-leader", "dialogue", "dialogue-leader", "vote", "vote-tie", "walk-interact" }],
            screens.Keys.Order(StringComparer.Ordinal));

        Check(screens);
    }

    /// <summary>
    /// The walk screen in a region with things to interact with (interactables design §2.5): the leader a step from the vault's chest and its
    /// key, with both in the list and the nearer one on the button, and a follower, whose list is empty.
    /// </summary>
    [Fact]
    public void The_walk_screen_with_things_in_reach_matches_its_fixture()
    {
        var s = Vault.At(new Tile(7, 2));
        Check(new SortedDictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            ["walk-things"] = Node(s.Harness.PlayerView(0)),
            ["walk-things-follower"] = Node(s.Harness.PlayerView(1)),
        });
    }

    /// <summary>
    /// The leader's Search (interactables design §7.4) in the vault: the check screen, with no DC, as the party picks who rolls, and its roll,
    /// which has none either. Nothing hidden is near, and it plays the same.
    /// </summary>
    [Fact]
    public void The_search_check_matches_its_fixture()
    {
        var s = Vault.At(new Tile(7, 2));
        s.Send(0, "search");
        StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.PickRoller, "the pick of who rolls");
        var picking = Node(s.Harness.PlayerView(1));
        StoryVoteTests.Until(s, () => s.Host.Story?.State == TacticsGame.StoryStates.Rolled, "the roll");
        Check(new SortedDictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            ["check-search"] = picking,
            ["check-search-rolled"] = Node(s.Harness.PlayerView(1)),
        });
    }

    private static JsonNode Node(ControllerView view) => JsonSerializer.SerializeToNode<ControllerView>(view with { ViewId = "v1" }, ArcJson.Options)!;

    // Freeze the pre-quest lore contract as well as the pre-shop rules: existing screens must remain byte-identical.
    private static readonly Lazy<LoreBook> LegacyStoryLore = new(() =>
    {
        var folder = Directory.CreateTempSubdirectory("tactics-legacy-lore-");
        try
        {
            var source = Path.Combine(ExplorationTests.ContentRules(), "lore");
            foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
            {
                var to = Path.Combine(folder.FullName, "lore", Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(to)!); File.Copy(file, to);
            }
            var indexPath = Path.Combine(folder.FullName, "lore", "pack.json");
            var index = JsonNode.Parse(File.ReadAllText(indexPath))!;
            var files = index["files"]!.AsArray();
            foreach (var file in files.Where(f => f!.GetValue<string>() is var name &&
                (name.StartsWith("conversations/", StringComparison.Ordinal) || name is "quests.json" or "scenes/side_quests.json" or "barks/townsfolk.json")).ToArray()) files.Remove(file);
            File.WriteAllText(indexPath, index.ToJsonString());
            foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "audio"), "*.ogg"))
                File.CreateSymbolicLink(Path.Combine(folder.FullName, "lore", "audio", Path.GetFileName(file)), file);
            var book = LoreBook.Load(folder.FullName);
            foreach (var line in book.Scenes.SelectMany(sc => sc.Lines)) _ = book.ClipSeconds(line.Id);
            return book;
        }
        finally { folder.Delete(recursive: true); }
    });

    private static SortedDictionary<string, JsonNode> CaptureStory()
    {
        var seen = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        TacticsGameTests.Session Ford()
        {
            var s = new TacticsGameTests.Session(seed: 3, game: new TacticsGame(Pack.Value, lore: LegacyStoryLore.Value,
                seed: StoryTests.SeedFor(StoryTests.FordChoice) with { At = StoryTests.NearSpot(StoryTests.FordChoice) }));
            s.ToWalking();
            return s;
        }
        void Until(TacticsGameTests.Session s, Func<bool> done)
        {
            for (var i = 0; i < 3_000 && !done(); i++)
                s.Harness.Advance(TimeSpan.FromMilliseconds(100));
            Assert.True(done());
        }
        StoryView Story(TacticsGameTests.Session s) => s.Host.Story!;

        // The leader is seat 0, the session's first member.
        var s = Ford();
        seen["walk-interact"] = Node(s.Harness.PlayerView(0));
        s.Send(0, "interact");
        StoryTests.LineEnds(s);
        StoryTests.LineEnds(s);     // a line with a speaker who has a portrait
        seen["dialogue-leader"] = Node(s.Harness.PlayerView(0));
        seen["dialogue"] = Node(s.Harness.PlayerView(1));
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking);
        seen["choice-leader"] = Node(s.Harness.PlayerView(0));
        s.Send(0, "vote", new { option = "cases" });
        seen["choice-follower"] = Node(s.Harness.PlayerView(1));     // read-only, with the leader's pick
        Until(s, () => Story(s).State == TacticsGame.StoryStates.Asking && StoryTests.Cursor(s)!.Node == "vote");
        s.Harness.SetConnected(3, false);
        s.Send(1, "vote", new { option = "force" });
        seen["vote"] = Node(s.Harness.PlayerView(1));
        s.Send(2, "vote", new { option = "parley" });
        s.Harness.Advance(Story(s).Ask!.Deadline!.Value - s.Harness.Now);
        Assert.Equal(TacticsGame.StoryStates.TieBreak, Story(s).State);
        seen["vote-tie"] = Node(s.Harness.PlayerView(0));

        var c = Ford();
        c.Send(0, "interact");
        Until(c, () => Story(c).State == TacticsGame.StoryStates.Asking);
        c.Send(0, "vote", new { option = "cases" });
        Until(c, () => Story(c).State == TacticsGame.StoryStates.Asking && StoryTests.Cursor(c)!.Node == "vote");
        c.Harness.SetConnected(3, false);       // an away card
        for (var p = 0; p < 3; p++)
            c.Send(p, "vote", new { option = "pay" });
        Until(c, () => Story(c).State == TacticsGame.StoryStates.PickRoller);
        c.Send(1, "pickRoller", new { member = "pr_member3" });
        seen["check-pick"] = Node(c.Harness.PlayerView(1));
        for (var p = 0; p < 3; p++)
            c.Send(p, "pickRoller", new { member = "pr_member3" });
        Until(c, () => Story(c).State == TacticsGame.StoryStates.Rolled);
        seen["check-rolled"] = Node(c.Harness.PlayerView(1));
        return seen;
    }

    /// <summary>
    /// A pack with no region sends the phones what it always did, but for the battle list's <c>canWalk</c>, which is false there: its
    /// own fixture, <c>pickEncounter-noWalk</c>, from which the phone hides "Back to walking".
    /// </summary>
    [Fact]
    public void A_pack_with_no_region_sends_the_phones_what_it_always_did()
    {
        var screens = Capture(ExplorationTests.NoRegion.Value);
        Assert.True(screens.Remove("pickEncounter", out var list));
        Assert.False(list["data"]!["canWalk"]!.GetValue<bool>());
        screens["pickEncounter-noWalk"] = list;
        Check(screens);
    }

    [Fact]
    public void Portal_views_match_their_fixtures()
    {
        var rules = Pack.Value;
        var portal = new TacticsCampaignRules(rules);
        var ana = new ProfileId("pr_ana");
        var bo = new ProfileId("pr_bo");
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", ana,
            [new CampaignMember(ana, "Ana", CampaignRole.Owner, DateTimeOffset.UnixEpoch),
             new CampaignMember(bo, "Bo", CampaignRole.Player, DateTimeOffset.UnixEpoch)], 1);
        var empty = portal.NewDocument(info);

        // Ana has a level-4 dwarf cleric with an increase to spend and a subclass to choose; Bo has nothing yet.
        var made = portal.Handle(info, empty, ana, "createCharacter", JsonSerializer.SerializeToElement(new
        {
            race = "dwarf", @class = "cleric", background = "acolyte", scores = new { str = 8, dex = 8, con = 15, @int = 8, wis = 15, cha = 15 },
        }, ArcJson.Options)).Document!.Value;
        var doc = TacticsDocument.Read(made);
        var cleric = doc.Members[ana.Value];
        doc = doc with
        {
            PartyLevel = 4,
            Members = doc.Members.SetItem(ana.Value, cleric with { Xp = 3200, Character = CharacterRules.LevelUp(cleric.Character, 4, rules) }),
        };

        var views = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            ["portal-new"] = JsonNode.Parse(portal.PortalView(info, doc.ToJson(), bo).GetRawText())!,
            ["portal-character"] = JsonNode.Parse(portal.PortalView(info, doc.ToJson(), ana).GetRawText())!,
        };
        Check(views);
    }

    [Fact]
    public void Npc_and_quest_journal_screens_match_their_fixtures()
    {
        var screens = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        var talk = ConversationQuestTests.At("halden_varrow");
        ConversationQuestTests.Talk(talk, "halden_varrow");
        screens["talk-vote"] = Node(talk.Harness.PlayerView(ConversationQuestTests.Lead(talk)));
        var check = ConversationQuestTests.At("corvin_ashby", "sq_forge_asked");
        ConversationQuestTests.Talk(check, "corvin_ashby");
        ConversationQuestTests.Choose(check, "arcana");
        StoryVoteTests.Until(check, () => check.Host.Story!.State == TacticsGame.StoryStates.PickRoller, "conversation skill check");
        screens["talk-check"] = Node(check.Harness.PlayerView(ConversationQuestTests.Lead(check)));
        var s = ConversationQuestTests.At("halden_varrow", "sq_grain_asked", "sq_grain_tally");
        var leader = ConversationQuestTests.Lead(s); var follower = (leader + 1) % 4;
        s.Send(leader, "questLog", new { action = "pin", quest = "sq_missing_grain" });
        screens["walk-pinned"] = Node(s.Harness.PlayerView(leader));
        var privateView = Node(s.Harness.PlayerView(follower));
        privateView["screen"] = "questLog";
        privateView["data"] = privateView["data"]!["journal"]!.DeepClone();
        screens["questlog-private"] = privateView;
        s.Send(leader, "questLog", new { action = "open", quest = "sq_missing_grain" });
        screens["questlog-leader"] = Node(s.Harness.PlayerView(leader));
        screens["questlog-viewer"] = Node(s.Harness.PlayerView(follower));
        s.Send(leader, "questLog", new { action = "close" });
        ConversationQuestTests.Talk(s, "halden_varrow"); ConversationQuestTests.Choose(s, "quiet");
        StoryVoteTests.Until(s, () => s.Game.Document.World!.Flags.Contains("sq_grain_done"), "quest complete");
        // Leave the final spoken line immediately, while the result tracker is still visible.
        s.Send(leader, "skip");
        screens["walk-pinned-done"] = Node(s.Harness.PlayerView(leader));
        Check(screens);
    }

    private static void Check(SortedDictionary<string, JsonNode> screens)
    {
        var dir = FixtureDir();
        var update = Environment.GetEnvironmentVariable("UPDATE_FIXTURES") == "1";
        foreach (var (name, json) in screens)
        {
            var path = Path.Combine(dir, name + ".json");
            if (update)
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(path, json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
                continue;
            }
            Assert.True(File.Exists(path), $"Missing fixture {path}; run with UPDATE_FIXTURES=1.");
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(File.ReadAllText(path)), json),
                $"{name}.json differs from what the game sends; run with UPDATE_FIXTURES=1 and check the diff.");
        }
    }

    /// <summary>
    /// Plays a four-member session until each screen has been seen once, keeping the first of each. The walk screens come first from a
    /// game as it's played, with no battle list; the rest from one with the Development battle list, which is the way to a fight that
    /// doesn't depend on the story.
    /// </summary>
    [Fact]
    public void Environmental_turn_and_waiting_screens_match_real_resumed_views()
    {
        var seed = new TacticsGameTests.Session();
        seed.ToBattle();
        var doc = TacticsDocument.Read(seed.Lease.Document);
        var save = doc.Battle!;
        var mage = new UnitId("hero4");
        var swimmer = new UnitId("hero1");
        var state = save.State with { Environment = true, TurnIndex = save.State.Order.IndexOf(mage), Pending = null };
        var at = state.Units[mage].Pos;
        state = state with
        {
            Units = state.Units.SetItem(swimmer, state.Units[swimmer] with { Pos = new Tile(6, 7) })
                .SetItem(new UnitId("goblin.1"), state.Units[new UnitId("goblin.1")] with { Pos = new Tile(4, 9) }),
            Zones = [new Zone(1, mage, "burning", [at, new Tile(3, 11)], 2, false, false, Pack.Value.Environment.Surfaces["burning"].Passives, Surface: true),
                new Zone(2, mage, "oil", [new Tile(4, 9)], 10, false, false, [], Surface: true)],
            NextSerial = 3,
        };
        var resumed = new TacticsGameTests.Session(document: (doc with { Battle = save with { State = state, Log = [] } }).ToJson());
        resumed.Harness.Resume(resumed.Lease);
        for (var i = 0; i < 4; i++) resumed.Harness.SetConnected(i, true);
        Assert.Equal(TacticsPhase.Battle, resumed.Game.Phase);
        Assert.Equal("turn", resumed.Screen(3)!.Screen);
        Assert.Equal("status", resumed.Screen(0)!.Screen);
        Assert.Contains("burning", resumed.Screen(3)!.Data.GetProperty("surfaces").EnumerateArray().Select(e => e.GetString()));
        Assert.Contains("water", resumed.Screen(0)!.Data.GetProperty("surfaces").EnumerateArray().Select(e => e.GetString()));
        Check(new SortedDictionary<string, JsonNode>(StringComparer.Ordinal)
        {
            ["environment-turn"] = Node(resumed.Screen(3)!),
            ["environment-status"] = Node(resumed.Screen(0)!),
        });
    }

    private static SortedDictionary<string, JsonNode> Capture(RulesPack? pack = null)
    {
        var rules = pack ?? Pack.Value;
        ProfileId?[] profiles = [.. Enumerable.Range(1, 4).Select(i => (ProfileId?)new ProfileId($"pr_member{i}"))];
        var seen = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];

        // One screen, two ways to see it: only the leader has the pad.
        static string Named(CustomView view) => view.Screen != TacticsGame.Screens.Walk ? view.Screen
            : view.Data.GetProperty("leader").GetBoolean() ? "walk-leader" : "walk-follower";

        (TacticsGame Game, GameHarness<TacticsGame> H, Action Look) Mustered(bool battleList)
        {
            var game = new TacticsGame(rules, battleList: battleList);
            var h = new GameHarness<TacticsGame>(game, playerCount: 4, seed: 3, profiles: profiles);
            var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
                [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
            void Look()
            {
                for (var p = 0; p < 4; p++)
                    if (h.PlayerView(p) is CustomView view && !seen.ContainsKey(Named(view)))
                        // As the base type, as the server sends it, so the "kind" discriminator is written.
                        seen[Named(view)] = JsonSerializer.SerializeToNode<ControllerView>(view with { ViewId = "v1" }, ArcJson.Options)!;
            }
            h.StartCampaign(new FakeCampaignLease(info, new TacticsCampaignRules(rules).NewDocument(info)));
            Look();
            for (var i = 0; i < 4; i++)
                h.Input(i, new { action = "pickClass", data = new { @class = classes[i] } });
            Look();
            return (game, h, Look);
        }

        Mustered(battleList: false);
        var (game, h, Look) = Mustered(battleList: true);
        h.Input(0, new { action = "battles" });
        Look();
        h.Input(0, new { action = "pickEncounter", data = new { encounter = "river_crossing" } });
        // Fought in the world, its opening beat waits for the TV's walk-in (overworld 5c), which a TV here has shown.
        if (((TacticsHostView)game.HostView()).Battle is { Stage: not null } battle)
            h.HostSignal("shown", new { seq = battle.Beats[^1].Seq });

        // Players end their turns at once (after the first turn screen is seen) so enemies walk past them and draw
        // reactions; reactions are left to time out.
        for (var step = 0; step < 5_000 && game.Phase == TacticsPhase.Battle && seen.Count < (rules.Regions.IsEmpty ? 5 : 7); step++)
        {
            Look();
            for (var p = 0; p < 4; p++)
                if (h.PlayerView(p) is CustomView { Screen: TacticsGame.Screens.Turn })
                    h.Input(p, new { action = "endTurn" });
            h.Advance(TimeSpan.FromSeconds(7));
        }
        for (var p = 1; p < 4; p++)
            h.SetConnected(p, false);
        h.SetConnected(0, false);
        for (var step = 0; step < 20_000 && game.Phase == TacticsPhase.Battle; step++)
            h.Advance(TimeSpan.FromSeconds(7));
        h.SetConnected(0, true);
        Look();
        return seen;
    }
}
