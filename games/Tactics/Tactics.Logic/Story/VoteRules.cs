using System.Collections.Immutable;

namespace Tactics.Logic;

/// <summary>
/// A party vote as it stands: the <see cref="Options"/> on offer, in the node's order, and each voter's pick by player id. A vote
/// for something not in <see cref="Options"/> (a tap meant for the question before) counts for nothing.
/// </summary>
public sealed record VoteTally(ImmutableArray<string> Options, ImmutableDictionary<string, string> Votes);

/// <summary>How a vote closed: <see cref="Won"/>, or <see cref="Tied"/> with someone to break the tie.</summary>
public abstract record VoteResult;

/// <summary>The vote chose <see cref="Option"/>.</summary>
public sealed record Won(string Option) : VoteResult;

/// <summary>A tie for <paramref name="Options"/>, in the node's order, that <paramref name="Decider"/> breaks (<see cref="VoteRules.Break"/>).</summary>
public sealed record Tied(string Decider, ImmutableArray<string> Options) : VoteResult;

/// <summary>
/// How a vote closes (spec §2 and §4.2; 5d plan §5), with no clock: the game decides when, and this decides what. The leader breaks a
/// tie, or with no leader the first connected player does; a vote nobody answers takes the <c>unanswered</c> rule, which for a
/// question is the first option offered and for picking who rolls is the best bonus. A voter who's left keeps their vote.
/// </summary>
public static class VoteRules
{
    /// <summary>
    /// The result of <paramref name="tally"/>. With no live votes, <paramref name="unanswered"/> picks from the options. A clear winner
    /// wins. A tie goes to the decider, <paramref name="leader"/> or else <paramref name="firstConnected"/>: at once if their own vote is
    /// for a tied option, or else as a <see cref="Tied"/> of the tied options only. With nobody to decide, the first tied in the node's
    /// order wins, as an unanswered tie-break would. Throws when there are no options, or <paramref name="unanswered"/> picks one that
    /// isn't offered.
    /// </summary>
    public static VoteResult Close(VoteTally tally, string? leader, string? firstConnected, Func<ImmutableArray<string>, string> unanswered)
    {
        if (tally.Options.IsEmpty)
            throw new ArgumentException("A vote needs something to vote for.", nameof(tally));
        var counts = tally.Options.Select(o => tally.Votes.Values.Count(v => v == o)).ToArray();
        var top = counts.Max();
        if (top == 0)
        {
            var taken = unanswered(tally.Options);
            return tally.Options.Contains(taken)
                ? new Won(taken)
                : throw new InvalidOperationException($"The unanswered rule took '{taken}', which isn't offered.");
        }

        ImmutableArray<string> tied = [.. tally.Options.Where((_, i) => counts[i] == top)];
        if (tied.Length == 1)
            return new Won(tied[0]);
        if ((leader ?? firstConnected) is not { } decider)
            return new Won(tied[0]);
        return tally.Votes.TryGetValue(decider, out var own) && tied.Contains(own) ? new Won(own) : new Tied(decider, tied);
    }

    /// <summary>The option a tie-break settles on: the decider's <paramref name="pick"/> if it's one of the tied, else the first tied.</summary>
    public static string Break(Tied tie, string? pick) => pick is not null && tie.Options.Contains(pick) ? pick : tie.Options[0];

    /// <summary>True when every one of <paramref name="present"/> has a live vote in <paramref name="tally"/>: "everyone has voted".</summary>
    public static bool AllIn(VoteTally tally, IEnumerable<string> present)
        => present.All(p => tally.Votes.TryGetValue(p, out var vote) && tally.Options.Contains(vote));
}
