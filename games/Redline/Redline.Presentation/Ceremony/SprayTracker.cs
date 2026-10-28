using Redline.Logic;

namespace Redline.Presentation.Ceremony;

/// <summary>A spray the TV has not shown yet.</summary>
/// <param name="First">The first spray of the ceremony (the victory sting plays with it).</param>
public sealed record NewSpray(int Step, int Seq, double Strength, bool First);

/// <summary>
/// Turns the host view's cumulative <see cref="PodiumStep.Sprays"/> into the sprays to launch now. <see cref="Spray.Seq"/>
/// runs 1, 2, 3… across the whole podium, so a spray is new when its Seq is above the highest one already handled; a
/// repeated view launches nothing. A TV that joins mid-ceremony (no view before this one) does not replay the
/// sprays it missed, and a new ceremony (the podium appears again after a view without one) starts over.
/// </summary>
public sealed class SprayTracker
{
    private int _lastSeq;
    private bool _any;
    private bool _active;

    /// <summary>The highest <see cref="Spray.Seq"/> handled so far in this ceremony.</summary>
    public int LastSeq => _lastSeq;

    /// <param name="view">The view just received.</param>
    /// <param name="previous">The view before it, or null when this is the first view the TV has seen.</param>
    /// <returns>The new sprays in Seq order (empty outside the ceremony).</returns>
    public IReadOnlyList<NewSpray> Update(RedlineHostView view, RedlineHostView? previous)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (view.Podium is not { } podium)
        {
            _active = false;
            return [];
        }
        var all = podium.Steps
            .SelectMany(step => step.Sprays.Select(spray => (step.Step, spray.Seq, spray.Strength)))
            .OrderBy(x => x.Seq)
            .ToList();
        if (!_active)
        {
            // A new ceremony. Joined mid-way (no podium before this view): what already happened is history.
            _active = true;
            _lastSeq = 0;
            _any = false;
            if (previous is null)
            {
                _lastSeq = all.Count > 0 ? all[^1].Seq : 0;
                _any = all.Count > 0;
                return [];
            }
        }
        var fresh = new List<NewSpray>();
        foreach (var (step, seq, strength) in all.Where(x => x.Seq > _lastSeq))
        {
            fresh.Add(new NewSpray(step, seq, strength, !_any));
            _any = true;
            _lastSeq = seq;
        }
        return fresh;
    }
}
