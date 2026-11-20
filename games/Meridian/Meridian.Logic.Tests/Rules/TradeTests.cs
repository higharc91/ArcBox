namespace Meridian.Logic.Tests.Turns;

public sealed class TradeTests
{
    private static readonly CardId Bar = new("pry_bar"), Torch = new("torch"), Stim = new("stim_pack");

    /// <summary>s1 (active) holds the pry bar and s2 the torch, both in the docking ring; s3 is in the airlock.</summary>
    private static Play Together()
        => Play.Game().Put(Play.S1, Play.C(2)).Put(Play.S2, Play.C(2)).Give(Play.S1, "pry_bar").Give(Play.S2, "torch");

    private static OfferTrade Swap => new(Play.S1, Play.S2, new TradeOffer([Bar], [Torch]));

    [Fact]
    public void An_accepted_trade_swaps_the_items_and_the_turn_goes_on()
    {
        var game = Together();
        game.Do(Swap);
        Assert.Equal(Play.S2, game.State.Waiting!.From);
        Assert.Equal([new AnswerTrade(Play.S2, true), new AnswerTrade(Play.S2, false), new Timeout(Play.S2)], game.Legal(Play.S2));
        Assert.Empty(game.Legal(Play.S1));
        game.Do(new AnswerTrade(Play.S2, true));
        Assert.Equal([Torch], game.Crew(Play.S1).Items);
        Assert.Equal([Bar], game.Crew(Play.S2).Items);
        Assert.Equal(Play.S1, game.State.ActiveSeat);
        Assert.Contains(new TradeAnswered(Play.S1, Play.S2, true), game.Events);
    }

    [Fact]
    public void A_declined_trade_changes_nothing()
    {
        var game = Together();
        game.Do(Swap);
        game.Do(new AnswerTrade(Play.S2, false));
        Assert.Equal([Bar], game.Crew(Play.S1).Items);
        Assert.Contains(new TradeAnswered(Play.S1, Play.S2, false), game.Events);
    }

    [Fact]
    public void A_timeout_declines()
    {
        var game = Together();
        game.Do(Swap);
        game.Do(new Timeout(Play.S2));
        Assert.Equal([Torch], game.Crew(Play.S2).Items);
        Assert.Null(game.State.Waiting);
    }

    [Fact]
    public void Only_someone_in_the_same_compartment_can_trade()
    {
        var game = Together().Give(Play.S3, "stim_pack");
        var result = game.Try(new OfferTrade(Play.S1, Play.S3, new TradeOffer([], [Stim])));
        Assert.Contains("same compartment", Assert.IsType<Rejected>(Assert.Single(result.Events)).Reason);
    }

    [Fact]
    public void An_item_not_carried_cannot_be_offered()
    {
        var result = Together().Try(new OfferTrade(Play.S1, Play.S2, new TradeOffer([Stim], [])));
        Assert.IsType<Rejected>(Assert.Single(result.Events));
    }

    [Fact]
    public void One_offer_per_turn()
    {
        var game = Together();
        game.Do(Swap);
        game.Do(new AnswerTrade(Play.S2, false));
        Assert.Contains("already traded", Assert.IsType<Rejected>(Assert.Single(game.Try(Swap).Events)).Reason);
        Assert.Empty(Rules.TradeOffers(game.State, Play.S1));
    }

    [Fact]
    public void Trade_offers_lists_gives_swaps_and_asks()
    {
        var offers = Rules.TradeOffers(Together().State, Play.S1).ToList();
        Assert.Equal(3, offers.Count);
        Assert.Contains(Swap, offers);
    }

    [Fact]
    public void Saved_while_a_trade_is_pending_then_restored_answers_the_same()
    {
        // Review focus 6.
        var game = Together();
        game.Do(Swap);
        var a = Rules.Step(game.Pack, game.State, new AnswerTrade(Play.S2, true));
        var b = Rules.Step(game.Pack, Json.RoundTrip(game.State), new AnswerTrade(Play.S2, true));
        Assert.Equal(a.State, b.State);
    }
}
