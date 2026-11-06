namespace Tactics.Game;

/// <summary>The clocks of walking the world (overworld 5b).</summary>
/// <param name="Step">How long a straight step on plain ground takes.</param>
/// <param name="DropStep">How much longer a step takes for each level of a drop past the second.</param>
/// <param name="HoldLease">How long a press lasts without a repeat, so a lost release can't walk the party on.</param>
/// <param name="LeadSilence">How long the leader's phone can be silent before the lead passes on.</param>
/// <param name="SaveAfterStop">How long after the party stops that a walk is saved.</param>
/// <param name="SaveWhileWalking">How often a walk is saved while the party keeps moving.</param>
public sealed record WalkTiming(TimeSpan Step, TimeSpan DropStep, TimeSpan HoldLease, TimeSpan LeadSilence, TimeSpan SaveAfterStop, TimeSpan SaveWhileWalking)
{
    /// <summary>0.30 s a tile, 60 ms a drop level, a 750 ms lease, 8 s of silence, saves at 1 s after stopping and every 15 s, and a 3 s takeback.</summary>
    public static WalkTiming Default { get; } = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(750),
        TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15));

    /// <summary>
    /// How long the party has to have been stopped before the session's own leader takes the lead back from whoever stood in, so that
    /// the stand-in's pad doesn't vanish the first time a thumb lifts. The party stopping again restarts it, so any press does.
    /// </summary>
    public TimeSpan TakebackAfter { get; init; } = TimeSpan.FromSeconds(3);
}

/// <summary>
/// The clocks of a scene (overworld 5d §4): the server's alone, since the TV sends nothing back for a line. Each is a number only a person
/// can judge, so they're here to be tuned, not in the rules.
/// </summary>
public sealed record StoryTiming
{
    /// <summary>The defaults of the 5d plan's tunables.</summary>
    public static StoryTiming Default { get; } = new();

    /// <summary>The pause after a line's clip before the next line: 0.5 s.</summary>
    public TimeSpan LineGap { get; init; } = TimeSpan.FromSeconds(0.5);

    /// <summary>How long a line with no clip is read for, for each word: 0.36 s.</summary>
    public TimeSpan PerWord { get; init; } = TimeSpan.FromSeconds(0.36);

    /// <summary>How long a line with no clip is read for, on top of its words: 1.2 s.</summary>
    public TimeSpan ReadBase { get; init; } = TimeSpan.FromSeconds(1.2);

    /// <summary>How long the party has to vote: 45 s (the user's decision 2).</summary>
    public TimeSpan Vote { get; init; } = TimeSpan.FromSeconds(45);

    /// <summary>How long the leader has to choose for the party: 30 s.</summary>
    public TimeSpan Choice { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>How long the decider has to break a tie: 10 s.</summary>
    public TimeSpan TieBreak { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>How long the party has to pick who rolls: 20 s.</summary>
    public TimeSpan Roller { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>How long a question stays open once everyone here has answered, for one last change: 2 s.</summary>
    public TimeSpan Settle { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>How long a roll's result shows before the scene goes on: 4.5 s.</summary>
    public TimeSpan Reveal { get; init; } = TimeSpan.FromSeconds(4.5);

    /// <summary>The pause between two scenes of a beat, with the next one's title: 2 s.</summary>
    public TimeSpan SceneGap { get; init; } = TimeSpan.FromSeconds(2);

    /// <summary>The brief settle-to-sleep transition before recovery is applied.</summary>
    public TimeSpan CampSleep { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Time to see the sunrise and hear its original music cue, skippable by the leader.</summary>
    public TimeSpan CampDawn { get; init; } = TimeSpan.FromSeconds(8);

    /// <summary>How long the card at the end of a chapter shows: 6 s.</summary>
    public TimeSpan Card { get; init; } = TimeSpan.FromSeconds(6);

    /// <summary>
    /// How long a line plays: its clip of <paramref name="clip"/> seconds and <see cref="LineGap"/>, or with no clip, a reading timer of
    /// <see cref="PerWord"/> for each word of <paramref name="text"/> and <see cref="ReadBase"/>.
    /// </summary>
    public TimeSpan Line(double? clip, string text) => clip is { } seconds
        ? TimeSpan.FromSeconds(seconds) + LineGap
        : PerWord * text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length + ReadBase;
}

/// <summary>
/// The adapter's clocks in one place (adapter spec §9, decision 5): a player's turn, a reaction prompt, how long an
/// AI beat may wait for the TV's <c>shown</c> signal before the next one goes anyway, walking, which defaults to
/// <see cref="WalkTiming.Default"/>, and scenes, which default to <see cref="StoryTiming.Default"/>.
/// </summary>
public sealed record TacticsTiming(TimeSpan Turn, TimeSpan Reaction, TimeSpan PaceBase, TimeSpan PacePerEvent, TimeSpan PaceMax, WalkTiming? Walk = null,
    StoryTiming? Story = null)
{
    /// <summary>60 s turns (rules-engine spec §5.5), 10 s reactions, 400 ms + 250 ms per event up to 6 s per AI beat.</summary>
    public static TacticsTiming Default { get; } = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10),
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(6));

    /// <summary>The clocks of walking the world.</summary>
    public WalkTiming Walk { get; init; } = Walk ?? WalkTiming.Default;

    /// <summary>The clocks of a scene.</summary>
    public StoryTiming Story { get; init; } = Story ?? StoryTiming.Default;

    /// <summary>
    /// The most a player's turn clock has left once they have spent both their Move and their Action (issue #113): it drops to the
    /// shorter of what's left and this, so the table isn't kept waiting on a player who has nothing left to do but tap End turn.
    /// </summary>
    public TimeSpan SpentTurn { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long the TV's "Something went wrong with that move" stays up at least: it comes down with the first command that
    /// applies normally after this.
    /// </summary>
    public TimeSpan FaultNotice { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// How long the opening beat of a fight in the world waits for the TV's <c>shown</c> before the AI goes anyway (overworld 5c §5): the
    /// TV walks the party to its places and steps the enemies in first, which takes at most 2.6 s. The players can act all the while.
    /// </summary>
    public TimeSpan FormUp { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a vote to fall back runs before it fails (issue #111).</summary>
    public TimeSpan RetreatVote { get; init; } = TimeSpan.FromSeconds(20);

    /// <summary>The fallback wait for a beat of <paramref name="events"/> events.</summary>
    public TimeSpan Pace(int events)
    {
        var wait = PaceBase + PacePerEvent * events;
        return wait < PaceMax ? wait : PaceMax;
    }
}
