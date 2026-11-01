using Godot;
using Tactics.Game;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Story;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// The story over the world (overworld 5d §10): the caption, the question, the skill check and the chapter's card, drawn from a
/// <see cref="StoryTrack"/>, which decides what's said and shown. It gives back each tick, for the scene to speak, play and log.
/// </summary>
public sealed partial class StoryLayer : Control
{
    private readonly StoryTrack _track;
    private readonly StoryPanel _caption;
    private readonly VoteBoard _vote;
    private readonly CheckBoard _check;
    private readonly ChapterBoard _card;

    public StoryLayer(string packRoot, UnitArt units, VoiceClips? voices)
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        var hud = HudArt.Load(packRoot, units);
        var art = new StoryArt(packRoot, hud, units);
        _track = new StoryTrack(voices, hud);
        _check = new CheckBoard(art);
        _caption = new StoryPanel(art);
        _vote = new VoteBoard(art);
        _card = new ChapterBoard();
        AddChild(_check);       // the check dims the world, under the caption and the question
        AddChild(_caption);
        AddChild(_vote);
        AddChild(_card);
    }

    /// <summary>The newest line number the track has seen.</summary>
    public int? Seq => _track.Seq;

    /// <summary>What's showing, or null with no scene.</summary>
    public StoryScreen? Screen => _track.Screen;

    /// <summary>Whether a skill check is up, which dims the world.</summary>
    public bool Dimmed => _track.Screen?.Dimmed == true;

    /// <summary>A host view, read at <paramref name="now"/> on the TV's clock.</summary>
    public StoryTick Receive(TacticsHostView view, DateTimeOffset now)
    {
        var tick = _track.Receive(view, now);
        _card.Show(ChapterCard.Of(view.Chapter));
        Paint(tick.Screen);
        return tick;
    }

    /// <summary>The TV's connection dropped: the next view only shows (<see cref="StoryTrack.Rejoin"/>).</summary>
    public void Rejoin() => _track.Rejoin();

    /// <summary>A frame.</summary>
    public StoryTick Step(double delta)
    {
        var tick = _track.Tick(delta);
        Paint(tick.Screen);
        return tick;
    }

    private void Paint(StoryScreen? screen)
    {
        _caption.Show(screen?.Caption, screen?.Interlude);
        _vote.Show(screen?.Vote);
        _check.Show(screen?.Check);
    }
}
