using System.Collections.Immutable;
using System.Text;
using System.IO.Compression;
using System.Text.Json;
using Tactics.Logic;

namespace Tactics.Sim;

/// <summary>Controlled paired comparisons of real levelled builds, independent of the default party sampler.</summary>
public static class BuildAudit
{
    public sealed record Cell(string Kind, string Build, string Class, string Encounter, int Level,
        SimConfig Config, ImmutableArray<SimBattle> Battles, double WinRate, ClassStats? Focus);

    public static readonly ImmutableArray<(int Level, string Encounter)> Scenarios =
        [(3, "forest_ruins"), (5, "crypt_hall"), (7, "crystal_cave"), (10, "hall_of_the_flame")];

    public static ImmutableArray<string> PartyFor(string cls)
        => [cls, .. new[] { "fighter", "cleric", "rogue", "wizard" }.Where(c => c != cls).Take(3)];

    public static IEnumerable<(string Kind, string Build, string Class, int Level, SimConfig Config)> Schedule(RulesPack rules, int seeds, int cap)
    {
        var defaults = rules.Classes.Values.ToImmutableDictionary(c => c.Id, c => c.Subclasses[0].Id, StringComparer.Ordinal);
        foreach (var (level, encounter) in Scenarios)
        {
            foreach (var cls in rules.Classes.Values)
            {
                var party = PartyFor(cls.Id);
                // Other party members keep fixed, valid subclasses; the focus class is paired with its base-kit control.
                var control = new SimConfig([encounter], [level], 1, [4], seeds, Difficulty.Normal, cap,
                    Subclasses: defaults.Remove(cls.Id), Race: "human", FixedParty: party, RotateFormation: true);
                yield return ("class", cls.Id, cls.Id, level, control);
                foreach (var sub in cls.Subclasses)
                    yield return ("subclass", sub.Id, cls.Id, level, control with { Subclasses = defaults.SetItem(cls.Id, sub.Id) });
            }
            foreach (var race in rules.Races.Keys)
                yield return ("race", race, "", level,
                    new SimConfig([encounter], [level], 1, [4], seeds, Difficulty.Normal, cap,
                        Subclasses: defaults, Race: race, FixedParty: ["fighter", "cleric", "rogue", "wizard"], RotateFormation: true));
        }
    }

    public static double Measure(RoleBand band, ClassStats stats) => band switch
    {
        RoleBand.Defender => stats.MitigatedPerRound,
        RoleBand.Striker => stats.TargetDamagePerRound ?? stats.DamagePerRound,
        RoleBand.Healer => stats.HealingPerRound,
        RoleBand.Control => stats.TurnsDenied,
        _ => 0,
    };

