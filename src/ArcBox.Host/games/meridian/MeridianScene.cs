using System.Text.Json;
using ArcBox.Host.Audio;
using ArcBox.Host.Core.Audio;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using ArcBox.Sdk.Content;
using Godot;
using Meridian.Game;
using Meridian.Logic;
using Meridian.Presentation;
using Meridian.Presentation.Art;
using Meridian.Presentation.Audio;
using Meridian.Presentation.Beats;
using Meridian.Presentation.Breach;
using Meridian.Presentation.Debrief;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// Derelict: ISV Meridian's TV scene (spec §5.5). It only animates: every rule and outcome is decided by the game, and
/// every decision about what to show is made by <c>Meridian.Presentation</c>, which this scene draws (M4 plan).
/// </summary>
/// <remarks>
/// Not an <c>IPreloadScene</c>: the scene builds in a frame, and keeping a finished one for a rematch would mean resetting
/// every piece of per-game state (the director, the camera, the shot marks) for no gain anyone would notice.
/// </remarks>
public partial class MeridianScene : Control, IGameScene, ISignalingScene, IContentScene, IAudioScene, IConnectionScene
{
    // Made with the scene: the shell gives it content before it enters the tree (Main.ShowGame).
    private readonly BoardCanvas _board = new();
    private readonly HudCanvas _hud = new();
    private readonly BreachOverlay _breach = new();
    private readonly DebriefCanvas _debrief = new();
    private Screenshotter? _shots;
    private Director _director = null!;
    private MeridianRules _rules = new();

    public Action<string, object> Signal { private get; set; } = (_, _) => { };

    public void SetShots(Screenshotter? shots) => _shots = shots;

    // ---- Tiller (M4 plan, Task 5): every line from the host view, so nothing secret is ever spoken.
    private Narrator? _narrator;
    private int _spokenSeq;
    private (int Seq, string Text)? _speaking;
    private HostWait? _spokenWait;

    public void SetNarrator(Narrator narrator)
    {
        if (_narrator is not null)
        {
            _narrator.LineFinished -= OnLineFinished;
        }
        _narrator = narrator;
        _narrator.LineFinished += OnLineFinished;
    }

    public override void _ExitTree()
    {
        if (_narrator is not null)
        {
            _narrator.LineFinished -= OnLineFinished;
        }
    }

    /// <summary>The narrator finished a line: if it was the one for the beat on screen, the beat may move on.</summary>
    private void OnLineFinished(string text)
    {
        if (_speaking is { } line && line.Text == text.Trim())
        {
            _director.SpeechFinished(line.Seq);
            _speaking = null;
        }
    }

    private void Speak()
    {
        if (_director.Current is { Speaks: true, Short: false } beat && beat.Seq != _spokenSeq && TillerLines.For(beat.Now) is { } line)
        {
            _spokenSeq = beat.Seq;
            if (_narrator is null)
            {
                return;   // no narrator: the beat waits its estimate, then moves on
            }
            _speaking = (beat.Seq, line.Text.Trim());
            _narrator.Say(line.Text, MeridianVoices.Tiller, line.Urgency == LineUrgency.CutIn ? NarrationPriority.Question : NarrationPriority.Commentary);
        }
        var wait = _director.Shown?.Wait;
        if (wait is { Kind: "pause" } && _spokenWait is null && TillerLines.ForWait(wait) is { } paused)
        {
            _narrator?.Say(paused.Text, MeridianVoices.Tiller, NarrationPriority.Commentary);
        }
        _spokenWait = wait;
    }

    /// <summary>The connection dropped: the next view is a fresh look at the room, not news (review focus 2).</summary>
    public void ConnectionLost()
    {
        _director.ConnectionLost();
        _speaking = null;
    }

