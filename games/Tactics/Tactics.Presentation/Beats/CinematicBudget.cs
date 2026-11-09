namespace Tactics.Presentation.Beats;

/// <summary>
/// Bounds optional camera overhead for large parties independently of which spells the AI chooses.
/// Script frames, telegraphs, rolls and outcomes keep playing when a later cut-in uses the existing Off presentation.
/// </summary>
public sealed class CinematicBudget
{
    public const double PerRound = 3.3;
    private int _round = -1;
    private double _used;

    public CutInPlayback? Apply(int round, int partySize, CutInPlayback? candidate)
    {
        if (_round != round) { _round = round; _used = 0; }
        if (candidate is null || partySize < 5) return candidate;
        if (_used + candidate.Extra > PerRound + 1e-7) return candidate.WithoutExtra();
        _used += candidate.Extra;
        return candidate;
    }

    public void Reset() { _round = -1; _used = 0; }
}
