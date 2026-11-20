namespace Meridian.Game.Tests;

public sealed class SessionJsonTests
{
    [Fact]
    public void Session_round_trips_through_json()
    {
        var game = new Harness().Board();
        game.PlayUntilPresenting();
        var session = game.Session;
        var back = Json.RoundTrip(session);
        Assert.Equal(session, back);
        Assert.NotEmpty(back.Queue);
    }

    [Fact]
    public void A_loaded_session_plays_on_identically()
    {
        var a = new Harness().Board();
        var b = new Harness().Board();
        b.Game.Load(Json.RoundTrip(a.Session));
        a.Send(a.Actor, "moveTo", new { to = "c2" });
        b.Send(b.Actor, "moveTo", new { to = "c2" });
        Assert.Equal(a.State, b.State);
    }
}
