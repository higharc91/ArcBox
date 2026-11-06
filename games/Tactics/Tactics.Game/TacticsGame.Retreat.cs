using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using ArcBox.Sdk;
using Tactics.Logic;

namespace Tactics.Game;

/// <summary>
/// One present player's say in the retreat vote, as the TV draws it.
/// </summary>
/// <param name="Name">The player's name.</param>
/// <param name="Color">Their colour, "#rrggbb".</param>
/// <param name="Vote">"yes", "no", or null while they haven't answered.</param>
public sealed record RetreatVoter(string Name, string? Color, string? Vote);

/// <summary>
/// The party's vote to fall back (issue #111), for the TV (and, shaped by <see cref="TacticsGame"/>, the phones).
/// </summary>
/// <param name="Proposer">The player who proposed it.</param>
/// <param name="Voters">Every present player, in seat order.</param>
/// <param name="Needed">The yes votes it takes: a strict majority of the present players.</param>
/// <param name="Deadline">When it times out and fails.</param>
public sealed record RetreatView(string Proposer, ImmutableArray<RetreatVoter> Voters, int Needed, DateTimeOffset? Deadline);

public sealed partial class TacticsGame
{
    /// <summary>The open vote: who proposed it, in which round, and what each present player said (it runs <see cref="TacticsTiming.RetreatVote"/>).</summary>
    private sealed class RetreatVoteState(string proposer, int round)
    {
        public string Proposer { get; } = proposer;

        /// <summary>The round it was proposed in: a failed vote shuts proposing until the next.</summary>
        public int Round { get; } = round;

        public Dictionary<string, bool> Votes { get; } = new(StringComparer.Ordinal) { [proposer] = true };
    }

    /// <summary>The open vote to fall back, or null. A social action of the table's, not the rules': it isn't saved.</summary>
    private RetreatVoteState? _retreat;

    /// <summary>The round a vote to fall back failed in (0 for none yet): nobody can propose again in it.</summary>
    private int _retreatFailedRound;

    /// <summary>The players here who are in the battle: they vote.</summary>
    private List<Player> RetreatVoters()
        => [.. _players.Where(p => p.Profile is { } pr && _doc.Battle is { } save && save.Members.Contains(pr) && _online.Contains(p.Id))];

    /// <summary>
    /// Why nobody can propose falling back now, or null when anyone could: the vote is open, it just failed this round, or the rules
    /// say no (before round <see cref="Battle.RetreatRound"/>, or the encounter forbids it).
    /// </summary>
    private Rejection? CantFallBack(UnitId hero)
    {
        if (_doc.Battle is null || State.Outcome is not null)
            return Rejection.Of(RejectCode.BattleOver);
        return Battle.Check(State, new Retreat(hero), Rules);
    }

    /// <summary>A present player proposes "Fall back" from their phone, on anyone's turn.</summary>
    private void ProposeFallBack(Player player, ProfileId profile, UnitId hero)
    {
        if (_retreat is not null)
            return;
        if (_retreatFailedRound >= State.Round && _retreatFailedRound > 0)
        {
            Toast(player.Id, "The party voted to stay and fight. You can propose it again next round.");
            return;
        }
        if (CantFallBack(hero) is { } refused)
        {
            Toast(player.Id, refused.Reason);
            return;
        }
        _retreat = new RetreatVoteState(profile.Value, State.Round);
        _ctx.SetTimer(Timers.Retreat, _timing.RetreatVote);
        SettleRetreat();
    }

