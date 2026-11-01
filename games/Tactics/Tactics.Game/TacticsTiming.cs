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
    /// <summary>0.30 s a tile, 60 ms a drop level, a 750 ms lease, 8 s of silence, and saves at 1 s after stopping and every 15 s.</summary>
    public static WalkTiming Default { get; } = new(TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(60), TimeSpan.FromMilliseconds(750),
        TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(15));
}

/// <summary>
/// The adapter's clocks in one place (adapter spec §9, decision 5): a player's turn, a reaction prompt, how long an
/// AI beat may wait for the TV's <c>shown</c> signal before the next one goes anyway, and walking, which defaults to
/// <see cref="WalkTiming.Default"/>.
/// </summary>
public sealed record TacticsTiming(TimeSpan Turn, TimeSpan Reaction, TimeSpan PaceBase, TimeSpan PacePerEvent, TimeSpan PaceMax, WalkTiming? Walk = null)
{
    /// <summary>60 s turns (rules-engine spec §5.5), 10 s reactions, 400 ms + 250 ms per event up to 6 s per AI beat.</summary>
    public static TacticsTiming Default { get; } = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10),
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(6));

    /// <summary>The clocks of walking the world.</summary>
    public WalkTiming Walk { get; init; } = Walk ?? WalkTiming.Default;

    /// <summary>The fallback wait for a beat of <paramref name="events"/> events.</summary>
    public TimeSpan Pace(int events)
    {
        var wait = PaceBase + PacePerEvent * events;
        return wait < PaceMax ? wait : PaceMax;
    }
}
