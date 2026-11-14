namespace Meridian.Logic;

// Scenario actions and votes (spec §4.3; M3 plan, Task 4).
internal sealed partial class Resolver
{
    private static void LegalScenario(RulesPack pack, GameState state, SeatId seat, List<Command> legal)
    {
        if (state.Phase != Phase.Endgame || state.Board is null || state.Turn.Acted)
        {
            return;
        }
        var scenario = pack.Scenarios.Get(state.Board.Scenario);
        legal.AddRange(scenario.ActionsFor(pack, state, seat).Select(a => new ScenarioAct(seat, a.Id, a.Data)));
    }

    private void Act(ScenarioAct act)
    {
        State = State with { Turn = State.Turn with { Acted = true } };
        State = Pack.Scenarios.Get(State.Board!.Scenario).Act(Pack, State, act, Events);
    }

    /// <summary>Takes a ballot. When the last is in, the tally is public and the scenario reacts to the result.</summary>
    private void Vote(SeatId voter, string? choice)
    {
        var vote = (VotePending)State.Waiting!;
        var owed = vote.Owed.Remove(voter);
        var cast = choice is null ? vote.Cast : vote.Cast.Add(new Ballot(voter, choice));
        Events.Add(new BallotCast(voter));
        if (!owed.IsEmpty)
        {
            State = State with { Waiting = vote with { From = owed[0], Owed = owed, Cast = cast } };
            return;
        }

        var tally = cast.GroupBy(b => b.Choice).Select(g => new VoteCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Votes).ThenBy(c => c.Choice, StringComparer.Ordinal).ToEqArray();
        var result = tally.Count > 0 && (tally.Count == 1 || tally[0].Votes > tally[1].Votes) ? tally[0].Choice : null;
        State = State with { Waiting = null };
        Events.Add(new VoteResolved(vote.Purpose, result, tally));
    }
}
