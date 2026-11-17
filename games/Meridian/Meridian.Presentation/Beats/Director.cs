using Meridian.Presentation.Board;

namespace Meridian.Presentation.Beats;

/// <summary>
/// Turns the stream of host views into beats and decides when the TV signals <c>presented { seq }</c> (spec §3.2; M4
/// plan, Task 4). The scene calls <see cref="Receive"/> with each view, <see cref="Tick"/> every frame and
/// <see cref="SpeechFinished"/> when Tiller stops talking; it draws <see cref="Current"/> at <see cref="Position"/>.
/// </summary>
/// <remarks>
/// The rules it keeps: a signal acknowledges only an event the TV drew, once, and never one it skipped. A newer event
/// cuts the current beat short (the game moved on without this TV); an empty queue clears it. The first view after a
/// connection is a fresh look at the room: its event is shown briefly, not replayed or read again.
/// </remarks>
public sealed class Director(MeridianRules rules, Action<int> signal)
{
    private bool _fresh = true;
    private int _lastSignalled;
    private double _timeline;   // position on the beat's timeline; holds while waiting for the voice
    private double _wall;       // time since the beat began
    private bool _voiceDone;

    /// <summary>
    /// A beat left unsignalled: a newer event replaced it, the queue emptied, or the connection dropped. Each one means the
    /// server moved on without this TV, which the end-to-end run counts (M4 plan, Task 11).
    /// </summary>
    public event Action<Beat>? Abandoned;

    /// <summary>The beat signalled last, for logging from the signal callback (by then <see cref="Current"/> is clear).</summary>
    public Beat? LastSignalled { get; private set; }

    /// <summary>The beat being shown, or null between beats.</summary>
    public Beat? Current { get; private set; }

    /// <summary>The newest view: the board draws its compartments, crew and tokens, tweening any changes.</summary>
    public MeridianHostView? Shown { get; private set; }

    /// <summary>The deck on screen. It changes only when a beat begins or between beats.</summary>
    public DeckId Deck { get; private set; } = DeckId.Habitat;

    /// <summary>The current step and how far through it the beat is (0 to 1).</summary>
    public (BeatStep Step, double Progress)? Position
    {
        get
        {
            if (Current is not { } beat)
            {
                return null;
            }
            var start = 0.0;
            foreach (var step in beat.Steps)
            {
                if (_timeline < start + step.Seconds || ReferenceEquals(step, beat.Steps[^1]))
                {
                    return (step, step.Seconds <= 0 ? 1 : Math.Clamp((_timeline - start) / step.Seconds, 0, 1));
                }
                start += step.Seconds;
            }
            return null;
        }
    }

    public void Receive(MeridianHostView view)
    {
        var fresh = _fresh;
        _fresh = false;
        if (fresh)
        {
            // A fresh look forgets what was signalled before: after a server restart the game resumes from a turn's
            // start and numbers its events from there again, below what this TV signalled (M6 plan, Task 5).
            _lastSignalled = 0;
        }
        Shown = view;
        if (view.Now is not { } now)
        {
            Abandon();        // nothing to present: whatever was playing was answered by the server's fallback
            Current = null;
            Deck = DeckLayout.ActiveDeck(view, Deck);
            return;
        }
        if (Current is { } playing && playing.Seq == now.Seq)
        {
            return;           // the same event, other things changed: they tween on the board
        }
        if (now.Seq <= _lastSignalled)
        {
            Current = null;   // already shown: never again
            return;
        }
        Abandon();
        Current = Beat.For(now, rules, brief: fresh);
        _timeline = 0;
        _wall = 0;
        _voiceDone = !Current.Speaks;
        Deck = DeckLayout.ActiveDeck(view, Deck);
    }

    /// <summary>The connection dropped: the next view is a fresh look, and the beat playing now is abandoned unsignalled.</summary>
    public void ConnectionLost()
    {
        _fresh = true;
        Abandon();
        Current = null;
    }

    private void Abandon()
    {
        if (Current is { } beat)
        {
            Abandoned?.Invoke(beat);
        }
    }

    /// <summary>Tiller finished the line for <paramref name="seq"/>.</summary>
    public void SpeechFinished(int seq)
    {
        if (Current?.Seq == seq)
        {
            _voiceDone = true;
        }
    }

    public void Tick(double seconds)
    {
        if (Current is not { } beat)
        {
            return;
        }
        _wall += seconds;
        var voiceAt = beat.VoiceAt;
        if (!_voiceDone && voiceAt is { } at && _wall >= at + beat.SpeakSeconds + Beat.VoiceGrace)
        {
            _voiceDone = true;   // a silent or slow voice doesn't hold the game
        }
        var limit = !_voiceDone && voiceAt is { } gate ? gate : beat.Seconds;
        _timeline = Math.Min(_timeline + seconds, limit);
        if (_timeline >= beat.Seconds - 1e-9)
        {
            _lastSignalled = beat.Seq;
            LastSignalled = beat;
            Current = null;
            signal(beat.Seq);
        }
    }
}
