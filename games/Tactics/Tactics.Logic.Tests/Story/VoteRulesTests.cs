using System.Collections.Immutable;

namespace Tactics.Logic.Tests;

public sealed class VoteRulesTests
{
    private static readonly ImmutableArray<string> Options = ["force", "parley", "pay"];

    /// <summary>The spec's rule for a vote nobody answers: the first option offered.</summary>
    private static string First(ImmutableArray<string> options) => options[0];

    private static VoteTally Tally(params (string Voter, string Option)[] votes) => Of(Options, votes);

    private static VoteTally Of(ImmutableArray<string> options, params (string Voter, string Option)[] votes)
        => new(options, votes.ToImmutableDictionary(v => v.Voter, v => v.Option));

    private static VoteResult Close(VoteTally tally, string? leader = null, string? firstConnected = null)
        => VoteRules.Close(tally, leader, firstConnected, First);

    /// <summary>A result in words, since a record's own equality compares an <see cref="ImmutableArray{T}"/> by reference.</summary>
    private static string Say(VoteResult result) => result switch
    {
        Won won => $"won {won.Option}",
        Tied tied => $"tie for {tied.Decider}: {string.Join(", ", tied.Options)}",
        _ => throw new ArgumentOutOfRangeException(nameof(result)),
    };

    [Fact]
    public void A_clear_winner_wins()
    {
        Assert.Equal(new Won("parley"), Close(Tally(("jess", "parley"), ("dave", "parley"), ("priya", "force")), leader: "priya"));
        Assert.Equal(new Won("force"), Close(Tally(("jess", "force"))));
        Assert.Equal(new Won("pay"), Close(Tally(("jess", "pay"), ("dave", "pay"), ("sam", "pay"), ("alex", "pay")), leader: "jess"));
    }

    [Fact]
    public void The_leaders_vote_breaks_a_tie()
    {
        var tie = Tally(("jess", "force"), ("dave", "parley"), ("priya", "parley"), ("sam", "force"));

        Assert.Equal(new Won("parley"), Close(tie, leader: "dave"));
        Assert.Equal(new Won("force"), Close(tie, leader: "sam"));
        Assert.Equal(new Won("force"), Close(tie, leader: "sam", firstConnected: "dave"));
    }

    [Fact]
    public void A_leader_outside_the_tie_gets_a_tie_break()
    {
        var result = Close(Tally(("jess", "force"), ("dave", "parley"), ("sam", "pay")).With(("alex", "force"), ("priya", "parley")), leader: "sam");

        var tied = Assert.IsType<Tied>(result);
        Assert.Equal("sam", tied.Decider);
        Assert.Equal(["force", "parley"], tied.Options);
    }

    [Fact]
    public void A_leader_who_didnt_vote_gets_a_tie_break()
    {
        var tied = Assert.IsType<Tied>(Close(Tally(("jess", "parley"), ("dave", "force")), leader: "sam", firstConnected: "jess"));

        Assert.Equal("sam", tied.Decider);
        Assert.Equal(["force", "parley"], tied.Options);
    }

    [Fact]
    public void With_no_leader_the_first_connected_decides()
    {
        var tie = Tally(("jess", "parley"), ("dave", "force"));

        Assert.Equal(new Won("parley"), Close(tie, firstConnected: "jess"));
        var tied = Assert.IsType<Tied>(Close(tie, firstConnected: "sam"));
        Assert.Equal("sam", tied.Decider);

        // Nobody to ask: the tie goes as an unanswered tie-break would, to the first tied in node order.
        Assert.Equal(new Won("force"), Close(tie));
    }

    [Fact]
    public void No_votes_takes_the_unanswered_rule()
    {
        Assert.Equal(new Won("force"), Close(Tally(), leader: "jess"));
        Assert.Equal(new Won("pay"), VoteRules.Close(Tally(), "jess", "jess", options => options[^1]));

        // The roller pick's rule is the best bonus, not the first card.
        var cards = ImmutableArray.Create("kara", "wren", "ilyra");
        Assert.Equal(new Won("wren"), VoteRules.Close(Of(cards), null, null, members => SkillCheck.Best(members.Select(m => (m, m == "wren" ? 5 : 1)))));
        Assert.Throws<InvalidOperationException>(() => VoteRules.Close(Tally(), null, null, _ => "elsewhere"));
        Assert.Throws<ArgumentException>(() => VoteRules.Close(Of([]), null, null, First));
    }

    [Fact]
    public void A_three_way_tie_offers_only_the_tied()
    {
        var options = ImmutableArray.Create("a", "b", "c", "d");
        var tally = Of(options, ("1", "c"), ("2", "a"), ("3", "b"), ("4", "c"), ("5", "a"), ("6", "b"), ("7", "d"));

        var tied = Assert.IsType<Tied>(VoteRules.Close(tally, "7", null, First));

        Assert.Equal(["a", "b", "c"], tied.Options);
        Assert.Equal("a", VoteRules.Break(tied, null));
        Assert.Equal("c", VoteRules.Break(tied, "c"));
        Assert.Equal("a", VoteRules.Break(tied, "d"));
    }

