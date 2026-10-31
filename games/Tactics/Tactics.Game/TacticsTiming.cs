namespace Tactics.Game;

/// <summary>
/// The adapter's clocks in one place (adapter spec §9, decision 5): a player's turn, a reaction prompt, and how long an
/// AI beat may wait for the TV's <c>shown</c> signal before the next one goes anyway.
/// </summary>
public sealed record TacticsTiming(TimeSpan Turn, TimeSpan Reaction, TimeSpan PaceBase, TimeSpan PacePerEvent, TimeSpan PaceMax)
{
    /// <summary>60 s turns (rules-engine spec §5.5), 10 s reactions, 400 ms + 250 ms per event up to 6 s per AI beat.</summary>
    public static TacticsTiming Default { get; } = new(TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(10),
        TimeSpan.FromMilliseconds(400), TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(6));

    /// <summary>The fallback wait for a beat of <paramref name="events"/> events.</summary>
    public TimeSpan Pace(int events)
    {
        var wait = PaceBase + PacePerEvent * events;
        return wait < PaceMax ? wait : PaceMax;
    }
}
