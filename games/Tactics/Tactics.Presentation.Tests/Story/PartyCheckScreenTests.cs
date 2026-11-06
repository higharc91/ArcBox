using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Story;

namespace Tactics.Presentation.Tests.Story;

public sealed class PartyCheckScreenTests
{
    private static StoryCheckView Basis => new("stealth", 12, "Cross together",
        [new("a", "Ana", "Ana", null, 2, "DEX 14", true, 0), new("b", "Bo", "Bo", null, 0, "DEX 10", true, 0)], null, null);
    private static CheckPanel Panel(StoryCheckView check) => StoryScreen.Of(new StoryView(1, "test", "test", TacticsGame.StoryStates.Rolled, null, null, check, null)).Check!;

    [Fact]
    public void The_TV_names_the_helper_and_advantage()
    {
        var panel = Panel(Basis with { Helper = "Bo", Chosen = "a", Roll = Rolls.Flat(16, 2, 12) });
        Assert.Contains("Bo helps", panel.Footer);
        Assert.Contains("advantage", panel.Footer);
        Assert.NotNull(panel.Roll);
    }

    [Fact]
    public void The_TV_shows_the_group_tally_and_each_d20_without_a_fake_aggregate_die()
    {
        var panel = Panel(Basis with { GroupCheck = true, Group = [new("a", 18, 2, true), new("b", 3, 0, false)], Roll = Rolls.Flat(1, 0, 1) });
        Assert.Equal("Group check", panel.Heading);
        Assert.Contains("1 of 2 succeeded", panel.Footer);
        Assert.Contains("Success", panel.Footer);
        Assert.Contains("Rolled 18", panel.Cards[0].Note);
        Assert.Contains("Rolled 3", panel.Cards[1].Note);
        Assert.Null(panel.Roll);
    }
}
