using System.Collections.Immutable;
using Tactics.Sim;

namespace Tactics.Logic.Tests;

public sealed class TargetReportTests
{
    [Fact]
    public void Bands_use_normalized_damage_while_the_audit_table_retains_raw_damage()
    {
        var battle = new SimBattle("test", 1, ["monk", "sorcerer"], 1, BattleOutcome.Won, 10, 0, 0,
            Ordinal.Map(("monk", new ClassStats(10, 0, 0, 0, 10)), ("sorcerer", new ClassStats(100, 0, 0, 0, 10))),
            ImmutableSortedDictionary<AbilityId, (int Used, int Legal)>.Empty, 0);
        var config = new SimConfig(["test"], [1], 1, [2], 1, Difficulty.Normal);
        var report = Report.Markdown([battle], config);
        Assert.Contains("| 1 | Striker | damage / target-turn | sorcerer | 10.0 | 10.0 |", report);
        Assert.Contains("| sorcerer | Striker | 1 | 1 | 100.0 | 10.0 |", report);
        var header = report.Split('\n').Single(l => l.StartsWith("| Class | Band | Level |", StringComparison.Ordinal));
        var separator = report.Split('\n')[Array.IndexOf(report.Split('\n'), header) + 1];
        Assert.Equal(header.Count(c => c == '|'), separator.Count(c => c == '|'));
    }
}
