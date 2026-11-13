namespace Meridian.Logic.Tests.Turns;

public sealed class DeckTests
{
    [Fact]
    public void An_empty_pile_reshuffles_its_discard()
    {
        // Review focus 4.
        var game = Play.Game().Pile(CardKind.Salvage, [], "pry_bar", "torch").Draw(CardKind.Salvage);
        Assert.Contains(new DeckReshuffled(CardKind.Salvage), game.Events);
        Assert.Single(game.Crew(Play.S1).Items);
        Assert.Single(game.State.Salvage.Draw);
        Assert.Empty(game.State.Salvage.Discard);
    }

    [Fact]
    public void An_empty_pile_with_nothing_to_reshuffle_draws_nothing_and_play_goes_on()
    {
        var game = Play.Game().Pile(CardKind.Incident, []).Draw(CardKind.Incident);
        Assert.Equal([new DeckEmpty(CardKind.Incident)], game.Events);
        Assert.Null(game.State.Waiting);
        Assert.Contains(new EndTurn(Play.S1), game.Legal(Play.S1));
    }

    [Fact]
    public void Anomalies_never_reshuffle()
    {
        var game = Play.Game().Pile(CardKind.Anomaly, [], "humming_shard").Draw(CardKind.Anomaly);
        Assert.Equal([new DeckEmpty(CardKind.Anomaly)], game.Events);
    }

    [Fact]
    public void Drawing_an_anomaly_holds_it_and_counts_it()
    {
        var game = Play.Game().Pile(CardKind.Anomaly, ["humming_shard"]).Draw(CardKind.Anomaly);
        Assert.Equal([new CardId("humming_shard")], game.Crew(Play.S1).Items);
        Assert.Equal(1, game.State.AnomaliesDrawn);
    }
}
