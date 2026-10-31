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
        Assert.Equal([.. new[] { "pickClass", "pickEncounter", "react", "results", "status", "turn" }], screens.Keys.Order(StringComparer.Ordinal));

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

    /// <summary>Plays a four-member session until each screen has been seen once, keeping the first of each.</summary>
    private static SortedDictionary<string, JsonNode> Capture()
    {
        var rules = Pack.Value;
        ProfileId?[] profiles = [.. Enumerable.Range(1, 4).Select(i => (ProfileId?)new ProfileId($"pr_member{i}"))];
        var game = new TacticsGame(rules);
        var h = new GameHarness<TacticsGame>(game, playerCount: 4, seed: 3, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}", CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        var lease = new FakeCampaignLease(info, new TacticsCampaignRules(rules).NewDocument(info));
        var seen = new SortedDictionary<string, JsonNode>(StringComparer.Ordinal);

        void Look()
        {
            for (var p = 0; p < 4; p++)
                if (h.PlayerView(p) is CustomView view && !seen.ContainsKey(view.Screen))
                    // As the base type, as the server sends it, so the "kind" discriminator is written.
                    seen[view.Screen] = JsonSerializer.SerializeToNode<ControllerView>(view with { ViewId = "v1" }, ArcJson.Options)!;
        }

        h.StartCampaign(lease);
        Look();
        string[] classes = ["fighter", "cleric", "rogue", "wizard"];
        for (var i = 0; i < 4; i++)
            h.Input(i, new { action = "pickClass", data = new { @class = classes[i] } });
        Look();
        h.Input(0, new { action = "pickEncounter", data = new { encounter = "river_crossing" } });

        // Players end their turns at once (after the first turn screen is seen) so enemies walk past them and draw
        // reactions; reactions are left to time out.
        for (var step = 0; step < 5_000 && game.Phase == TacticsPhase.Battle && seen.Count < 5; step++)
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
