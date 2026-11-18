using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ArcBox.Sdk.Content;
using Meridian.Presentation.Board;
using Meridian.Presentation.Hud;

namespace Meridian.Presentation.Tests.Scenarios;

/// <summary>
/// Spec success criterion 8 (M7 plan, review focus 1): a thirteenth scenario touches only its class, its content file, its
/// tests and one registration line. Here it is added in a test, from exactly those, and the whole game takes it.
/// </summary>
public sealed class ThirteenthScenarioTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("meridian-thirteenth-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>A small co-op scenario no code outside this file knows: light the beacon to win.</summary>
    private sealed class BeaconHunt : IScenario
    {
        public const string Id = "beacon_hunt";
        public ScenarioInfo Info { get; } = new(Id, "co-op", 3, 6);
        public bool CanRun(RulesPack pack, GameState state, BreachInfo breach) => true;

        public GameState Setup(RulesPack pack, GameState state, BreachInfo breach)
            => state.WithBoard(new ScenarioBoard(Id,
                state.Crew.Select(c => new RoleCard(c.Seat, "keeper", "keeper", Audience.Everyone)).ToEqArray(),
                [new Token("beacon", "beacon", Primitives.FirstOf(state, "airlock")!.Value, Audience.Everyone, State: "dark")],
                [new Counter("lit", 0, Audience.Everyone), new Counter("tally", 0, Audience.Everyone)],
                [new Objective("keeper", pack.ScenarioDef(Id).Role("keeper").Objectives[0], ObjectiveState.Open, Audience.Everyone)],
                pack.ScenarioDef(Id).RoundLimit, state.Round));

        public GameState On(RulesPack pack, GameState state, GameEvent e, List<GameEvent> events) => state;

        public IEnumerable<ScenarioAction> ActionsFor(RulesPack pack, GameState state, SeatId seat)
            => state.CrewOf(seat).Aboard && state.CrewOf(seat).At == state.Board!.Tokens[0].At ? [new ScenarioAction("light", "Light the beacon")] : [];

        public GameState Act(RulesPack pack, GameState state, ScenarioAct act, List<GameEvent> events)
        {
            events.Add(new ScenarioActed(act.From, act.Action, null, "The beacon burns."));
            var board = state.Board!;
            return state.WithBoard(board.WithCounter("lit", 1).WithToken(board.Tokens[0] with { State = "lit" }));
        }

        public Outcome? Evaluate(RulesPack pack, GameState state)
            => state.Board!.Value("lit") > 0 ? new Outcome(state.Crew.Select(c => c.Seat).ToEqArray(), "The beacon burns.") : null;

        public Outcome AtLimit(RulesPack pack, GameState state) => new([], "Nobody lit the beacon.");

        public IBotPolicy Bot(string role)
            => new PolicyBot(ctx => BotMoves.Act(ctx, "light") ?? BotMoves.Toward(ctx, ctx.State.Board!.Tokens[0].At));
    }

    /// <summary>The shipped content, plus the scenario's own file and its line in the Breach table's list.</summary>
    private RulesPack PackWithBeacon()
    {
        var source = Path.GetDirectoryName(BasePack.Root)!;
        foreach (var file in Directory.EnumerateFiles(source, "*.json", SearchOption.AllDirectories))
        {
            var to = Path.Combine(_root, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }
        var rules = Path.Combine(_root, Path.GetFileName(BasePack.Root));
        File.WriteAllText(Path.Combine(rules, "rules", "scenarios", "beacon_hunt.json"), """
            {
              "id": "beacon_hunt", "title": "Beacon Hunt", "roundLimit": 6,
              "announcement": "Light the beacon in the Airlock before the dark takes the ship.",
              "roles": [ { "id": "keeper", "side": "crew", "name": "Keeper", "briefing": "Light the beacon.", "objectives": [ "Light the beacon in the Airlock." ] } ],
              "tokens": { "beacon": { "label": "◎", "colour": "#7fffd4", "name": "Beacon", "states": { "lit": { "label": "☼", "colour": "#ffd447" } } } },
              "counters": { "lit": "Beacons lit", "tally": null }
            }
            """);
        var breach = JsonNode.Parse(File.ReadAllText(Path.Combine(rules, "rules", "breach.json")))!;
        breach["scenarios"]!.AsArray().Add("beacon_hunt");
        File.WriteAllText(Path.Combine(rules, "rules", "breach.json"), breach.ToJsonString());
        // The one registration line.
        var scenarios = new ScenarioSet([.. ScenarioRegistry.Default.All, new BeaconHunt()]);
        return PackLoader.Load(PhysicalFileSource.Instance, rules, scenarios: scenarios).WithForcedScenario(BeaconHunt.Id);
    }

    [Fact]
    public void A_scenario_added_from_its_own_files_plays_briefs_and_shows_on_the_TV()
    {
        var pack = PackWithBeacon();
        var game = new Harness(pack, players: 4, seed: 3).Board();
        var sawPanel = false;
        game.PlayUntil(() =>
        {
            var view = (MeridianHostView)game.Game.HostView();
            if (view.Scenario is { Reveal: null } panel)
            {
                sawPanel = true;
                var hud = HudModel.Of(view, null, 0, ScenarioLooks.Of(pack));
                Assert.Contains(hud.Stats, s => s.Label == "Beacons lit");                  // its content's label
                Assert.DoesNotContain(hud.Stats, s => s.Label.Contains("tally", StringComparison.OrdinalIgnoreCase));   // kept off
                Assert.Equal("Beacon Hunt", hud.Scenario);
                var beacon = panel.Tokens.Single();
                Assert.Equal(new Glyph(beacon.State == "lit" ? "☼" : "◎", beacon.State == "lit" ? "#ffd447" : "#7fffd4"),
                             ScenarioLooks.Of(pack).Token(beacon.Kind, beacon.State));
            }
            return game.Session.Phase == SessionPhase.Debrief;
        }, steps: 6000);
        Assert.True(sawPanel, "the TV never showed the scenario");
        Assert.Equal(SessionPhase.Debrief, game.Session.Phase);
        Assert.Equal(BeaconHunt.Id, game.State.Board!.Scenario);
    }

    // ---- The other half: no shared code names a scenario

    /// <summary>
    /// The words only a scenario may say: its id, role ids, token kinds and counter ids. <c>crew</c> is exempt: it's the core
    /// rules' word for the crew and a side in spec §4.3, as well as the crew sprites' art set.
    /// </summary>
    private static HashSet<string> ScenarioWords()
    {
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach (var def in BasePack.Pack.ScenarioDefs)
        {
            words.Add(def.Id);
            words.UnionWith(def.Roles.Select(r => r.Id));
            words.UnionWith(def.Tokens?.Keys ?? []);
            words.UnionWith(def.Counters?.Keys ?? []);
            words.UnionWith(def.Votes?.Keys ?? []);
        }
        words.Remove("crew");
        return words;
    }

    [Fact]
    public void No_shared_code_names_a_scenario_its_tokens_or_its_counters()
    {
        var repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(BasePack.Root)!, "..", ".."));
        var shared = new[]
        {
            "games/Meridian/Meridian.Game", "games/Meridian/Meridian.Presentation", "src/ArcBox.Host/games/meridian",
            "src/ArcBox.Server/Games/Meridian", "web/play/src/games/meridian", "web/play/src/dev/MeridianBoard.tsx",
        };
        var words = ScenarioWords();
        var literal = new Regex("""["']([a-z_\-]+)["']""");
        var hits = new List<string>();
        foreach (var dir in shared)
        {
            var path = Path.Combine(repo, dir);
            foreach (var file in (File.Exists(path) ? [path] : Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                         .Where(f => f.EndsWith(".cs", StringComparison.Ordinal) || f.EndsWith(".ts", StringComparison.Ordinal)
                                     || f.EndsWith(".tsx", StringComparison.Ordinal))
                         .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
            {
                var lines = File.ReadAllLines(file);
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimStart();
                    if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith('*') || line.StartsWith("/*", StringComparison.Ordinal))
                    {
                        continue;   // comments may give examples
                    }
                    foreach (Match m in literal.Matches(line))
                    {
                        if (words.Contains(m.Groups[1].Value))
                        {
                            hits.Add($"{Path.GetRelativePath(repo, file)}:{i + 1}: \"{m.Groups[1].Value}\"");
                        }
                    }
                }
            }
        }
        Assert.True(hits.Count == 0, "Scenario knowledge outside the scenarios (spec criterion 8):\n" + string.Join("\n", hits));
    }

    [Fact]
    public void The_boundary_check_catches_a_scenario_name()
        => Assert.Contains("one_of_us_isnt", ScenarioWords());
}