    public void SetContentRoots(IReadOnlyDictionary<string, string> rootsByPackId)
    {
        if (!rootsByPackId.TryGetValue("base", out var root))
        {
            return;
        }
        try
        {
            var pack = PackLoader.Load(PhysicalFileSource.Instance, root);
            _rules = pack.Rules;
            var tiles = pack.StartTiles.Concat(pack.Tiles).GroupBy(t => t.Id.Value).ToDictionary(g => g.Key, g => g.First().Name);
            var crew = pack.Crew.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First().Id.Value);
            var art = MeridianArt.Load(root);
            foreach (var problem in art.Problems)
            {
                GD.PushWarning($"Meridian art: {problem}; drawing a placeholder");
            }
            _board.Use(id => tiles.GetValueOrDefault(id, id), name => crew.GetValueOrDefault(name), art);
        }
        catch (ContentException e)
        {
            GD.PushError($"Meridian content: {e.Message}; drawing placeholders by id");
        }
    }

    public override void _Ready()
    {
        _director = new Director(_rules, seq =>
        {
            // The end-to-end run reads these lines (tools/tv-e2e.sh, ARCBOX_GAME=meridian).
            GD.Print($"MERIDIAN presented {seq} {_director.LastSignalled?.Kind}");
            Signal(MeridianGame.PresentedSignal, new { seq });
        });
        _director.Abandoned += beat => GD.Print($"MERIDIAN abandoned {beat.Seq} {beat.Kind}");
        AddChild(new Starfield());
        _board.Use(_director);
        AddChild(_board);

        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);   // the starfield is the background, not the shell's backdrop
        _hud.Use(_director);
        AddChild(_hud);
        _breach.Use(_director, _board.RoomName);
        AddChild(_breach);
        _debrief.Use(_director);
        AddChild(_debrief);
    }

    public override void _Process(double delta)
    {
        _director.Tick(delta);
        Speak();
        Shoot();
    }

    // --shots: a capture when the screen changes, and part-way through the first beat of each kind and every eighth event.
    private readonly HashSet<string> _shotKinds = [];
    private int _shotSeq;
    private TvMode? _shotMode;
    private BreachStage? _shotStage;
    private bool _shotDebrief;
    private bool _loggedResume;

    private void Shoot()
    {
        if (_director.Shown is not { } view)
        {
            return;   // with or without --shots: the screen and Breach lines below are logged for the end-to-end run
        }
        // A restart's resume, for the end-to-end run: logged once each time it begins.
        var resuming = view.Wait?.Kind == "resume";
        if (resuming && !_loggedResume)
        {
            GD.Print($"MERIDIAN resuming {view.Wait!.Back ?? 0} of {view.Seats.Count}");
        }
        _loggedResume = resuming;
        var mode = TvScreen.Of(view).Mode;
        if (mode != _shotMode)
        {
            _shotMode = mode;
            GD.Print($"MERIDIAN screen {mode}");
            _shots?.Capture($"meridian-{mode.ToString().ToLowerInvariant()}");
        }
        // The Breach sequence's stages and the Debrief can pass quickly (bot phones are ready at once): one shot each.
        var stage = BreachSequence.Of(view, _director.Current, _director.Position)?.Stage;
        if (stage != _shotStage)
        {
            _shotStage = stage;
            if (stage is { } s && s != BreachStage.Lights)
            {
                GD.Print($"MERIDIAN breach {s}");
                _shots?.Capture($"meridian-breach-{s.ToString().ToLowerInvariant()}");
            }
        }
        if (_shots is null)
        {
            return;
        }
        if (DebriefModel.Of(view) is not null && !_shotDebrief)
        {
            _shotDebrief = true;
            _shots.Capture("meridian-debrief-reveal");
        }
        // The held moment of a spoken beat, otherwise half-way through the last step.
        if (_director.Current is { } beat && beat.Seq != _shotSeq && _director.Position is { Progress: >= 0.5 } at
            && ReferenceEquals(at.Step, beat.Steps.FirstOrDefault(s => s.Name == "hold") ?? beat.Steps[^1])
            && (_shotKinds.Add(beat.Kind) || beat.Seq % 8 == 0))
        {
            _shotSeq = beat.Seq;
            _shots.Capture($"meridian-{beat.Seq:000}-{beat.Kind}");
        }
    }

    public void Render(JsonElement view, JsonElement? previous)
    {
        if (view.Deserialize<MeridianHostView>(ArcJson.Options) is not { } v)
        {
            return;
        }
        _director.Receive(v);
    }
}
