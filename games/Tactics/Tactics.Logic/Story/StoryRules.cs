namespace Tactics.Logic;

/// <summary>
/// Where a campaign is in its story (spec §4.2): one beat at a time, in the arc's order. 5c arms a battle stamp only for the beat
/// this offers, and 5d's markers and chains follow the same rule, so it means the same thing to both. <c>StoryOrder</c> in
/// <c>Tactics.WorldTools</c> replays a region's story by this rule, and the two must change together.
/// </summary>
public static class StoryRules
{
    /// <summary>
    /// The beat on offer: the first of the arc, in order, that isn't done and whose <see cref="ArcBeat.Requires"/> are all set. A beat
    /// whose flags aren't set is passed over, not waited for, so a branch the party didn't take never blocks the story; it's offered
    /// again if a later beat sets them. Null when nothing is left to offer.
    /// </summary>
    public static ArcBeat? Available(Arc arc, IReadOnlySet<string> flags, IReadOnlySet<string> beats)
        => arc.Beats.FirstOrDefault(b => !beats.Contains(b.Id) && b.Requires.All(flags.Contains));

    /// <summary>True once the arc's last beat is done, which ends its chapter (spec §2). An arc with no beats never ends.</summary>
    public static bool ChapterDone(Arc arc, IReadOnlySet<string> beats) => arc.Beats is [.., var last] && beats.Contains(last.Id);
}
