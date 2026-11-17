using System.Text.Json;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using ArcBox.Sdk.Content;
using Godot;
using Meridian.Game;
using Meridian.Logic;
using Meridian.Presentation;
using Meridian.Presentation.Art;
using Meridian.Presentation.Beats;

namespace ArcBox.Host.Games.Meridian;

/// <summary>
/// Derelict: ISV Meridian's TV scene (spec §5.5). It only animates: every rule and outcome is decided by the game, and
/// every decision about what to show is made by <c>Meridian.Presentation</c>, which this scene draws (M4 plan).
/// </summary>
public partial class MeridianScene : Control, IGameScene, ISignalingScene, IContentScene
{
    private Label _banner = null!;
    private Label _mode = null!;
    private HBoxContainer _seats = null!;
    // Made with the scene: the shell gives it content before it enters the tree (Main.ShowGame).
    private readonly BoardCanvas _board = new();
    private Screenshotter? _shots;
    private Director _director = null!;
    private MeridianRules _rules = new();

    public Action<string, object> Signal { private get; set; } = (_, _) => { };

    public void SetShots(Screenshotter? shots) => _shots = shots;

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
        _director = new Director(_rules, seq => Signal(MeridianGame.PresentedSignal, new { seq }));
        AddChild(new Starfield());
        _board.Use(_director);
        AddChild(_board);

        // The starfield is the background (not the shell's broadcast backdrop), and the top bar keeps to the HUD band
        // DeckLayout leaves for it. Task 7 replaces this bar with the HUD.
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _banner = ArcTheme.Label("", ArcTheme.Bold, 30);
        _mode = ArcTheme.Meta("", 18);
        _seats = ArcTheme.HBox(18);
        var bar = ArcTheme.Margin(24, 10, 24, 0, ArcTheme.VBox(2, ArcTheme.HBox(16, _mode, _banner), _seats));
        bar.SetAnchorsAndOffsetsPreset(LayoutPreset.TopWide);
        AddChild(bar);
    }

    public override void _Process(double delta)
    {
        _director.Tick(delta);
        Shoot();
    }

    // --shots: a capture when the screen changes, and part-way through the first beat of each kind and every eighth event.
    private readonly HashSet<string> _shotKinds = [];
    private int _shotSeq;
    private TvMode? _shotMode;

    private void Shoot()
    {
        if (_shots is null || _director.Shown is not { } view)
        {
            return;
        }
        var mode = TvScreen.Of(view).Mode;
        if (mode != _shotMode)
        {
            _shotMode = mode;
            _shots.Capture($"meridian-{mode.ToString().ToLowerInvariant()}");
        }
        if (_director.Current is { } beat && beat.Seq != _shotSeq && _director.Position is { Progress: >= 0.5 } at
            && ReferenceEquals(at.Step, beat.Steps[^1]) && (_shotKinds.Add(beat.Kind) || beat.Seq % 8 == 0))
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
        var screen = TvScreen.Of(v);
        _banner.Text = screen.Banner;
        _mode.Text = screen.Mode.ToString().ToUpperInvariant();
        foreach (var child in _seats.GetChildren())
        {
            child.QueueFree();
        }
        foreach (var seat in screen.Seats)
        {
            var text = seat.Crew is { } crew ? $"{seat.Name} · {crew}" : seat.Name;
            _seats.AddChild(ArcTheme.Label(seat.Active ? $"▶ {text}" : text, ArcTheme.Bold, 22, ArcTheme.PlayerColour(seat.Colour)));
        }
    }
}
