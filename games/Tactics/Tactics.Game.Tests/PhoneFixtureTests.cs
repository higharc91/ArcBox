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
    private static readonly Lazy<RulesPack> Pack = new(() =>
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
                return RulesPack.Load([Path.Combine(dir.FullName, "content", "tactics", "base", "rules")]);
        throw new DirectoryNotFoundException("No ArcBox.slnx above the test binaries.");
    });

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

        Check(screens);
    }

    [Fact]
    public void A_pack_with_no_region_sends_the_phones_what_it_always_did() => Check(Capture(ExplorationTests.NoRegion.Value));

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
