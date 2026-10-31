using System.Collections.Immutable;
using Tactics.Game;

namespace Tactics.Presentation.Beats;

/// <summary>
/// The beats still to play: each view carries the last 20, and a beat goes in the first time its <see cref="Beat.Seq"/> is
/// seen. It also says when playing them through is no use any more (<see cref="Late"/>).
/// </summary>
public sealed class BeatQueue
{
    /// <summary>
    /// The longest, in seconds, a beat may have waited with others behind it before the TV counts as behind. Bursts of real
    /// play reach 6 beats queued and drain in about 4 s, and a collapse holds the stage for 4.7 s, so those play whole; a TV
    /// slower than the game goes past this and stays there.
    /// </summary>
    public const double MostLag = 8;

    private const int OpeningBeats = 2;     // the battle's start, and the first step if the AI goes first: all the game sends before it waits for the TV

    private readonly Queue<(Beat Beat, double Since)> _beats = new();
    private int _seen;
    private bool _fresh = true;
    private bool _missed;

    /// <summary>The number of beats waiting.</summary>
    public int Count => _beats.Count;

    /// <summary>The beats waiting, oldest first.</summary>
    public IEnumerable<Beat> Waiting => _beats.Select(b => b.Beat);

    /// <summary>
    /// Queues the beats of a view that haven't been seen, at time <paramref name="now"/>. Numbers that went backwards mean a
    /// new battle, or the same one started over: what's queued is dropped, and this returns true.
    /// </summary>
    public bool Offer(ImmutableArray<Beat> beats, double now = 0)
    {
        var fresh = _fresh;
        _fresh = false;
        if (beats.IsDefaultOrEmpty)
            return false;
        var restarted = beats[^1].Seq < _seen;
        if (restarted)
        {
            Clear();
            fresh = true;
            _fresh = false;
        }
        _missed |= beats[0].Seq > _seen + 1 || (fresh && beats[^1].Seq > OpeningBeats);
        foreach (var beat in beats.Where(b => b.Seq > _seen))
        {
            _beats.Enqueue((beat, now));
            _seen = beat.Seq;
        }
        return restarted;
    }

    /// <summary>
    /// True when the TV should drop back to the newest beat: beats went by unseen (a reconnect, or a scene joining a fight), or
    /// the beat that's next has waited over <see cref="MostLag"/> at time <paramref name="now"/> with others behind it.
    /// </summary>
    public bool Late(double now) => _missed || (_beats.Count > 1 && now - _beats.Peek().Since > MostLag);

    /// <summary>Drops everything but the newest beat.</summary>
    public void KeepNewest()
    {
        while (_beats.Count > 1)
            _beats.Dequeue();
        _missed = false;
    }

    /// <summary>The oldest beat waiting, or null.</summary>
    public Beat? Next() => _beats.TryDequeue(out var beat) ? beat.Beat : null;

    /// <summary>Forgets everything, as for a new battle.</summary>
    public void Clear()
    {
        _beats.Clear();
        _seen = 0;
        _fresh = true;
        _missed = false;
    }
}
