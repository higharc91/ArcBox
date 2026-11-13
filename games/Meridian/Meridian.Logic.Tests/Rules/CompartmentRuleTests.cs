namespace Meridian.Logic.Tests.Turns;

public sealed class CompartmentRuleTests
{
    private static (Play Game, Compartment MedBay) InMedBay(TraitSteps steps)
    {
        var game = Play.Game();
        var medBay = game.Build("med_bay", new Cell(DeckId.Habitat, 1, 1), 1);
        game.Put(Play.S1, medBay.Id).Put(Play.S2, medBay.Id).Steps(Play.S1, steps).Steps(Play.S2, steps);
        return (game, medBay);
    }

    [Fact]
    public void A_heal_restores_the_lowest_trait_below_its_start()
    {
        var (game, medBay) = InMedBay(new TraitSteps(2, 3, 4, 4));
        game.Do(new UseCompartment(Play.S1));
        Assert.Equal(new TraitSteps(3, 3, 4, 4), game.Crew(Play.S1).Steps);
        Assert.Contains(new CompartmentUsed(Play.S1, medBay.Id), game.Events);
    }

    [Fact]
    public void A_heal_is_not_offered_with_nothing_to_heal()
        => Assert.DoesNotContain(new UseCompartment(Play.S1), InMedBay(new TraitSteps(4, 4, 2, 2)).Game.Legal(Play.S1));

    [Fact]
    public void A_once_per_crew_rule_can_be_used_by_each_crew_member_once()
    {
        var (game, _) = InMedBay(new TraitSteps(1, 1, 4, 4));
        game.Do(new UseCompartment(Play.S1));
        game.Do(new EndTurn(Play.S1));
        game.Do(new UseCompartment(Play.S2));
        game.Do(new EndTurn(Play.S2));
        game.Do(new EndTurn(Play.S3));
        Assert.DoesNotContain(new UseCompartment(Play.S1), game.Legal(Play.S1));
        Assert.Equal(new TraitSteps(2, 1, 4, 4), game.Crew(Play.S1).Steps);
    }
}
