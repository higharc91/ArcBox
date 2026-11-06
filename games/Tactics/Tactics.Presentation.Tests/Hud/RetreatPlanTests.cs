using Tactics.Game;
using Tactics.Presentation.Hud;

namespace Tactics.Presentation.Tests.Hud;

/// <summary><see cref="RetreatPlan"/>: the vote to fall back, as the TV's vote panel shows it (#111).</summary>
public sealed class RetreatPlanTests
{
    private static RetreatView Vote(params (string Name, string? Vote)[] voters)
        => new("Ana", [.. voters.Select(v => new RetreatVoter(v.Name, "#ffd23f", v.Vote))], voters.Length / 2 + 1, null);

    [Fact]
    public void No_vote_no_panel()
        => Assert.Null(RetreatPlan.Of(null, "0:10"));

    [Fact]
    public void The_panel_has_a_row_to_fall_back_and_one_to_stay_with_each_sides_voters()
    {
        var panel = RetreatPlan.Of(Vote(("Ana", "yes"), ("Bo", "no"), ("Cy", null), ("Di", "yes")), "0:14")!;

        Assert.Equal(RetreatPlan.Heading, panel.Heading);
        Assert.True(panel.Party);
        Assert.Equal("0:14", panel.Clock);
        Assert.Equal(["A", "B"], panel.Rows.Select(r => r.Letter));
        Assert.Equal([RetreatPlan.FallBackRow, RetreatPlan.StayRow], panel.Rows.Select(r => r.Id));
        Assert.Equal(["Ana", "Di"], panel.Rows[0].Voters.Select(v => v.Name));
        Assert.Equal(["Bo"], panel.Rows[1].Voters.Select(v => v.Name));
        Assert.Equal(RetreatPlan.Cost, panel.Rows[0].Detail);
        Assert.Equal(["Ana proposes to fall back"], panel.Rows[0].Label);
    }

    [Fact]
    public void The_tally_says_how_many_it_takes_and_who_is_still_out()
    {
        Assert.Equal("1 of 3 needed to fall back · 3 to answer", RetreatPlan.Of(Vote(("Ana", "yes"), ("Bo", null), ("Cy", null), ("Di", null)), null)!.Tally);
        Assert.Equal("2 of 3 needed to fall back", RetreatPlan.Of(Vote(("Ana", "yes"), ("Bo", "yes"), ("Cy", "no"), ("Di", "no")), null)!.Tally);
    }

    [Fact]
    public void A_view_only_the_clock_changes_is_a_different_panel_and_the_same_rows()
    {
        var vote = Vote(("Ana", "yes"), ("Bo", null));
        var a = RetreatPlan.Of(vote, "0:10")!;
        var b = RetreatPlan.Of(vote, "0:09")!;

        Assert.NotEqual(a, b);
        Assert.Equal(RetreatPlan.Of(vote, "0:10"), a);
        Assert.True(a.Rows.SequenceEqual(b.Rows));
    }

    [Fact]
    public void The_battle_view_carries_the_vote_to_the_TV()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(Vote(("Ana", "yes")), ArcBox.Sdk.ArcJson.Options);
        var back = System.Text.Json.JsonSerializer.Deserialize<RetreatView>(json, ArcBox.Sdk.ArcJson.Options)!;
        Assert.Equal("Ana", back.Proposer);
        Assert.Equal("yes", back.Voters[0].Vote);
    }
}
