using ArcBox.Sdk;
using Meridian.Logic;

namespace Meridian.Game;

// The Breach sequence, the endgame and the Debrief (spec §3.1; M3 plan, Task 11).
public sealed partial class MeridianGame
{
    /// <summary>The Breach has been shown and a scenario set up: every phone reads its briefing.</summary>
    private void EnterBriefing()
    {
        Session = Session with { Phase = SessionPhase.Briefing, Ready = [] };
        Rearm(fresh: true);
    }

    /// <summary>A seat has read its briefing. When every seat has (or the clock runs out), the endgame begins.</summary>
    private void Ready(SeatId seat)
    {
        if (Session.Ready.Contains(seat))
        {
            return;
        }
        Session = Session with { Ready = Session.Ready.Add(seat) };
        if (Session.Seats.All(s => Session.Ready.Contains(s.Seat)))
        {
            BeginEndgame();
        }
        else
        {
            Rearm();
        }
    }

    private void BeginEndgame()
    {
        var game = Session.Game!;
        var result = Rules.Step(_pack, game, new Begin(game.ActiveSeat));
        Session = Session with { Phase = SessionPhase.Endgame, Game = result.State, Ready = [] };
        Record(result.Events);
        Present(result.Events);
        Rearm(fresh: true);
    }

    /// <summary>A side has won and it has been shown: the Debrief reveals everything until the VIP continues.</summary>
    private void EnterDebrief()
    {
        Session = Session with { Phase = SessionPhase.Debrief, Ending = Session.Game!.Board!.Outcome!.Message };
        Rearm(fresh: true);
    }

    /// <summary>Results (spec §2.9): every seat scores 1 for a win and 0 otherwise, winners first.</summary>
    private void FinishGame()
    {
        if (Session.Phase != SessionPhase.Debrief)
        {
            return;
        }
        var outcome = Session.Game!.Board!.Outcome!;
        Session = Session with { Phase = SessionPhase.Over };
        Rearm();
        var scores = Session.Seats
            .Select(s => new PlayerScore(s.Player, s.Name, outcome.Winners.Contains(s.Seat) ? 1 : 0))
            .OrderByDescending(s => s.Score).ToList();
        _ctx.EndGame(new GameResults(scores, outcome.Message));
    }

    /// <summary>The Debrief's three awards, from the crew's stats; one with no clear winner is left out.</summary>
    internal IReadOnlyList<(string Title, SeatId Seat)> Awards()
    {
        var crew = Session.Game!.Crew;
        var awards = new List<(string, SeatId)>();
        void Top(string title, Func<CrewState, int> score)
        {
            var best = crew.Max(score);
            var top = crew.Where(c => score(c) == best).ToList();
            if (best > 0 && top.Count == 1)
            {
                awards.Add((title, top[0].Seat));
            }
        }
        Top("Pathfinder: most compartments found", c => c.Counted.Revealed);
        Top("Heavy hitter: most damage dealt", c => c.Counted.Damage);
        if (crew.Count(c => c.Aboard) == 1)
        {
            awards.Add(("Last one standing", crew.Single(c => c.Aboard).Seat));
        }
        return awards;
    }
}
