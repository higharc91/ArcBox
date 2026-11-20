namespace Meridian.Logic.Tests.Turns;

public sealed class InvariantTests
{
    [Fact]
    public void A_new_game_has_no_violations()
        => Assert.Empty(Invariants.Check(BasePack.Pack, Rules.NewGame(BasePack.Pack, BotRunner.Seats(BasePack.Pack, 6), 1)));

    [Fact]
    public void A_lost_card_is_caught()
    {
        var game = Play.Game();
        game.State = game.State with { Salvage = new CardPile([]) };
        Assert.Contains("a card is missing or exists twice", Invariants.Check(game.Pack, game.State));
    }

    [Fact]
    public void A_duplicated_tile_is_caught()
    {
        var game = Play.Game();
        game.State = game.State with { TilePile = game.State.TilePile.Add(new TileId("junction")) };
        Assert.Contains("a tile is missing or exists twice", Invariants.Check(game.Pack, game.State));
    }

    [Fact]
    public void A_marker_off_its_track_is_caught()
    {
        var game = Play.Game().Steps(Play.S1, new TraitSteps(0, 4, 4, 4));
        Assert.Contains(Invariants.Check(game.Pack, game.State), e => e.Contains("marker"));
    }

    [Fact]
    public void An_unsealed_hatch_facing_a_wall_is_caught()
    {
        var game = Play.Game();
        game.Build("storage", new Cell(DeckId.Habitat, 1, 1));
        game.State = game.State with { Ship = game.State.Ship with { Sealed = [] }, TilePile = game.State.TilePile.Remove(new TileId("storage")) };
        Assert.Contains(Invariants.Check(game.Pack, game.State), e => e.Contains("faces a wall"));
    }
}
