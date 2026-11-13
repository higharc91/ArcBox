namespace Meridian.Logic.Tests.Turns;

public sealed class IncidentTests
{
    [Fact]
    public void Revealing_an_incident_compartment_draws_and_starts_its_check()
    {
        var game = Play.Game().Put(Play.S1, Play.C(2), moves: 4).Pile(CardKind.Incident, ["power_flicker"]);
        game.State = game.State with { TilePile = [new TileId("quarters")] };
        game.Do(new Open(Play.S1, Play.C(2), Side.E));
        game.Do(new Orient(Play.S1, 1));
        Assert.Contains(new CardDrawn(Play.S1, new CardId("power_flicker"), CardKind.Incident), game.Events);
        Assert.Equal(new CardId("power_flicker"), Assert.IsType<RollPending>(game.State.Waiting).Card);
        Assert.Empty(game.State.Incidents.Draw);
    }

    [Fact]
    public void A_choice_card_waits_for_an_option()
    {
        var game = Play.Game().Pile(CardKind.Incident, ["strange_signal"]).Draw(CardKind.Incident);
        Assert.IsType<ChoicePending>(game.State.Waiting);
        Assert.Equal([new Choose(Play.S1, "follow"), new Choose(Play.S1, "ignore"), new Timeout(Play.S1)], game.Legal(Play.S1));
    }

    [Fact]
    public void An_option_without_a_check_applies_and_discards()
    {
        var game = Play.Game().Pile(CardKind.Incident, ["strange_signal"]).Draw(CardKind.Incident);
        game.Do(new Choose(Play.S1, "ignore"));
        Assert.Equal(5, game.Crew(Play.S1).Steps.Tech);   // gain 1 mental: Tech and Nerve tie, so Tech
        Assert.Contains(new CardId("strange_signal"), game.State.Incidents.Discard);
        Assert.Null(game.State.Waiting);
    }

    [Fact]
    public void An_option_with_a_check_applies_its_effects_then_rolls()
    {
        var game = Play.Game().Put(Play.S1, Play.C(1), moves: 2).Pile(CardKind.Incident, ["strange_signal"]).Draw(CardKind.Incident);
        game.Do(new Choose(Play.S1, "follow"));
        Assert.Equal(3, game.State.MovesLeft);
        var roll = Assert.IsType<RollPending>(game.State.Waiting);
        Assert.Equal("follow", roll.Option);
        Assert.DoesNotContain(new CardId("strange_signal"), game.State.Incidents.Discard);
        game.Do(new Roll(Play.S1));
        Assert.Contains(new CardId("strange_signal"), game.State.Incidents.Discard);
    }

    [Fact]
    public void A_choice_timeout_takes_the_first_option()
    {
        var game = Play.Game().Pile(CardKind.Incident, ["strange_signal"]).Draw(CardKind.Incident);
        game.Do(new Timeout(Play.S1));
        Assert.Contains(game.Events, e => e is ChoiceMade { Option: "follow" });
    }

    [Fact]
    public void A_draw_effect_can_reshuffle_the_card_that_caused_it()
    {
        // Jammed Panel's high band draws Salvage; the incident is already in its discard by then.
        var game = Play.Game().Pile(CardKind.Salvage, ["pry_bar"]).Check(Play.S1, "jammed_panel");
        game.Rig(game.Dice, t => t >= 2).Do(new Roll(Play.S1));
        Assert.Contains(new CardId("pry_bar"), game.Crew(Play.S1).Items);
        Assert.Contains(new CardId("jammed_panel"), game.State.Incidents.Discard);
    }
}
