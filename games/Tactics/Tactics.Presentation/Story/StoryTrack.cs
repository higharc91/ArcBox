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
/// <param name="Stopping">With <paramref name="Cut"/>, the words of the story line that may still be speaking: the only voice to stop (<see cref="StoryVoice.Owns"/>).</param>
public sealed record StoryTick(StorySay? Say, bool Cut, StoryScreen? Screen, bool Tumbled = false, bool Landed = false, string? Stopping = null)
{
    /// <summary>The die rolling (<c>sfx</c>'s <c>Rolled</c> sound).</summary>
    public const string DiceSound = "ui_dice_roll";

    /// <summary>A roll that passed, as it lands.</summary>
    public const string PassSound = "ui_confirm";

    /// <summary>A roll that failed, as it lands.</summary>
    public const string FailSound = "ui_error";

    /// <summary>The sound effect to play this tick, or null: the dice as a roll the TV saw made starts to tumble, the verdict as it lands.</summary>
    public string? Sound => Tumbled ? DiceSound : Landed ? Screen?.Check?.Roll is { Success: var passed } ? passed ? PassSound : FailSound : null : null;
}

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
/// <item>A line is never said twice in one campaign: a server restart that shows its saved line again, with a new Seq, shows it silently.</item>
/// <item>
/// No view is dropped. A Seq lower than the track's within one campaign is the server's truth: it went back (a crash that lost its last
/// writes, then resumed on the saved line, or a rollback), so the view is shown, its question and clock with it, and what was heard keeps a
/// line from being said again. Another campaign (<see cref="StoryView.Campaign"/>) is another story, where nothing has been heard yet.
/// </item>
/// <item>After the TV's connection drops (<see cref="Rejoin"/>), the first view only shows, as a TV joining does.</item>
/// <item>No line showing, or no story, cuts the voice.</item>
/// </list>
/// A line with no clip goes by <see cref="StoryVoice.Fallback"/>.
/// </summary>
public sealed class StoryTrack(VoiceClips? clips, HudArt? art = null, SpeechFallback fallback = StoryVoice.Fallback, StoryTiming? timing = null)
{
    private readonly StoryTiming _timing = timing ?? StoryTiming.Default;
    private readonly HashSet<(string? Campaign, string Line)> _heard = [];
    private readonly VoteClock _clock = new();
    private StoryView? _view;
    private string? _speaking;
    private string? _speakingText;
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
            var ending = _speakingText;
            return Hush() ? new StoryTick(null, true, null, Stopping: ending) : new StoryTick(null, false, null);
        }

        var stopping = _speakingText;
        var another = view.Campaign is { } campaign && _campaign is { } was && campaign != was;
        var last = _view;
        _view = view;
        _campaign = view.Campaign ?? _campaign;
        if (another)
            Seq = null;                                  // another story on this TV: what was heard is kept by campaign, so it says nothing here
        var fresh = Seq is null && !_watching;
        var started = Seq is null ? _watching : view.Seq != Seq;    // a lower Seq too: the server went back, and shows another line
        _watching = false;
        StorySay? say = null;
        var cut = false;
        var showing = Showing(view);
        if (!fresh && started && showing is not null && _heard.Add((_campaign, showing.Id)))
        {
            say = StoryVoice.For(showing, clips, fallback);
            cut = true;                                  // the last line stops for this one, said or not
            (_speaking, _speakingText) = (say is null ? null : showing.Id, say?.Text);
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
        return new StoryTick(say, cut, Screen, Tumbled: tumbled, Stopping: cut ? stopping : null);
    }

    /// <summary>
    /// The TV's connection dropped: what it missed can't be caught up, so the next view only shows, as a TV joining does, and its line isn't
    /// started partway. What was heard stays heard.
    /// </summary>
    public void Rejoin() => (Seq, _watching) = (null, false);

    /// <summary>The words of the story line the track last started, or null: the only voice a <see cref="StoryTick.Cut"/> may stop (<see cref="StoryVoice.Owns"/>).</summary>
    public string? Speaking => _speakingText;

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
        (_speaking, _speakingText) = (null, null);
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