    [Fact]
    public void A_stale_option_isnt_counted()
    {
        // A vote for an option the node no longer offers (a tap from the last question) counts for nothing.
        var tally = Tally(("jess", "parley"), ("dave", "force"), ("sam", "gone"), ("alex", "gone"));

        Assert.Equal(new Won("force"), Close(tally, leader: "dave"));
        Assert.IsType<Tied>(Close(tally, leader: "sam"));
        Assert.Equal(new Won("force"), Close(Tally(("sam", "gone"))));
        Assert.False(VoteRules.AllIn(tally, ["jess", "sam"]));
    }

    [Fact]
    public void Everyone_present_has_voted_only_when_each_has_a_live_vote()
    {
        var tally = Tally(("jess", "parley"), ("dave", "force"), ("away", "pay"));

        Assert.True(VoteRules.AllIn(tally, ["jess", "dave"]));
        Assert.False(VoteRules.AllIn(tally, ["jess", "dave", "sam"]));
        Assert.True(VoteRules.AllIn(Tally(), []));

        // A voter who left keeps their vote: it still counts, though they no longer hold up the close.
        Assert.Equal(new Won("pay"), Close(tally.With(("away2", "pay")), leader: "jess"));
    }

    // ---- The model test -----------------------------------------------------------------------------------------------------

    private static readonly string[] Voters = ["jess", "dave", "priya", "sam", "marcus", "alex"];

    /// <summary>
    /// Random votes, some stale, from random voters (any of whom may have left), with a random leader and first connected player,
    /// against the rules written out another way: count the live votes; none takes the unanswered rule; one top option wins; a tie
    /// goes to the decider's vote if it's tied, else to the decider, else the first tied. And the result can't depend on the order
    /// the votes arrived in.
    /// </summary>
    [Fact]
    public void Random_tallies_close_by_the_rules_and_by_them_alone()
    {
        var (won, ties, unanswered) = (0, 0, 0);
        for (var seed = 0; seed < 5000; seed++)
        {
            var rng = new Random(seed);
            var options = Options.Take(rng.Next(1, 4)).ToImmutableArray();
            var votes = Voters.Where(_ => rng.Next(3) > 0)
                .Select(v => (Voter: v, Option: rng.Next(8) == 0 ? "stale" : options[rng.Next(options.Length)])).ToList();
            string? Someone() => rng.Next(4) == 0 ? null : Voters[rng.Next(Voters.Length)];
            var (leader, first) = (Someone(), Someone());

            var result = VoteRules.Close(Of(options, [.. votes]), leader, first, First);

            var counts = options.Select(o => votes.Count(v => v.Option == o)).ToArray();
            var top = counts.Max();
            var tied = options.Where((_, i) => counts[i] == top).ToArray();
            var decider = leader ?? first;
            var says = votes.Where(v => v.Voter == decider).Select(v => v.Option).FirstOrDefault();
            VoteResult expected = top == 0 ? new Won(options[0])
                : tied.Length == 1 ? new Won(tied[0])
                : decider is null ? new Won(tied[0])
                : tied.Contains(says) ? new Won(says!)
                : new Tied(decider, [.. tied]);
            Assert.Equal(Say(expected), Say(result));
            (won, ties, unanswered) = (won + (result is Won && top > 0 ? 1 : 0), ties + (result is Tied ? 1 : 0), unanswered + (top == 0 ? 1 : 0));

            // Determinism: the same votes, arriving in any order, close the same way.
            votes.Reverse();
            Assert.Equal(Say(result), Say(VoteRules.Close(Of(options, [.. votes]), leader, first, First)));
            var shuffled = votes.OrderBy(_ => rng.Next()).ToArray();
            Assert.Equal(Say(result), Say(VoteRules.Close(Of(options, shuffled), leader, first, First)));
        }
        Assert.True(won > 1000 && ties > 300 && unanswered > 10, $"{won} won, {ties} tied, {unanswered} unanswered: the generator should cover all three.");
    }

    /// <summary>A tally whose voters all pick at random is a vote nobody answered only when nobody voted.</summary>
    [Fact]
    public void The_unanswered_rule_is_asked_only_when_nobody_voted()
    {
        var asked = 0;
        string Count(ImmutableArray<string> options)
        {
            asked++;
            return options[0];
        }
        VoteRules.Close(Tally(("jess", "pay")), null, null, Count);
        VoteRules.Close(Tally(("jess", "stale")), null, null, Count);
        Assert.Equal(1, asked);
    }
}

internal static class TallyExtensions
{
    public static VoteTally With(this VoteTally tally, params (string Voter, string Option)[] votes)
        => tally with { Votes = tally.Votes.SetItems(votes.Select(v => KeyValuePair.Create(v.Voter, v.Option))) };
}
