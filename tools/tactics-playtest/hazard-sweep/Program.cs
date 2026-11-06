using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using ArcBox.Sdk;
using ArcBox.Sdk.Campaigns;
using ArcBox.Sdk.Testing;
using Tactics.Game;
using Tactics.Logic;

// Adapted from docs/superpowers/reports/2026-10-31-tactics-overworld/battle-sweep-harness.cs.txt.
// The historical Objectives.ZzNoFix switch no longer exists. The current adapter requires battleList: true.
var root = Directory.GetCurrentDirectory();
var rulesPath = Path.Combine(root, "content/tactics/base/rules");
var output = Environment.GetEnvironmentVariable("SWEEP_OUT") ?? Path.Combine(root, "artifacts/hazard-sweep.tsv");
var seeds = int.Parse(Environment.GetEnvironmentVariable("SWEEP_SEEDS") ?? "100");
var workers = int.Parse(Environment.GetEnvironmentVariable("SWEEP_WORKERS") ?? "4");
ArgumentOutOfRangeException.ThrowIfLessThan(seeds, 1);
ArgumentOutOfRangeException.ThrowIfLessThan(workers, 1);
var packRoot = Path.GetDirectoryName(rulesPath)!;
var rules = RulesPack.Load([rulesPath], ArtManifests.Load(packRoot));
Invariants.Enabled = true;
var encounters = Environment.GetEnvironmentVariable("SWEEP_ENCOUNTERS")?.Split(',') ?? [.. rules.Encounters.Keys];
var cases = new List<Case>();
foreach (var id in encounters.Order(StringComparer.Ordinal))
    foreach (var n in new[] { 1, 2, 3, 4, 6 })
    {
        if (n > rules.Encounters[id].Party.Max)
            continue;
        foreach (var away in n >= 2 ? new[] { 0, 1 } : new[] { 0 })
            foreach (var mode in new[] { "pass", "timeout" })
                for (var seed = 1; seed <= seeds; seed++)
                    cases.Add(new Case(id, n, away, mode, seed));
    }
var results = new ConcurrentDictionary<Case, Result>();
var watch = Stopwatch.StartNew();
var finished = 0;
Parallel.ForEach(cases, new ParallelOptions { MaxDegreeOfParallelism = workers }, c =>
{
    results[c] = Play(c, rules);
    var count = Interlocked.Increment(ref finished);
    if (count % 100 == 0)
        Console.Error.WriteLine($"{count}/{cases.Count} cases, {watch.Elapsed.TotalSeconds:0}s");
});
var report = new StringBuilder();
report.AppendLine($"cases {cases.Count}; seeds {seeds}; elapsed {watch.Elapsed}");
report.AppendLine("enc\tN\taway\tmode\tbattles\tWon\tLost\tFled\tunfinished\tnostart\tcrash");
foreach (var group in results.GroupBy(p => (p.Key.Enc, p.Key.N, p.Key.Away, p.Key.Mode)).OrderBy(g => g.Key))
{
    int Count(string kind) => group.Count(p => p.Value.Kind == kind);
    report.AppendLine($"{group.Key.Enc}\t{group.Key.N}\t{group.Key.Away}\t{group.Key.Mode}\t{group.Count()}\t{Count("Won")}\t{Count("Lost")}\t{Count("Fled")}\t{Count("unfinished")}\t{Count("nostart")}\t{Count("crash")}");
}
report.AppendLine("--- distinct crashes");
foreach (var group in results.Where(p => p.Value.Kind == "crash").GroupBy(p => p.Value.Message).OrderByDescending(g => g.Count()))
    report.AppendLine($"{group.Count()}\t{group.Key}");
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, report.ToString());
File.WriteAllLines(output + ".cases", results.OrderBy(p => p.Key.Enc, StringComparer.Ordinal).ThenBy(p => p.Key.N)
    .ThenBy(p => p.Key.Away).ThenBy(p => p.Key.Mode, StringComparer.Ordinal).ThenBy(p => p.Key.Seed)
    .Select(p => $"{p.Key.Enc}\t{p.Key.N}\t{p.Key.Away}\t{p.Key.Mode}\t{p.Key.Seed}\t{p.Value.Kind}\t{p.Value.Fingerprint}\t{p.Value.Message}"));
var crashes = results.Count(p => p.Value.Kind == "crash");
var noStart = results.Count(p => p.Value.Kind == "nostart");
Console.WriteLine($"{cases.Count} cases; {crashes} crashes; {noStart} did not start; {results.Count(p => p.Value.Kind == "unfinished")} unfinished; wrote {output}");
return crashes > 0 || noStart > 0 ? 1 : 0;

static Result Play(Case c, RulesPack rules)
{
    try
    {
        // Stable random class sampling belongs to the harness only; battle randomness is still the engine's seeded RNG.
        var random = new Random(c.Seed * 7919 + 17);
        var classes = rules.Classes.Keys.Order(StringComparer.Ordinal).ToArray();
        var profiles = Enumerable.Range(0, c.N).Select(i => (ProfileId?)new ProfileId($"pr_{i}")).ToArray();
        var timing = new TacticsTiming(TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50));
        var game = new TacticsGame(rules, timing, battleList: true);
        var harness = new GameHarness<TacticsGame>(game, playerCount: c.N, seed: c.Seed, profiles: profiles);
        var info = new CampaignInfo("c1", "ABCD", TacticsGame.Id, "Embers", profiles[0]!.Value,
            [.. profiles.Select((p, i) => new CampaignMember(p!.Value, $"Player{i + 1}",
                i == 0 ? CampaignRole.Owner : CampaignRole.Player, DateTimeOffset.UnixEpoch))], 1);
        var document = TacticsDocument.Read(new TacticsCampaignRules(rules).NewDocument(info)) with
            { PartyLevel = rules.Encounters[c.Enc].Level ?? 1 };
        var lease = new FakeCampaignLease(info, document.ToJson());
        harness.StartCampaign(lease);
        for (var i = 0; i < c.N; i++)
            harness.Input(i, new { action = "pickClass", data = new { @class = classes[random.Next(classes.Length)] } });
        harness.Input(0, new { action = "battles" });
        var online = c.N - c.Away;
        for (var i = online; i < c.N; i++)
            harness.SetConnected(i, false);
        harness.Input(0, new { action = "pickEncounter", data = new { encounter = c.Enc } });
        if (game.Phase != TacticsPhase.Battle)
            return new Result("nostart", game.Phase.ToString());
        for (var step = 0; step < 1500 && game.Phase == TacticsPhase.Battle; step++)
        {
            if (c.Mode == "pass")
                for (var p = 0; p < online; p++)
                    harness.Input(p, new { action = "endTurn" });
            harness.Advance(TimeSpan.FromMilliseconds(500));
        }
        if (game.Phase == TacticsPhase.Battle)
            return new Result("unfinished", "round " + game.Document.Battle!.State.Round);
        var result = (BattleResult)typeof(TacticsGame).GetField("_result", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
        return new Result(result.Outcome.ToString(), "xp " + string.Join(',', game.Document.Members.Select(m => m.Value.Xp + "/" + m.Value.Character.Injuries.Length)));
    }
    catch (Exception e)
    {
        var normalized = Regex.Replace(Regex.Replace(e.Message, @"\(\d+, \d+\)", "(x, y)"), @"\b(hero|ally)\d+", "$1N");
        return new Result("crash", "", $"{e.GetType().Name}: {normalized}");
    }
}

internal sealed record Case(string Enc, int N, int Away, string Mode, int Seed);
internal sealed record Result(string Kind, string Fingerprint, string? Message = null);
