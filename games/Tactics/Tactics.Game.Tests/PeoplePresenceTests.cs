using Tactics.Logic;
using static Tactics.Game.Tests.TacticsGameTests;
using static Tactics.Game.Tests.ExplorationTests;

namespace Tactics.Game.Tests;

public sealed class PeoplePresenceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void The_ford_people_only_leave_after_the_corresponding_battle_was_won(bool parley, bool won)
    {
        var flag = parley ? "ch1_skarrow_spared" : "ch1_skarrow_slain";
        var battle = parley ? "ch1_river_crossing" : "ch1_ford_assault";
        var world = Saved(Rules.RegionFor("ashen_ford")!.Def.Spawns.Start) with { Flags = [flag], Beats = won ? [battle] : [] };
        var session = new Session(document: DocumentAt(world));
        session.Harness.StartCampaign(session.Lease);
        var flags = (session.Host.Explore!.PeopleFlags ?? []).ToHashSet(StringComparer.Ordinal);
        var people = Rules.RegionFor("ashen_ford")!.Def.People.Where(p => RegionPeople.Present(p, flags)).ToArray();
        Assert.Equal(!won, people.Any(p => p.Preset is "goblin_archer" or "goblin_spear" or "goblin_cutthroat"));
        Assert.Equal(parley || !won, people.Any(p => p.Character == "mother_skarrow"));
        Assert.Contains(flag, session.Game.Document.World!.Flags);
    }
}
