namespace Meridian.Logic.Tests.Turns;

public sealed class SalvageTests
{
    [Fact]
    public void Revealing_a_salvage_compartment_gives_the_item()
    {
        var game = Play.Game().Put(Play.S1, Play.C(2), moves: 4).Pile(CardKind.Salvage, ["pry_bar"]);
        game.State = game.State with { TilePile = [new TileId("storage")] };
        game.Do(new Open(Play.S1, Play.C(2), Side.E));
        Assert.Equal([new CardId("pry_bar")], game.Crew(Play.S1).Items);
        Assert.Contains(new ItemGained(Play.S1, new CardId("pry_bar")), game.Events);
        Assert.Null(game.State.Waiting);
    }

    [Fact]
    public void A_single_use_item_applies_once_and_is_discarded()
    {
        var game = Play.Game().Steps(Play.S1, new TraitSteps(2, 2, 4, 4)).Give(Play.S1, "stim_pack");
        var events = game.Do(new UseItem(Play.S1, new CardId("stim_pack")));
        Assert.Equal(new TraitSteps(3, 3, 4, 4), game.Crew(Play.S1).Steps);
        Assert.Empty(game.Crew(Play.S1).Items);
        Assert.Contains(new CardId("stim_pack"), game.State.Salvage.Discard);
        Assert.Equal(new ItemUsed(Play.S1, new CardId("stim_pack"), true), events[0]);
    }

    [Fact]
    public void A_reusable_item_stays_and_one_item_is_used_per_turn()
    {
        var game = Play.Game().Give(Play.S1, "torch", "stim_pack");
        var moves = game.State.MovesLeft;
        game.Do(new UseItem(Play.S1, new CardId("torch")));
        Assert.Equal(moves + 1, game.State.MovesLeft);
        Assert.Contains(new CardId("torch"), game.Crew(Play.S1).Items);
        Assert.DoesNotContain(game.Legal(Play.S1), c => c is UseItem);
        game.Do(new EndTurn(Play.S1));
        game.Do(new EndTurn(Play.S2));
        game.Do(new EndTurn(Play.S3));
        Assert.Contains(new UseItem(Play.S1, new CardId("torch")), game.Legal(Play.S1));
    }

    [Fact]
    public void A_passive_only_item_is_never_offered_for_use()
        => Assert.DoesNotContain(Play.Game().Give(Play.S1, "pry_bar").Legal(Play.S1), c => c is UseItem);
}
