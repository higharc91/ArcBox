using System.Collections.Immutable;
using Tactics.Game;

namespace Tactics.Presentation.Beats;

/// <summary>
/// The beats still to play: each view carries the last 20, and a beat goes in the first time its <see cref="Beat.Seq"/> is
/// seen. A gap (the log moved on past beats this queue never saw) just plays what's there.
/// </summary>
public sealed class BeatQueue
{
    private readonly Queue<Beat> _beats = new();
    private int _seen;

    /// <summary>The number of beats waiting.</summary>
    public int Count => _beats.Count;

    /// <summary>
    /// Queues the beats of a view that haven't been seen. Numbers that went backwards mean a new battle, or the same one
    /// started over: what's queued is dropped, and this returns true.
    /// </summary>
    public bool Offer(ImmutableArray<Beat> beats)
    {
        if (beats.IsDefaultOrEmpty)
            return false;
        var restarted = beats[^1].Seq < _seen;
        if (restarted)
            Clear();
        foreach (var beat in beats.Where(b => b.Seq > _seen))
        {
            _beats.Enqueue(beat);
            _seen = beat.Seq;
        }
        return restarted;
    }

    /// <summary>The oldest beat waiting, or null.</summary>
    public Beat? Next() => _beats.TryDequeue(out var beat) ? beat : null;

    /// <summary>Forgets everything, as for a new battle.</summary>
    public void Clear()
    {
        _beats.Clear();
        _seen = 0;
    }
}
