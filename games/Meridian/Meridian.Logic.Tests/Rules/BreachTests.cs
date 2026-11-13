namespace Meridian.Logic.Tests.Turns;

public sealed class BreachTests
{
    private static Play Drawing(int alreadyDrawn)
    {
        var game = Play.Game().Pile(CardKind.Anomaly, ["humming_shard"]);
        game.State = game.State with { AnomaliesDrawn = alreadyDrawn };
        return game.Draw(CardKind.Anomaly);
    }

    [Fact]
    public void The_roll_uses_as_many_dice_as_anomalies_drawn()
    {
        var roll = Assert.IsType<RollPending>(Drawing(3).State.Waiting);
        Assert.Equal(RollReason.Breach, roll.Reason);
        Assert.Equal(4, roll.Dice);
        Assert.Null(roll.Trait);
    }

    [Fact]
    public void Below_the_threshold_play_continues()
    {
        var game = Drawing(0);
        game.Do(new Roll(Play.S1));   // one die can't reach 5
        Assert.Contains(new BreachHeld(Play.S1, game.Events.OfType<Rolled>().Single().Dice.Total, 5), game.Events);
        Assert.Equal(Phase.Explore, game.State.Phase);
        Assert.Contains(new EndTurn(Play.S1), game.Legal(Play.S1));
    }

    [Fact]
    public void At_the_threshold_the_breach_fires_and_records_who_what_and_where()
    {
        var game = Drawing(5).Put(Play.S1, Play.C(2));
        game.Rig(6, t => t >= 5).Do(new Roll(Play.S1));
        Assert.Equal(Phase.Breach, game.State.Phase);
        var breach = game.State.Breach!;
        Assert.Equal(Play.S1, breach.Seat);
        Assert.Equal(new CardId("humming_shard"), breach.Anomaly);
        Assert.Equal(Play.C(2), breach.Compartment);
        Assert.False(breach.Forced);
        Assert.True(breach.Roll >= 5);
        Assert.Equal(new BreachFired(breach), game.Events[^1]);
    }

    [Fact]
    public void The_ninth_anomaly_fires_without_a_roll()
    {
        // Review focus 8.
        var game = Drawing(8);
        Assert.Equal(Phase.Breach, game.State.Phase);
        Assert.True(game.State.Breach!.Forced);
        Assert.Null(game.State.Breach.Roll);
        Assert.DoesNotContain(game.Events, e => e is Rolled);
    }

    [Fact]
    public void Nothing_is_legal_after_the_breach()
    {
        var game = Drawing(8);
        Assert.All(new[] { Play.S1, Play.S2, Play.S3 }, s => Assert.Empty(game.Legal(s)));
        Assert.IsType<Rejected>(Assert.Single(game.Try(new EndTurn(Play.S1)).Events));
    }

    [Fact]
    public void A_ship_with_nowhere_left_to_grow_fires_the_breach()
    {
        var game = Play.Game();
        game.State = game.State with { TilePile = [] };
        game.Do(new EndTurn(Play.S1));
        Assert.Equal(Phase.Breach, game.State.Phase);
        Assert.Equal(new BreachInfo(Play.S2, null, null, Forced: true, Roll: null), game.State.Breach);
    }

    [Fact]
    public void Tiles_for_a_deck_that_still_has_a_frontier_keep_exploring()
    {
        var game = Play.Game();
        game.State = game.State with { TilePile = [new TileId("lab")] };   // Command only; the landing has open hatches
        game.Do(new EndTurn(Play.S1));
        Assert.Equal(Phase.Explore, game.State.Phase);
    }
}
