using System.Collections.Immutable;
using Tactics.Sim;

namespace Tactics.Logic.Tests;

public sealed class FormationTests
{
    private static readonly ImmutableArray<string> Members = ["cleric", "fighter", "rogue", "wizard"];

    [Fact]
    public void Every_member_gets_every_spawn_seat_over_a_full_seed_cycle()
    {
        var formations = Enumerable.Range(1, Members.Length).Select(seed => SimRunner.Formation(Members, (ulong)seed, true)).ToArray();
        for (var seat = 0; seat < Members.Length; seat++)
            Assert.Equal(Members.Order(StringComparer.Ordinal), formations.Select(f => f[seat]).Order(StringComparer.Ordinal));
        Assert.Equal(Members, SimRunner.Formation(Members, 1, true));
    }

    [Fact]
    public void Legacy_runs_keep_the_same_formation_for_every_seed()
    {
        foreach (var seed in Enumerable.Range(1, 20))
            Assert.Equal(Members, SimRunner.Formation(Members, (ulong)seed, false));
    }
}
