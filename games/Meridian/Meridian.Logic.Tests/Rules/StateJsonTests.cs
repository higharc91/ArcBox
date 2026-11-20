namespace Meridian.Logic.Tests.Turns;

public sealed class StateJsonTests
{
    [Fact]
    public void State_round_trips_through_json_with_value_equality()
    {
        var game = Play.Game();
        game.Do(new EndTurn(Play.S1));
        var back = Json.RoundTrip(game.State);
        Assert.Equal(game.State, back);
        Assert.Equal(game.State.GetHashCode(), back.GetHashCode());
    }

    [Fact]
    public void Every_wait_round_trips_as_its_own_kind()
    {
        Pending[] waits =
        [
            new OrientPending(Play.S1, new TileId("lab"), new Cell(DeckId.Command, 1, 0), Side.W, [1, 2]),
            new RollPending(Play.S1, RollReason.Check, Trait.Nerve, 3, new CardId("power_flicker")),
            new ChoicePending(Play.S1, new CardId("x")),
            new TradePending(Play.S2, Play.S1, new TradeOffer([new CardId("pry_bar")], [])),
        ];
        foreach (var wait in waits)
        {
            var state = Play.Game().State with { Waiting = wait };
            Assert.Equal(state, Json.RoundTrip(state));
        }
    }

    [Fact]
    public void Commands_and_events_round_trip()
    {
        Command[] commands = [new MoveTo(Play.S1, Play.C(2)), new Open(Play.S1, Play.C(2), Side.E), new Timeout(Play.S1)];
        foreach (var command in commands)
        {
            Assert.Equal(command, Json.RoundTrip(command));
        }
        GameEvent e = new Rolled(Play.S1, RollReason.Breach, null, new DiceRoll([1, 2]));
        Assert.Equal(e, Json.RoundTrip(e));
        Assert.StartsWith("{\"kind\":\"rolled\"", Json.Write(e));
    }

    [Fact]
    public void No_polymorphic_type_has_a_property_named_kind()
    {
        // "kind" is the discriminator for commands, events, waits, effects and tile rules.
        Type[] bases = [typeof(Command), typeof(GameEvent), typeof(Pending), typeof(Effect), typeof(TileRule)];
        var offenders = typeof(GameState).Assembly.GetTypes()
            .Where(t => bases.Any(b => b.IsAssignableFrom(t)))
            .Where(t => t.GetProperties().Any(p => string.Equals(p.Name, "kind", StringComparison.OrdinalIgnoreCase)))
            .Select(t => t.Name);
        Assert.Empty(offenders);
    }
}
