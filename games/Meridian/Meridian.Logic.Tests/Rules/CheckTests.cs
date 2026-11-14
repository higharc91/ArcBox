namespace Meridian.Logic.Tests.Turns;

// s1 is Ivo: every trait on step 4, so Brawn 3, Agility 3, Tech 4, Nerve 3.
public sealed class CheckTests
{
    private static readonly Roll Roll1 = new(Play.S1);

    [Fact]
    public void A_check_rolls_the_traits_value_in_dice()
    {
        var game = Play.Game().Check(Play.S1, "power_flicker");
        var wait = Assert.IsType<RollPending>(game.State.Waiting);
        Assert.Equal(3, wait.Dice);
        Assert.Equal(Trait.Nerve, wait.Trait);
        Assert.Equal([Roll1, new Timeout(Play.S1)], game.Legal(Play.S1));
    }

    [Fact]
    public void Carried_items_add_their_extra_dice()
        => Assert.Equal(5, Play.Game().Give(Play.S1, "humming_shard").Check(Play.S1, "jammed_panel").Dice);

    [Fact]
    public void Bands_pick_by_total()
    {
        var low = Play.Game().Check(Play.S1, "power_flicker");
        low.Rig(low.Dice, t => t <= 1).Do(Roll1);
        Assert.Contains(low.Events, e => e is BandResolved { Band: 0, Text: "You flinch." });

        var high = Play.Game().Check(Play.S1, "power_flicker");
        high.Rig(high.Dice, t => t >= 4).Do(Roll1);
        Assert.Contains(high.Events, e => e is BandResolved { Band: 2 });
        Assert.Equal(5, high.Crew(Play.S1).Steps.Nerve);
    }

    [Fact]
    public void The_roll_is_reported_with_its_faces()
    {
        var game = Play.Game().Check(Play.S1, "power_flicker");
        game.Do(Roll1);
        var rolled = Assert.Single(game.Events.OfType<Rolled>());
        Assert.Equal(3, rolled.Dice.Faces.Count);
        Assert.Null(game.State.Waiting);
    }

    [Fact]
    public void The_incident_is_discarded_once_it_resolves()
    {
        var game = Play.Game().Check(Play.S1, "power_flicker");
        Assert.DoesNotContain(new CardId("power_flicker"), game.State.Incidents.Discard);
        game.Do(Roll1);
        Assert.Contains(new CardId("power_flicker"), game.State.Incidents.Discard);
    }

    [Fact]
    public void Timeout_rolls_for_you()
    {
        var game = Play.Game().Check(Play.S1, "power_flicker");
        game.Do(new Timeout(Play.S1));
        Assert.Single(game.Events.OfType<Rolled>());
    }

    [Fact]
    public void A_check_on_a_choice_option_uses_that_options_bands()
    {
        var game = Play.Game().Put(Play.S1, Play.C(1)).Check(Play.S1, "strange_signal", "follow");
        game.Rig(game.Dice, t => t >= 3).Do(Roll1);
        Assert.Equal(Play.C(3), game.Crew(Play.S1).At);   // "moveTo central_lift", at no cost
        Assert.Contains(new Moved(Play.S1, Play.C(1), Play.C(3), 0), game.Events);
    }

    [Fact]
    public void Saved_while_a_roll_is_pending_then_restored_rolls_the_same_faces()
    {
        // Review focus 6.
        var game = Play.Game().Check(Play.S1, "power_flicker");
        var a = Rules.Step(game.Pack, game.State, Roll1);
        var b = Rules.Step(game.Pack, Json.RoundTrip(game.State), Roll1);
        Assert.Equal(a.State, b.State);
        Assert.Equal(a.Events, b.Events);
    }
}