    public static void Write(RulesPack rules, int seeds, int cap, string path)
    {
        if (seeds < 1 || cap < 1) throw new ArgumentOutOfRangeException(nameof(seeds));
        var cells = new List<Cell>();
        var completed = 0;
        foreach (var job in Schedule(rules, seeds, cap))
        {
            var battles = SimRunner.Run(job.Config, rules).ToImmutableArray();
            if (battles.Length != seeds) throw new InvalidOperationException($"Incomplete audit cell: {job.Build} at {job.Level}.");
            var stats = job.Class.Length == 0 ? null : new ClassStats(
                battles.Average(b => b.PerClass[job.Class].DamagePerRound), battles.Average(b => b.PerClass[job.Class].HealingPerRound),
                battles.Average(b => b.PerClass[job.Class].MitigatedPerRound), battles.Average(b => b.PerClass[job.Class].TurnsDenied),
                battles.Average(b => b.PerClass[job.Class].TargetDamagePerRound ?? b.PerClass[job.Class].DamagePerRound));
            cells.Add(new(job.Kind, job.Build, job.Class, job.Config.Encounters[0], job.Level, job.Config, battles,
                battles.Count(b => b.Outcome == BattleOutcome.Won) * 100.0 / battles.Length, stats));
            Console.Error.WriteLine($"BUILD AUDIT {++completed}: {job.Kind} {job.Build} L{job.Level}; {battles.Length} battles");
        }
        var text = new StringBuilder("# Representative build audit — #119\n\n");
        text.AppendLine($"Content hash: `{rules.Hash}`. {cells.Sum(c => c.Battles.Length)} deterministic battles; seeds 1–{seeds}, four heroes, Normal, cap {cap}. Stat increases are spent by class priority at levels 4/8, capped at 20. Spawn seats rotate over paired seeds. No consumables are used.");
        text.AppendLine("\nThese are paired benchmark scenarios, not a certification of every party composition. The subclass control changes only the focus class's subclass; its three teammates retain their fixed subclasses. Race cells change all four heroes to the measured race, with identical classes, subclasses and seeds. Mixed-race parties are supported separately by --races class:race pairs.\n");
        text.AppendLine("| Class | Subclass | Level / encounter | Win % | Base-kit win % | Role measure | Base role | Change | Review |\n|---|---|---|---:|---:|---:|---:|---:|---|");
        foreach (var cell in cells.Where(c => c.Kind == "subclass"))
        {
            var baseline = cells.Single(c => c.Kind == "class" && c.Class == cell.Class && c.Level == cell.Level);
            var band = SimRunner.BandOf(cell.Class);
            var actual = Measure(band, cell.Focus!); var original = Measure(band, baseline.Focus!);
            var ratio = original == 0 ? (actual == 0 ? 0 : double.PositiveInfinity) : (actual - original) / original * 100;
            var flagged = double.IsInfinity(ratio) || Math.Abs(ratio) > 25;
            text.AppendLine($"| {cell.Class} | {cell.Build} | {cell.Level} / {cell.Encounter} | {cell.WinRate:F1} | {baseline.WinRate:F1} | {actual:F2} | {original:F2} | {(double.IsInfinity(ratio) ? "new capability" : ratio.ToString("+0.0;-0.0;0.0") + "%")} | {(flagged ? "OUTLIER — retained for review in #118/#120/#121; a new role capability can have a zero base denominator" : "within ±25%") } |");
        }
        text.AppendLine("\nRole measures match the simulator: defender damage mitigated/round, striker mean target damage/round, healer HP restored/round, controller turns denied. Outliers are documented rather than silently tuned; class identity gaps and racial balance remain tracked in #118/#120/#121/#125. Win rates from twenty seeds are descriptive, not precise estimates.\n");
        text.AppendLine("| Race | Level / encounter | Win % | Human win % | Delta (points) | Review |\n|---|---|---:|---:|---:|---|");
        foreach (var cell in cells.Where(c => c.Kind == "race"))
        {
            var human = cells.Single(c => c.Kind == "race" && c.Build == "human" && c.Level == cell.Level);
            var delta = cell.WinRate - human.WinRate;
            text.AppendLine($"| {cell.Build} | {cell.Level} / {cell.Encounter} | {cell.WinRate:F1} | {human.WinRate:F1} | {delta:+0.0;-0.0;0.0} | {(Math.Abs(delta) > 5 ? "OUTLIER — #125" : "within ±5 points")} |");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, text.ToString());
        File.WriteAllText(Path.ChangeExtension(path, ".json"), JsonSerializer.Serialize(new { ContentHash = rules.Hash, Seeds = seeds,
            Cells = cells.Select(c => new { c.Kind, c.Build, c.Class, c.Encounter, c.Level, c.Config, c.WinRate, c.Focus,
                Battles = c.Battles.Length, Wins = c.Battles.Count(b => b.Outcome == BattleOutcome.Won), Capped = c.Battles.Count(b => b.Capped) }) },
            new JsonSerializerOptions { WriteIndented = true }));
        using var raw = File.Create(Path.ChangeExtension(path, ".battles.jsonl.gz"));
        using var zip = new GZipStream(raw, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(zip);
        foreach (var cell in cells)
            foreach (var battle in cell.Battles)
                writer.WriteLine(JsonSerializer.Serialize(new { cell.Kind, cell.Build, cell.Level, Battle = battle }, new JsonSerializerOptions { IncludeFields = true }));
    }
}
