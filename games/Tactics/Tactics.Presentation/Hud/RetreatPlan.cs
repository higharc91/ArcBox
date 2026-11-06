using System.Collections.Immutable;
using System.Globalization;
using Tactics.Game;
using Tactics.Presentation.Story;

namespace Tactics.Presentation.Hud;

/// <summary>
/// The party's vote to fall back (issue #111) on the TV, in the style of the story's party vote: <see cref="VotePanel"/>, at the bottom
/// right, with a row to fall back and one to stay and fight, each with the pips of whoever voted for it, and the tally under them. A player
/// who hasn't answered is in neither row; the tally says how many are still out.
/// </summary>
public static class RetreatPlan
{
    /// <summary>The panel's heading.</summary>
    public const string Heading = "Fall back?";

    /// <summary>The row that carries it.</summary>
    public const string FallBackRow = "yes";

    /// <summary>The row that votes it down.</summary>
    public const string StayRow = "no";

    /// <summary>What falling back costs, under its row.</summary>
    public const string Cost = "Half XP · injuries kept · no loot";

    /// <summary>The panel for <paramref name="vote"/> at <paramref name="clock"/> (<see cref="VoteClock.Text"/>), or null while none is open.</summary>
    public static VotePanel? Of(RetreatView? vote, string? clock)
    {
        if (vote is null)
            return null;
        ImmutableArray<VoterPip> Said(string said) => [.. vote.Voters.Where(v => v.Vote == said).Select(v => VoterPip.Of(v.Name, v.Color))];
        var yes = Said(FallBackRow);
        var no = Said(StayRow);
        var out_ = vote.Voters.Length - yes.Length - no.Length;
        var tally = string.Create(CultureInfo.InvariantCulture, $"{yes.Length} of {vote.Needed} needed to fall back")
            + (out_ > 0 ? string.Create(CultureInfo.InvariantCulture, $" · {out_} to answer") : "");
        return new VotePanel(Heading, Party: true, Tie: false, clock,
            [
                new VoteRow(FallBackRow, "A", [$"{vote.Proposer} proposes to fall back"], Cost, yes),
                new VoteRow(StayRow, "B", ["Stay and fight"], null, no),
            ],
            tally, Away: null);
    }
}
