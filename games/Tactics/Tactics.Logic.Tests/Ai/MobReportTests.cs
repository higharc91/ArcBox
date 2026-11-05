using System.Collections.Immutable;
using Tactics.Sim;

namespace Tactics.Logic.Tests;

public sealed class MobReportTests
{
    private static SimBattle Battle(int level, int rounds = 4, bool capped = false) => new(
        "mob_test", level, ["fighter"], 1, capped ? BattleOutcome.Lost : BattleOutcome.Won, rounds, 0, 0,
        ImmutableSortedDictionary<string, ClassStats>.Empty,
        ImmutableSortedDictionary<AbilityId, (int Used, int Legal)>.Empty, 0, capped);

    [Fact]
    public void A_level_one_fight_is_assessed_against_the_arrival_target()
    {
        var report = MobReport.Markdown([Battle(1, 3)]);
        Assert.Contains("easier than target; short", report);
    }

    [Fact]
    public void Leveling_up_is_reported_as_progression_instead_of_scaling_the_target()
    {
        var report = MobReport.Markdown([Battle(2, 2), Battle(3, 1)]);
        Assert.Contains("progression check (fixed enemies)", report);
        Assert.DoesNotContain("easier than target", report);
        Assert.DoesNotContain("short", report);
    }

    [Fact]
    public void A_stalled_progression_fight_is_still_flagged()
        => Assert.Contains("progression check (fixed enemies); round cap", MobReport.Markdown([Battle(3, 30, capped: true)]));
}