    /// <summary>A present player answers the open vote; the last answer stands.</summary>
    private void VoteFallBack(ProfileId profile, JsonElement? data)
    {
        if (_retreat is not { } vote || RetreatVoters().All(p => p.Profile != profile)
            || data is not { ValueKind: JsonValueKind.Object } d || !d.TryGetProperty("yes", out var yes)
            || yes.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return;
        vote.Votes[profile.Value] = yes.GetBoolean();
        SettleRetreat();
    }

    /// <summary>
    /// Counts the open vote: a strict majority of the present players ends the battle; once that can't be reached, it fails. A player who
    /// leaves mid-vote stops counting.
    /// </summary>
    private void SettleRetreat()
    {
        if (_retreat is not { } vote)
            return;
        var present = RetreatVoters().Select(p => p.Profile!.Value.Value).ToList();
        if (present.Count == 0)
        {
            CloseRetreat(failed: false);
            return;
        }
        var yes = present.Count(p => vote.Votes.GetValueOrDefault(p));
        var no = present.Count(p => vote.Votes.TryGetValue(p, out var said) && !said);
        var needed = present.Count / 2 + 1;
        if (yes >= needed)
            FallBack(vote);
        else if (present.Count - no < needed)
            CloseRetreat(failed: true);
    }

    private void CloseRetreat(bool failed)
    {
        if (_retreat is not { } vote)
            return;
        _ctx.CancelTimer(Timers.Retreat);
        _retreat = null;
        if (failed)
            _retreatFailedRound = vote.Round;
    }

    /// <summary>The vote carried: the rules end the battle as a flight (<see cref="Retreat"/>), and the campaign takes the result.</summary>
    private void FallBack(RetreatVoteState vote)
    {
        var actor = UnitOf(new ProfileId(vote.Proposer)) ?? UnitFor(0);
        var before = State;
        var command = new Retreat(actor);
        CloseRetreat(failed: false);
        if (TryApply(before, command, aiFinished: false, "command") is not { } resolution || resolution.Rejected is not null)
            return;     // the rules said no after all: the vote is spent, the fight goes on
        _plan = null;
        SetState(resolution.State);
        AddBeat(actor, resolution.Events, before.Active);
        Drive();
    }

    private void RetreatTimedOut()
    {
        if (_retreat is null)
            return;
        CloseRetreat(failed: true);
    }

    /// <summary>The vote as the TV draws it, or null while none is open.</summary>
    private RetreatView? RetreatViewOf()
    {
        if (_retreat is not { } vote)
            return null;
        var voters = RetreatVoters();
        var name = _players.FirstOrDefault(p => p.Profile?.Value == vote.Proposer)?.Name ?? "Someone";
        return new RetreatView(name,
            [.. voters.Select(p => new RetreatVoter(p.Name, ColorOf(p.Profile!.Value.Value),
                vote.Votes.TryGetValue(p.Profile!.Value.Value, out var said) ? said ? "yes" : "no" : null))],
            voters.Count / 2 + 1, _ctx.TimerDeadline(Timers.Retreat));
    }

    /// <summary>
    /// Adds what the phone of <paramref name="profile"/> shows of falling back to <paramref name="view"/>'s data as <c>fallBack</c>: the
    /// button to propose it (<c>canPropose</c>), the vote under way, or that it failed this round. Nothing is added when none of
    /// them applies (before round 3, or an encounter that forbids it), so those screens are as they were. <paramref name="offer"/>
    /// false (a reaction prompt, a 10 s decision) shows a vote under way but never the button, nor the note that one failed.
    /// </summary>
    private CustomView WithFallBack(CustomView view, ProfileId profile, UnitId hero, bool offer = true)
    {
        JsonObject? panel = null;
        if (_retreat is { } vote)
        {
            var voters = RetreatVoters();
            var mine = vote.Votes.TryGetValue(profile.Value, out var said) ? said ? "yes" : "no" : null;
            panel = new JsonObject
            {
                ["vote"] = new JsonObject
                {
                    ["proposer"] = _players.FirstOrDefault(p => p.Profile?.Value == vote.Proposer)?.Name ?? "Someone",
                    ["yes"] = voters.Count(p => vote.Votes.GetValueOrDefault(p.Profile!.Value.Value)),
                    ["no"] = voters.Count(p => vote.Votes.TryGetValue(p.Profile!.Value.Value, out var v) && !v),
                    ["present"] = voters.Count,
                    ["needed"] = voters.Count / 2 + 1,
                    ["mine"] = mine,
                    ["until"] = _ctx.TimerDeadline(Timers.Retreat)?.UtcDateTime.ToString("O"),
                },
            };
        }
        else if (offer && CantFallBack(hero) is null)
        {
            panel = _retreatFailedRound > 0 && _retreatFailedRound >= State.Round
                ? new JsonObject { ["failed"] = true }
                : new JsonObject { ["canPropose"] = true };
        }
        if (panel is null)
            return view;
        var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(view.Data, ArcJson.Options)!;
        data["fallBack"] = JsonSerializer.SerializeToElement(panel, ArcJson.Options);
        return view with { Data = JsonSerializer.SerializeToElement(data, ArcJson.Options) };
    }
}
