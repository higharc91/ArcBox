namespace Meridian.Logic.Tests.Turns;

public sealed class TraitTests
{
    private static readonly Roll Roll1 = new(Play.S1);

    /// <summary>Jammed Panel's low band: lose 2 physical, then end the turn.</summary>
    private static Play LowPanel(TraitSteps steps)
    {
        var game = Play.Game().Steps(Play.S1, steps).Check(Play.S1, "jammed_panel");
        return game.Rig(game.Dice, t => t <= 1);
    }

    [Fact]
    public void A_loss_spreads_across_the_group()
    {
        var game = LowPanel(new TraitSteps(4, 4, 4, 4));
        game.Do(Roll1);
        Assert.Equal(new TraitSteps(3, 3, 4, 4), game.Crew(Play.S1).Steps);
        Assert.Contains(new TraitChanged(Play.S1, Trait.Brawn, 4, 3), game.Events);
        Assert.Contains(new TraitChanged(Play.S1, Trait.Agility, 4, 3), game.Events);
    }

    [Fact]
    public void A_loss_comes_off_the_higher_trait_first()
    {
        var game = LowPanel(new TraitSteps(2, 6, 4, 4));
        game.Do(Roll1);
        Assert.Equal(new TraitSteps(2, 4, 4, 4), game.Crew(Play.S1).Steps);
    }

    [Fact]
    public void A_loss_at_step_one_before_the_breach_is_dropped()
    {
        // Review focus 3: the markers stay on step 1 and the loss isn't remembered.
        var game = LowPanel(new TraitSteps(1, 1, 4, 4));
        game.Do(Roll1);
        Assert.Equal(new TraitSteps(1, 1, 4, 4), game.Crew(Play.S1).Steps);
        Assert.DoesNotContain(game.Events, e => e is TraitChanged);
    }

    [Fact]
    public void An_end_turn_effect_ends_the_turn()
    {
        var game = LowPanel(new TraitSteps(4, 4, 4, 4));
        game.Do(Roll1);
        Assert.Equal(Play.S2, game.State.ActiveSeat);
        Assert.IsType<TurnEnded>(game.Events[^2]);
    }

    [Fact]
    public void A_gain_stops_at_the_top_step()
    {
        var game = Play.Game().Steps(Play.S1, new TraitSteps(4, 4, 4, 8)).Check(Play.S1, "power_flicker");
        game.Rig(game.Dice, t => t >= 4).Do(Roll1);
        Assert.Equal(8, game.Crew(Play.S1).Steps.Nerve);
        Assert.DoesNotContain(game.Events, e => e is TraitChanged);
    }

    [Fact]
    public void Values_follow_the_crews_own_track()
    {
        var game = Play.Game().Steps(Play.S2, new TraitSteps(1, 8, 4, 4));
        Assert.Equal(1, Rules.Value(game.Pack, game.Crew(Play.S2), Trait.Brawn));
        Assert.Equal(7, Rules.Value(game.Pack, game.Crew(Play.S2), Trait.Agility));
    }
}
