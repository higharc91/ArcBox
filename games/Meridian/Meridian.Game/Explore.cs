using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

// Explore: commands through the rules, and the M2 ending at the Breach (M2 plan, Task 2).
public sealed partial class MeridianGame
{
    /// <summary>
    /// Runs a command through <see cref="Rules.Step"/>. A rejected command changes nothing, not even the session's
    /// identity, so the phone keeps its view and its viewId. Returns whether it applied.
    /// </summary>
    private bool Apply(Command command)
    {
        var result = Rules.Step(_pack, Session.Game!, command);
        if (result.Events.FirstOrDefault() is Rejected)
        {
            return false;
        }

        Session = Session with { Game = result.State };
        Record(result.Events);
        Present(result.Events);   // the Breach is always presented, so the game ends once it has been shown (Pop)
        Rearm(fresh: true);
        return true;
    }

    /// <summary>
    /// The M2 ending (M2 plan, Task 2): scenarios are stage M3, so the Breach ends the game with a message, every seat
    /// scored 0, and the room goes back to the lobby.
    /// </summary>
    private void End()
    {
        var game = Session.Game!;
        var breach = game.Breach!;
        var ending = breach is { Anomaly: not null, Compartment: { } at }
            ? $"The Breach fired in the {_pack.Tile(game.Ship.Get(at).Tile).Name} after {game.AnomaliesDrawn} Anomalies. Scenarios arrive in stage M3."
            : "The ship had nowhere left to explore, and the Breach fired. Scenarios arrive in stage M3.";
        Session = Session with { Phase = SessionPhase.Over, Ending = ending };
        Rearm();
        _ctx.EndGame(new GameResults(Session.Seats.Select(s => new PlayerScore(s.Player, s.Name, 0)).ToList(), ending));
    }

    /// <summary>Keeps the last eight lines of what happened, for the TV's log.</summary>
    private void Record(EqArray<GameEvent> events)
    {
        var lines = events.Select(Line).OfType<string>().ToList();
        if (lines.Count > 0)
        {
            Session = Session with { Log = Session.Log.AddRange(lines).Items.TakeLast(8).ToEqArray() };
        }
    }

    private string? Line(GameEvent e)
    {
        string Name(SeatId seat) => Session.SeatOf(seat).Name;
        return e switch
        {
            CompartmentRevealed r => $"{Name(r.Seat)} found the {_pack.Tile(r.Compartment.Tile).Name}.",
            HatchSealed => "A hatch is welded shut.",
            CardDrawn d => $"{Name(d.Seat)} drew {_pack.Card(d.Card).Title}.",
            BandResolved b => $"{Name(b.Seat)}: {b.Text}",
            ChoiceMade c => $"{Name(c.Seat)}: {c.Text}",
            ItemUsed u => $"{Name(u.Seat)} used {_pack.Card(u.Card).Title}.",
            TradeAnswered { Accepted: true } t => $"{Name(t.From)} and {Name(t.To)} traded.",
            BreachHeld h => $"{Name(h.Seat)} rolled {h.Total} of {h.Needed}. The ship holds.",
            BreachFired => "The Breach.",
            _ => null,
        };
    }
}
