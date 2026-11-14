using System.Text.Json;

namespace Meridian.Game.Tests;

public sealed class ExploreTests
{
    private static readonly SeatId S = new("p1");

    private static Command? Parse(string action, object? data = null)
        => Intents.ToCommand(S, action, data is null ? default : JsonSerializer.SerializeToElement(data, ArcJson.Options));

    [Fact]
    public void Every_intent_maps_to_its_command()
    {
        Assert.Equal(new MoveTo(S, new("c4")), Parse("moveTo", new { to = "c4" }));
        Assert.Equal(new Open(S, new("c2"), Side.E), Parse("open", new { at = "c2", side = "e" }));
        Assert.Equal(new Orient(S, 2), Parse("orient", new { rotation = 2 }));
        Assert.Equal(new Roll(S), Parse("roll"));
        Assert.Equal(new Choose(S, "search"), Parse("choose", new { option = "search" }));
        Assert.Equal(new UseItem(S, new("med_kit")), Parse("useItem", new { item = "med_kit" }));
        Assert.Equal(new UseCompartment(S), Parse("useRoom"));
        Assert.Equal(new OfferTrade(S, new("p2"), new TradeOffer([new("crowbar")], [])),
                     Parse("offerTrade", new { to = "p2", give = new[] { "crowbar" }, take = Array.Empty<string>() }));
        Assert.Equal(new AnswerTrade(S, true), Parse("answerTrade", new { accept = true }));
        Assert.Equal(new EndTurn(S), Parse("endTurn"));
    }

    [Fact]
    public void Intents_round_trip_through_their_phone_form()
    {
        Command[] commands = [new MoveTo(S, new("c4")), new Open(S, new("c2"), Side.W), new Orient(S, 3), new Roll(S),
            new Choose(S, "x"), new UseItem(S, new("a")), new UseCompartment(S), new AnswerTrade(S, false), new EndTurn(S),
            new OfferTrade(S, new("p3"), new TradeOffer([], [new("b")]))];
        foreach (var command in commands)
        {
            var (action, data) = Intents.FromCommand(command);
            Assert.Equal(command, Parse(action, data));
        }
    }

    [Theory]
    [InlineData("moveTo")]
    [InlineData("open")]
    [InlineData("teleport")]
    public void An_unknown_or_incomplete_intent_is_no_command(string action)
        => Assert.Null(Parse(action, new { side = "up" }));

    [Fact]
    public void A_rejected_intent_changes_nothing()
    {
        var game = new Harness().Board();
        var before = game.Session;
        game.Send(game.Actor, "moveTo", new { to = "c99" });
        game.Send((game.Actor + 1) % 3, "endTurn");
        Assert.Same(before, game.Session);
    }

    [Fact]
    public void A_double_tap_applies_once()
    {
        // M2 review focus 2: the second endTurn comes from a seat that's no longer active.
        var game = new Harness().Board();
        var first = game.Actor;
        game.Send(first, "endTurn");
        game.Send(first, "endTurn");
        Assert.Equal((first + 1) % 3, game.Actor);
    }

    [Fact]
    public void A_move_goes_through_the_rules()
    {
        var game = new Harness().Board();
        var seat = game.State.ActiveSeat;
        game.Send(game.Actor, "moveTo", new { to = "c2" });
        Assert.Equal(new CompartmentId("c2"), game.State.CrewOf(seat).At);
    }

    [Fact]
    public void A_whole_game_ends_with_the_scenarios_outcome()
    {
        var game = new Harness().Board();
        game.PlayBots();
        Assert.Equal(SessionPhase.Over, game.Session.Phase);
        var results = Assert.IsType<GameResults>(game.H.Results);
        Assert.Equal(game.State.Board!.Outcome!.Message, results.Message);
        Assert.Equal(3, results.Scores.Count);
        Assert.Empty(game.H.ActiveTimers);
    }
}
