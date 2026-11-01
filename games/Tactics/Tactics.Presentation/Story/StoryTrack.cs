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
/// <item>
/// A track is running once it has seen the story, or seen the party with no scene under way (walking, a fight, Muster): a TV that watched
/// the walk up to the "!" says the scene's first line. A TV whose first sight of the story is mid-line, mid-question or mid-roll, or that
/// comes in while a story waits to resume (Resuming), only shows: it never starts a line partway (5d §8).
/// </item>
/// <item>A line is never said twice in one story: a server restart that shows its saved line again, with a new Seq, shows it silently.</item>
/// <item>
/// Another story is another campaign (<see cref="StoryView.Campaign"/>), or a Seq well below the track's, as a new local campaign counts
/// from 1 again: what was heard of the last story says nothing of this one. A view one or two behind the newest is late, and is dropped.
/// </item>
/// <item>No line showing, or no story, cuts the voice.</item>
/// </list>
/// A line with no clip goes by <see cref="StoryVoice.Fallback"/>.
/// </summary>
public sealed class StoryTrack(VoiceClips? clips, HudArt? art = null, SpeechFallback fallback = StoryVoice.Fallback, StoryTiming? timing = null)
{
    /// <summary>How far behind the newest Seq a view can be and still be a late one of this story, not the start of another.</summary>
    public const int LateBy = 2;

    /// <summary>How many lines into a story a TV may first see it: a Seq this low, well behind the track's, is a story begun again.</summary>
    public const int FirstLines = 3;

    private readonly StoryTiming _timing = timing ?? StoryTiming.Default;
    private readonly HashSet<(string? Campaign, string Line)> _heard = [];
    private readonly VoteClock _clock = new();
    private StoryView? _view;
    private string? _speaking;
    private string? _campaign;
    private bool _watching;
    private (int Seq, string State, string? Skill, bool Asking)? _question;
    private double _revealed = double.PositiveInfinity;

    /// <summary>The track's newest line number, or null before its first view.</summary>
    public int? Seq { get; private set; }

    /// <summary>What's showing, or null with no scene.</summary>
    public StoryScreen? Screen { get; private set; }

    /// <summary>
    /// A host view, as it comes, at <paramref name="now"/> on the TV's clock: its story, and whether the TV is watching the party (any phase
    /// but Resuming), which is what lets the next scene's first line be said.
    /// </summary>
    public StoryTick Receive(TacticsHostView? host, DateTimeOffset now)
        => Receive(host?.Story, now, watching: host is not null && host.Phase != nameof(TacticsPhase.Resuming));

    /// <summary>
    /// A host view's story, as it comes (null outside a scene), at <paramref name="now"/> on the TV's clock, which only the question's countdown
    /// reads (<see cref="VoteClock"/>). <paramref name="watching"/>, with no story: the TV sees the party, with no story waiting to resume.
    /// </summary>
    public StoryTick Receive(StoryView? view, DateTimeOffset now, bool watching = false)
    {
        if (view is null)
        {
            _view = null;
            _watching = watching;
            (_question, _revealed, Screen) = (null, double.PositiveInfinity, null);
            _clock.Stop();
            return new StoryTick(null, Hush(), null);
        }

        var another = (view.Campaign is { } campaign && _campaign is { } was && campaign != was) || Restarted(view.Seq);
        if (!another && view.Seq < Seq)
            return new StoryTick(null, false, Screen);   // late: the newest has been shown
        var last = _view;
        _view = view;
        _campaign = view.Campaign ?? _campaign;
        if (another)
        {
            // Another story on this TV. What was heard is kept by campaign, so a late view of the last one can't be said again; a campaign
            // that started over under the same name (a new local one) has heard nothing yet.
            if (Restarted(view.Seq))
                _heard.RemoveWhere(h => h.Campaign == _campaign);
            Seq = null;
        }
        var fresh = Seq is null && !_watching;
        var started = Seq is null ? _watching : view.Seq > Seq;
        _watching = false;
        StorySay? say = null;
        var cut = false;
        var showing = Showing(view);
        if (!fresh && started && showing is not null && _heard.Add((_campaign, showing.Id)))
        {
            say = StoryVoice.For(showing, clips, fallback);
            cut = true;                                  // the last line stops for this one, said or not
            _speaking = say is null ? null : showing.Id;
        }
        else
        {
            if (showing is not null)
                _heard.Add((_campaign, showing.Id));                  // shown is as good as heard: a TV that joined on it mustn't say it later
            if (_speaking is not null && showing?.Id != _speaking)
                cut = Hush();
        }
        Seq = view.Seq;

        Clock(view, now, fresh);
        var tumbled = Rolling(view, last, fresh);
        Screen = Draw();
        return new StoryTick(say, cut, Screen, Tumbled: tumbled);
    }

    /// <summary>
    /// A story counting from its start again under the same campaign (a new local one): a Seq well behind the track's, and among a story's
    /// first lines. Within one campaign a Seq only goes up, across restarts too, so anything else that's behind is a late view.
    /// </summary>
    private bool Restarted(int seq) => seq < Seq - LateBy && seq <= FirstLines;

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
    /// Reads the question's deadline against the span the server says its clock ran (<see cref="StoryAskView.Span"/>). One seen opening (a new
    /// question, on a track that saw the view before it) has all of that span to run, which tells how far the TV's clock is off. A server that
    /// sends no span leaves the TV the default length, and a deadline that's the line's end then says nothing of the TV's clock.
    /// </summary>
    private void Clock(StoryView view, DateTimeOffset now, bool fresh)
    {
        var deadline = view.Ask?.Deadline ?? view.Check?.Deadline;
        var span = view.Ask?.Span ?? view.Check?.Span;
        var question = view.Ask is not null || (view.Check is { Roll: null })
            ? (view.Seq, view.State, view.Check?.Skill, view.Ask is not null)
            : ((int, string, string?, bool)?)null;
        var opened = !fresh && question is not null && question != _question && (span is not null || deadline != view.Until);
        _question = question;
        if (question is null || deadline is null)
        {
            _clock.Stop();
            return;
        }
        _clock.Read(deadline, now, span ?? Length(view).TotalSeconds, opened);
    }

    /// <summary>How long the question showing has in full by default (5d default 4), for a server that doesn't say.</summary>
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
