using System.Text.Json;
using Meridian.Logic;

namespace Meridian.Game;

// Boarding (spec §3.1): each phone picks a crew member; then the rules game starts.
public sealed partial class MeridianGame
{
    private void Board(SeatInfo seat, string action, JsonElement data)
    {
        if (action != "pick" || Intents.Text(data, "crew") is not { } id)
        {
            return;
        }
        var crew = new CrewId(id);
        if (!_pack.HasCrew(crew) || Session.Seats.Any(s => s.Crew == crew && s.Player != seat.Player))
        {
            return;
        }
        Session = Session.With(seat with { Crew = crew });
        if (Session.Seats.All(s => s.Crew is not null))
        {
            FinishBoarding();
        }
        else
        {
            Rearm();
        }
    }

    /// <summary>Gives every seat without a pick a free crew member, then starts the rules game in join order.</summary>
    private void FinishBoarding()
    {
        var rng = Session.BotRng;
        foreach (var seat in Session.Seats.Where(s => s.Crew is null).ToList())
        {
            var free = _pack.Crew.Select(c => c.Id).Where(c => Session.Seats.All(s => s.Crew != c)).ToList();
            (var i, rng) = rng.Below(free.Count);
            Session = Session.With(seat with { Crew = free[i] });
        }

        var seats = Session.Seats.Select(s => (s.Seat, s.Crew!.Value)).ToList();
        var game = Rules.NewGame(_pack, seats, Session.GameSeed);
        Session = Session with { Phase = SessionPhase.Explore, Game = game, BotRng = rng };
        Rearm();
    }
}
