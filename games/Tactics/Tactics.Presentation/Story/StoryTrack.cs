using Tactics.Game;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Hud;

namespace Tactics.Presentation.Story;

/// <summary>What the TV does for the story, this view or frame.</summary>
/// <param name="Say">A line to say now, cutting whatever was speaking; null for none.</param>
/// <param name="Cut">Stop the voice now: a new line, or none showing. With <paramref name="Say"/>, cut first, then say it.</param>
/// <param name="Screen">What to show, or null with no scene.</param>
/// <param name="Tumbled">A roll the TV saw being made started to tumble: the dice's sound.</param>
/// <param name="Landed">The tumbling die landed: the verdict's sound.</param>
public sealed record StoryTick(StorySay? Say, bool Cut, StoryScreen? Screen, bool Tumbled = false, bool Landed = false);

/// <summary>
/// The story on the TV, view by view and frame by frame (overworld 5d §10). The server owns every clock and the TV sends nothing back, so this
/// only decides what to say and show of what it's told:
/// <list type="bullet">
/// <item>A new <see cref="StoryView.Seq"/> on a running track says its line, cutting the last; the newest wins, and a line missed is missed.</item>
/// <item>The first view of a track only shows: a TV joining mid-line, mid-question or mid-roll never starts a line partway (5d §8).</item>
/// <item>A line is never said twice on one track: a server restart that shows its saved line again, with a new Seq, shows it silently.</item>
/// <item>A Seq lower than the track's is a new story on this TV, another campaign, and starts a new track.</item>
/// <item>No line showing, or no story, cuts the voice.</item>
/// </list>
/// A line with no clip goes by <see cref="StoryVoice.Fallback"/>.
/// </summary>
public sealed class StoryTrack(VoiceClips? clips, HudArt? art = null, SpeechFallback fallback = StoryVoice.Fallback, StoryTiming? timing = null)
{
    private readonly StoryTiming _timing = timing ?? StoryTiming.Default;
    private readonly HashSet<string> _heard = new(StringComparer.Ordinal);
    private readonly VoteClock _clock = new();
    private StoryView? _view;
    private string? _speaking;
    private (int Seq, string State, string? Skill, bool Asking)? _question;
    private double _revealed = double.PositiveInfinity;

    /// <summary>The track's newest line number, or null before its first view.</summary>
    public int? Seq { get; private set; }

    /// <summary>What's showing, or null with no scene.</summary>
    public StoryScreen? Screen { get; private set; }

    /// <summary>
    /// A host view's story, as it comes (null outside a scene), at <paramref name="now"/> on the TV's clock, which only the question's countdown
    /// reads (<see cref="VoteClock"/>).
    /// </summary>
    public StoryTick Receive(StoryView? view, DateTimeOffset now)
    {
        var last = _view;
        _view = view;
        if (view is null)
        {
            (_question, _revealed, Screen) = (null, double.PositiveInfinity, null);
            _clock.Stop();
            return new StoryTick(null, Hush(), null);
        }

        var fresh = Seq is not { } seq || view.Seq < seq;
        if (Seq is { } old && view.Seq < old)
            _heard.Clear();                              // another story on this TV: what was heard was heard on the old one
        StorySay? say = null;
        var cut = false;
        var showing = Showing(view);
        if (!fresh && view.Seq > Seq && showing is not null && _heard.Add(showing.Id))
        {
            say = StoryVoice.For(showing, clips, fallback);
            cut = true;                                  // the last line stops for this one, said or not
            _speaking = say is null ? null : showing.Id;
        }
        else
        {
            if (showing is not null)
                _heard.Add(showing.Id);                  // shown is as good as heard: a TV that joined on it mustn't say it later
            if (_speaking is not null && showing?.Id != _speaking)
                cut = Hush();
        }
        Seq = view.Seq;

        Clock(view, now, fresh);
        var tumbled = Rolling(view, last, fresh);
        Screen = Draw();
        return new StoryTick(say, cut, Screen, Tumbled: tumbled);
    }

    /// <summary>A frame of <paramref name="seconds"/>: the countdown and the die go on. Nothing is said here.</summary>
    public StoryTick Tick(double seconds)
    {
        _clock.Tick(seconds);
        var landed = false;
        if (_revealed < StoryScreen.Tumble)
        {
            _revealed += Math.Max(0, seconds);
            landed = _revealed >= StoryScreen.Tumble;
        }
        Screen = _view is null ? null : Draw();
        return new StoryTick(null, false, Screen, Landed: landed);
    }

    /// <summary>The line the view shows, or null in a gap or a roll's reveal.</summary>
    private static StoryLineView? Showing(StoryView view)
        => view.State is TacticsGame.StoryStates.Gap or TacticsGame.StoryStates.Rolled ? null : view.Line;

    /// <summary>Stops the voice, if this track started it: true when there's anything to stop.</summary>
    private bool Hush()
    {
        var speaking = _speaking is not null;
        _speaking = null;
        return speaking;
    }

    /// <summary>
    /// Reads the question's deadline. One seen opening (a new question, on a track that saw the view before it) has its whole length to run,
    /// unless the line asking it outlasts that: then its deadline is the line's end, and says nothing of the TV's clock.
    /// </summary>
    private void Clock(StoryView view, DateTimeOffset now, bool fresh)
    {
        var deadline = view.Ask?.Deadline ?? view.Check?.Deadline;
        var question = view.Ask is not null || (view.Check is { Roll: null })
            ? (view.Seq, view.State, view.Check?.Skill, view.Ask is not null)
            : ((int, string, string?, bool)?)null;
        var opened = !fresh && question is not null && question != _question && deadline != view.Until;
        _question = question;
        if (question is null || deadline is null)
        {
            _clock.Stop();
            return;
        }
        _clock.Read(deadline, now, Length(view).TotalSeconds, opened);
    }

    /// <summary>How long the question showing has in full (5d default 4).</summary>
    private TimeSpan Length(StoryView view) => view.State switch
    {
        TacticsGame.StoryStates.TieBreak => _timing.TieBreak,
        TacticsGame.StoryStates.PickRoller => _timing.Roller,
        _ when view.Ask is { Party: false } => _timing.Choice,
        _ => _timing.Vote,
    };

    /// <summary>
    /// A roll showing. It tumbles only when the view before it was the party picking who rolls: one the TV joined on, or that shows again after
    /// a restart, has already been rolled, and shows landed, so it can't look rolled twice.
    /// </summary>
    private bool Rolling(StoryView view, StoryView? last, bool fresh)
    {
        if (view.Check?.Roll is null)
        {
            _revealed = double.PositiveInfinity;
            return false;
        }
        if (last?.Check?.Roll is not null)
            return false;                                // already showing
        var seen = !fresh && last?.Check is { Roll: null };
        _revealed = seen ? 0 : double.PositiveInfinity;
        return seen;
    }

    private StoryScreen Draw() => StoryScreen.Of(_view!, art, _clock.Text, _revealed);
}
