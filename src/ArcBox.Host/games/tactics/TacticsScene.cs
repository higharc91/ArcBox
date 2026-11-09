using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.Json;
using ArcBox.Host.Audio;
using ArcBox.Host.Core.Audio;
using ArcBox.Host.Core;
using ArcBox.Host.Shell;
using ArcBox.Sdk;
using Godot;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Beats;
using Tactics.Presentation.Board;
using Tactics.Presentation.Hud;
using Tactics.Presentation.Story;
using Tactics.Presentation.World;

namespace ArcBox.Host.Games.Tactics;

/// <summary>
/// ArcBox Tactics on the TV: the banner, and for each phase its table from <see cref="PhaseScreen"/> (the Muster roster,
/// the encounter list, the results), and in battle the board and its units (<see cref="BoardCanvas"/>). Everything worded or worked out
/// here comes from <c>Tactics.Presentation</c>; this class only draws it.
/// <para>
/// In battle, what's drawn is <see cref="BeatPlayer.Shown"/>, never the view: the view is already past the beat on screen.
/// That goes for the HUD too (<see cref="HudLayer"/>).
/// Each beat that finishes sends <c>shown</c>. That only ever shortens the game's wait, so nothing here can hold the game up.
/// </para>
/// <para>
/// In Exploration, the world (<see cref="WorldCanvas"/>) stands in for all of that. A view only feeds <see cref="WalkShow"/>, and the picture
/// moves on every frame in <see cref="WalkOn"/>, whether a view came or not. The world is built the first time it's wanted, and only once; it's kept out of
/// the way behind a fight or a list, and goes when the scene does.
/// </para>
/// <para>
/// A fight on a stamped map is drawn in the world (overworld 5c): <see cref="StageDirector"/> has every view and says what the fight looks
/// like, and <see cref="PhaseScreen"/> reads it, so the two agree. The board goes on the world (<see cref="WorldCanvas.ShowStage"/>), the beats
/// wait for the walk-in, the camera holds on the stage, the walk follows <see cref="StageDirector.Walk"/>, and the results go over the world.
/// While the world can't be drawn the plain board plays the fight, as it always did.
/// </para>
/// </summary>
public partial class TacticsScene : Control, IGameScene, IContentScene, ISignalingScene, IAudioScene, IConnectionScene
{
    private const string BasePack = "base";

    /// <summary>How many screenshots <c>--shots</c> takes of each kind of moment.</summary>
    private const int ShotsPerMoment = 6;

    /// <summary>With <c>--shots</c>, how many shots a walk leaves of the party on the move and at a stop, and how far apart and how long after stopping.</summary>
    private const int WalkShots = 24;
    private const double TilesBetweenShots = 24;
    private const double RestBeforeShot = 1.6;

    private BattlePresentationControls _presentationControls = null!;
    private RulesPack? _cinemaRules;
    private AbilityBanner _abilityBanner = null!;
    public BattleCinematics? CinematicsOverride { get; set; }
    public bool ReducedEffects { get; set; }

    private Label _meta = null!;
    private Label _title = null!;
    private Label _level = null!;
    private PanelContainer _notice = null!;
    private Label _noticeText = null!;
    private VBoxContainer _rows = null!;
    private Label _empty = null!;
    private Control _header = null!;
    private Control _table = null!;
    private Control _backdrop = null!;
    private WorldCanvas? _world;
    private string? _worldRegion;
    private string? _unreadableWorld;
    private WalkShow? _walk;
    private ThingShow? _things;
    private ThingArt? _thingArt;
    private ExploreView? _thingsFed;    // the last view the things were given
    private bool _thingsOff;            // the pack's art for things can't be read: they're left out, and nothing else stops
    private QuestHud? _questHud;
    private WorldHud? _worldHud;
    private ExploreView? _explore;
    private WalkStats? _walkStats;
    private WalkLog? _walkLog;
    private PhaseScreen? _screen;
    private bool _revealed;
    private bool _loadingShot;
    private bool _walking;
    private bool _battle;
    private readonly WorldCover _cover = new();
    private CoverLook? _coverShown;
    private double _shotAt;
    private double _stillFor;
    private double _lastWalked;
    private int _walkShots;
    private int _bumpShots;
    private Tone? _titleTone;
    private BoardCanvas _board = null!;
    private HudLayer? _hud;
    private BattleHud? _said;
    private string _banner = "";
    private ArtIndex? _art;
    private UnitArt? _unitArt;
    private BattleMap? _map;
    private BoardFit? _fit;
    private string? _unreadableMap;
    private Screenshotter? _shots;
    private IReadOnlyDictionary<string, string> _content = new Dictionary<string, string>();
    private Action<string, object> _signal = (_, _) => { };
    private string? _lastShot;
    private FxArt? _fx;
    private BeatPlayer? _player;
    private BeatLoop? _loop;
    private BoardTerrain? _terrain;
    private BoardChanges? _planned;
    private Narrator? _narrator;
    private TacticsAudio _audio = null!;
    private SoundBank? _sound;
    private VoiceClips? _voices;
    private TitleArt? _titleArt;
    private bool _soundRead;
    private ResultBanner _resultBanner = null!;
    private PanelContainer _caption = null!;
    private Label _captionText = null!;
    private BeatScript? _playing;
    private double _playingSince;
    private readonly Dictionary<string, int> _moments = new(StringComparer.Ordinal);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private StageDirector? _director;
    private readonly Dictionary<string, BattleMap?> _maps = new(StringComparer.Ordinal);
    private StagePlan? _stagePlan;
    private StageGround _groundShown = new();
    private readonly RepaintQueue _repaints = new();
    private BoardChanges? _stageTerrainFor;
    private bool _inPlace;              // the fight's board is on the world now
    private bool _overWorld;
    private ulong? _takeoverHeldSince;     // when the world began waiting to take a fight over, for its blocks at the stage (TellWorld)
    private bool _takeoverHeldSaid;
    private ExploreView? _fed;          // the last view the walk was given
    private (WorldState State, BoardTerrain? Terrain)? _worldSaid;
    private WalkFrame? _lastWalk;       // the camera, as of the last frame of the world
    private System.Numerics.Vector2 _stageShake;
    private StageStep _stageSeen;
    private ColorRect _stageFade = null!;

    /// <summary><c>--beat-log</c>: print a <c>BEAT</c> line with each beat's timing.</summary>
    public bool LogBeats { get; set; }

    /// <summary><c>--walk-log</c>: print a <c>WALK</c> line for each step, a <c>WALKAT</c> line with where the party is shown, and how smooth the walking was.</summary>
    public bool LogWalks { get; set; }
    private string? _mobLog;

    /// <summary><c>--story-log</c>: print a <c>STORY &lt;seq&gt; &lt;line&gt; said|shown</c> line for each line of a scene the TV takes.</summary>
    public bool LogStory { get; set; }

    private StoryLayer? _story;
    private readonly Dictionary<string, int> _storyShots = new(StringComparer.Ordinal);
    private (string Label, double Since)? _storyMoment;
    private bool _markerShot;

    public void SetNarrator(Narrator narrator) => _narrator = narrator;

    public void SetShots(Screenshotter? shots) => _shots = shots;

    /// <summary>The connection dropped: walls refused while the TV was away are old news, so the next view doesn't bump anyone.</summary>
    public void ConnectionLost()
    {
        _walk?.Rebase();
        _story?.Rejoin();       // the first view back only shows: no line is started partway
    }

    public void SetContentRoots(IReadOnlyDictionary<string, string> rootsByPackId)
    {
        _content = rootsByPackId;
        GD.Print($"Tactics content: {string.Join(", ", _content.Select(p => $"{p.Key} at {p.Value}"))}");
    }

    public Action<string, object> Signal { set => _signal = value; }

    public override void _Ready()
    {
        var preferenceStore = new SettingsStore(ProjectSettings.GlobalizePath("user://settings.json"));
        var preferences = preferenceStore.Load();
        CinematicsOverride = Enum.TryParse<BattleCinematics>(preferences.BattleCinematics, out var mode) && Enum.IsDefined(mode) ? mode : null;
        ReducedEffects = preferences.ReducedBattleEffects == true;
        if (Enum.TryParse<BattleCinematics>(System.Environment.GetEnvironmentVariable("ARCBOX_FX_CINEMATICS"), out var previewMode) && Enum.IsDefined(previewMode))
            CinematicsOverride = previewMode;
        if (System.Environment.GetEnvironmentVariable("ARCBOX_FX_REDUCED") is { } reducedPreview) ReducedEffects = reducedPreview == "1";
        var column = ShellLayout.Frame(this, 24);
        _backdrop = GetChild<Control>(0);

        _meta = TvText.Meta(Brand.Name);
        _title = TvText.Label("", ArcTheme.Bold, 42);
        _level = TvText.Label("1", ArcTheme.Display("Black", 0, 18, 14), 60);
        _header = ShellLayout.Header(Brand.Name.ToUpperInvariant(), _meta, _title, ShellLayout.StatBox(TvText.Meta("Party level", 16), _level, null, 220));
        column.AddChild(_header);

        _board = new BoardCanvas { Visible = false };
        AddChild(_board);
        MoveChild(_board, 1);   // over the background, under the column

        _abilityBanner = new AbilityBanner();
        AddChild(_abilityBanner);
        _presentationControls = new BattlePresentationControls(CinematicsOverride, ReducedEffects, (cinema, reduced) =>
        {
            (CinematicsOverride, ReducedEffects) = (cinema, reduced);
            if (_player is not null) _player.ReducedEffects = reduced;
            if (cinema == BattleCinematics.Off) _player?.ClearCinematics();
            preferenceStore.Update(s => s with { BattleCinematics = cinema?.ToString(), ReducedBattleEffects = reduced });
            _loop?.Guard(Redraw);
        });
        AddChild(_presentationControls);

        _stageFade = new ColorRect { Color = Colors.Black, Visible = false, MouseFilter = MouseFilterEnum.Ignore };
        _stageFade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_stageFade);
        MoveChild(_stageFade, 2);   // over the world and the board, under the header, the table and the HUD

        _noticeText = TvText.Label("", ArcTheme.SemiBold, 28, ArcTheme.Gold);
        _notice = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 28, 10), _noticeText);
        _notice.Visible = false;
        column.AddChild(_notice);

        _resultBanner = new ResultBanner();
        column.AddChild(_resultBanner);

        _rows = ArcTheme.VBox(0);
        _rows.SizeFlagsVertical = SizeFlags.ExpandFill;
        _empty = TvText.Label("", ArcTheme.Black, 72);
        _empty.HorizontalAlignment = HorizontalAlignment.Center;
        _empty.VerticalAlignment = VerticalAlignment.Center;
        _empty.SizeFlagsVertical = SizeFlags.ExpandFill;
        _empty.Visible = false;

        _table = ArcTheme.Panel(ArcTheme.GlassBox(null, 0, 0), ArcTheme.VBox(0, _rows, _empty));
        _table.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(_table);

        _captionText = TvText.Label("", ArcTheme.SemiBold, 34);
        _caption = ArcTheme.Panel(ArcTheme.GlassBox(ArcTheme.Gold, 28, 12), _captionText);
        _caption.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _caption.GrowHorizontal = GrowDirection.Both;
        _caption.GrowVertical = GrowDirection.Begin;
        _caption.OffsetBottom = -40;
        _caption.Visible = false;
        AddChild(_caption);

        _audio = new TacticsAudio();
        AddChild(_audio);
    }

    /// <summary>The narrator is the whole TV's, and a recorded line never goes stale, so whatever's left of this game's stops with it.</summary>
    public override void _ExitTree()
    {
        if (_narrator is { } narrator && IsInstanceValid(narrator))
        {
            narrator.StopAll();
            GD.Print("TACTICS left: narrator cleared");
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_battle && input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F6 })
        {
            _presentationControls.Toggle();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        _presentationControls.Visible = _battle;
        if (!_battle)
        {
            _board.SetCinematic(null, 1);
            if (_abilityBanner.Visible) _abilityBanner.ShowName(null);
            if (_world is { } restingWorld) restingWorld.Cinematic = null;
        }
        GiftAudio();
        if (_director is { } director)
        {
            TellWorld(director);
            director.Tick(delta);
            if (_inPlace && !director.InPlace)
            {
                LeaveStage();       // the wake's darkest moment, or a world that's gone
                (_battle, _coverShown) = (_board.Visible, null);
                if (_hud is not null)
                {
                    _hud.Visible = _board.Visible;
                }
                Cover();
            }
            _stageFade.Visible = director.Fade > 0;
            _stageFade.Color = new Color(0, 0, 0, director.Fade);
            StageMoments(director);
        }
        if (_loop is not null && (_board.Visible || _inPlace) && _director?.BeatsRun != false)
        {
            _loop.Step(delta);
        }
        if (_world is { Suspended: false } world)
        {
            WalkOn(world, delta);
        }
        if (_world is { Repainting: false, Plan: { } worldPlan } painting && _repaints.Take() is { } job)
        {
            painting.Repaint(() => job.Plan.WorldBlocks(worldPlan, job.Tiles, job.Changes));
        }
        if (_story is { } story)
        {
            PresentStory(story.Step(delta));
            StoryShot(delta);
        }
        if (_retreatVote is not null)
        {
            _retreatClock.Tick(delta);
            _hud?.ShowRetreat(RetreatPlan.Of(_retreatVote, _retreatClock.Text));
        }
        if (_inPlace)
        {
            _loop?.Guard(Redraw);       // the walk-in moves units every frame, and the camera moves the HUD's marks
        }
    }

    private readonly VoteClock _retreatClock = new();
    private RetreatView? _retreatVote;

    /// <summary>
    /// The party's vote to fall back, from the latest view (it isn't a beat, so it needn't wait for the TV to show one), with its clock read
    /// on the TV's own (<see cref="VoteClock"/>) and run down each frame.
    /// </summary>
    private void ShowRetreat(BattleView live)
    {
        var vote = live.Retreat;
        _retreatClock.Read(vote?.Deadline, DateTimeOffset.UtcNow, RetreatLength.TotalSeconds, opened: vote is not null && _retreatVote is null);
        _retreatVote = vote;
        _hud?.ShowRetreat(RetreatPlan.Of(vote, _retreatClock.Text));
    }

    private static readonly TimeSpan RetreatLength = TacticsTiming.Default.RetreatVote;

    /// <summary>Tells the director whether the world can be drawn, when that changes.</summary>
    private void TellWorld(StageDirector director)
    {
        var stageShown = true;
        if (_world is { Now: WorldCanvas.Stage.Open, Plan: { } plan } open && director.TakeoverOn(plan.Terrain) is { } camera)
        {
            var size = GetViewport().GetVisibleRect().Size;
            var map = plan.Terrain.Map;
            stageShown = open.Shows(WorldCamera.CutSeen(map.Width, map.Height, camera.Map, camera.Height, new System.Numerics.Vector2(size.X, size.Y)), camera.Map);
            var clock = Time.GetTicksMsec();
            _takeoverHeldSince ??= clock;
            if (stageShown)
            {
                GD.Print($"TACTICS takeover: the stage's blocks recorded, after {clock - _takeoverHeldSince} ms held");
                (_takeoverHeldSince, _takeoverHeldSaid) = (null, false);
            }
            else if (!_takeoverHeldSaid && clock - _takeoverHeldSince > 1000)
            {
                _takeoverHeldSaid = true;
                GD.Print($"TACTICS takeover held over 1 s: {open.Waiting} blocks still to record");
            }
        }
        else
        {
            (_takeoverHeldSince, _takeoverHeldSaid) = (null, false);    // no takeover waits (the fight ended, or took over): the next counts from its own start
        }
        var state = StageLayout.WorldOf(new WorldSeen(_world is not null, _world?.Now == WorldCanvas.Stage.Open,
            _world is { Plan: not null, Ground: not null }, _world?.Now == WorldCanvas.Stage.Failed, _unreadableWorld is not null, stageShown));
        (WorldState, BoardTerrain?) now = (state, state == WorldState.Ready ? _world!.Plan!.Terrain : null);
        if (_worldSaid is { } said && said.State == now.Item1 && ReferenceEquals(said.Terrain, now.Item2))
        {
            return;
        }
        _worldSaid = now;
        director.SetWorld(now.Item1, now.Item2);
        if (_screen is { } screen && director.InPlace != screen.InPlace)
        {
            Show(screen with { InPlace = director.InPlace }, again: true);       // the world took the fight over: its battle is older than the player's
        }
    }

    /// <summary>With <c>--shots</c>, a shot as a fight in the world moves from one step of its timeline to the next, and one mid-way through the walk-in.</summary>
    private void StageMoments(StageDirector director)
    {
        if (director.Stage != _stageSeen)
        {
            _stageSeen = director.Stage;
            GD.Print($"TACTICS stage {director.Stage}{(director.InPlace ? " in place" : "")}");
            if (director.Where is not null)
            {
                Shoot($"stage-{director.Stage.ToString().ToLowerInvariant()}");
            }
        }
        if (director.Stage == StageStep.Forming && director.Intro is { } intro && director.InPlace)
        {
            if (director.Places.Count > 0 && director.GridAlpha == 0)
            {
                Shoot("stage-walk-in");
            }
            else if (director.GridAlpha is > 0.3f and < 0.8f)
            {
                Shoot("stage-grid");
            }
            else if (director.Places.Keys.Any(id => !id.Value.StartsWith("hero", StringComparison.Ordinal)))
            {
                Shoot("stage-step-in");
            }
        }
    }

    /// <summary>A battle map of the pack, read once, or null if it can't be read.</summary>
    private BattleMap? MapFor(string id)
    {
        if (_maps.TryGetValue(id, out var known))
        {
            return known;
        }
        BattleMap? map = null;
        if (_content.TryGetValue(BasePack, out var root))
        {
            try
            {
                map = BoardMap.Parse(File.ReadAllText(Path.Combine(root, "rules", "maps", $"{id}.json")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException)
            {
                GD.PushWarning($"Tactics stage: can't read map '{id}': {ex.Message}");
            }
        }
        return _maps[id] = map;
    }

    /// <summary>Gives the walk the view the director lets it follow, and after a fight's new epoch, where the followers stood.</summary>
    private (int Epoch, string Person, long Seq)? _spokenBark;
    private (int Epoch, long Seq)? _spokenThing;

    private void FeedWalk()
    {
        if (_walk is not { } walk)
        {
            return;
        }
        var next = _director is { } director ? director.NextWalk(_fed, _worldRegion)
            : _explore is { } explore && !ReferenceEquals(explore, _fed) && explore.Region == _worldRegion ? new WalkNext(explore, null) : null;
        if (next is null)
        {
            return;
        }
        foreach (var leg in walk.Receive(next.View))
        {
            LogWalk(leg);
        }
        if (next.Seed is { } seed)
        {
            walk.Seed(seed);
        }
        if (next.View.Bark is { } bark && bark.Until > DateTimeOffset.UtcNow
            && _spokenBark != (next.View.Epoch, bark.Person, bark.Seq))
        {
            _spokenBark = (next.View.Epoch, bark.Person, bark.Seq);
            if (StoryVoice.For(bark.Line, _voices, SpeechFallback.Speech) is { } say)
                _narrator?.Say(say.Text, "tactics.npc." + bark.Line.Speaker.Replace('_', '-'), NarrationPriority.Question,
                    clip: say.Clip is { } clip ? Path.Combine(_content[BasePack], clip) : null);
        }
        if (next.View.ThingSpeech is { } thing && thing.Until > DateTimeOffset.UtcNow
            && _spokenThing != (next.View.Epoch, thing.Seq))
        {
            _spokenThing = (next.View.Epoch, thing.Seq);
            if (StoryVoice.For(thing.Line, _voices, SpeechFallback.Speech) is { } say)
                _narrator?.Say(say.Text, TacticsVoices.Narrator, NarrationPriority.Question,
                    clip: say.Clip is { } clip ? Path.Combine(_content[BasePack], clip) : null);
        }
        _fed = next.View;
    }

    /// <summary>
    /// The things to interact with, for a newly opened world: drawn with the pack's art for them, its effects and its sounds. With the art
    /// check (<c>--world-demo=things</c>), each is labelled with its key and where its picture came from.
    /// </summary>
    private ThingShow? ThingsFor(BoardTerrain terrain, UnitArt art)
    {
        if (_thingsOff || !_content.TryGetValue(BasePack, out var root))
        {
            return null;
        }
        try
        {
            _thingArt ??= ThingArt.Load(root);
            _fx ??= FxArt.Load(root);
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            _thingsOff = true;
            GD.PushError($"Tactics world: can't read the art for things, so they're left out: {ex.Message}");
            return null;
        }
        return new ThingShow(terrain, art, _thingArt, _fx, _sound, _thingCaptions);
    }

    /// <summary>Labels every thing with its art key and source: the art check's (<see cref="ThingsDemo"/>).</summary>
    public bool ThingCaptions
    {
        get => _thingCaptions;
        set
        {
            _thingCaptions = value;
            if (_things is { } things)
            {
                things.Captions = value;
            }
        }
    }

    private bool _thingCaptions;

    /// <summary>
    /// Draws wall over each gap the server says a secret door nobody has found stands in (<see cref="WallPlugs"/>): built off the main thread
    /// when the plugs change, which is only when a door is found, and swapped in when it's ready. Meanwhile the last stay.
    /// </summary>
    private void Plug(WorldCanvas world, ImmutableArray<WallPlug> plugs, IReadOnlyList<string> storyFlags)
    {
        if (_plugBuild is { IsCompleted: true } done)
        {
            _plugBuild = null;
            if (done.IsCompletedSuccessfully && ReferenceEquals(done.Result.World, world))
            {
                world.SetPlugs(done.Result.Items);
            }
            else if (!done.IsCompletedSuccessfully)
            {
                GD.PushError($"Tactics world: the wall over a gap can't be built: {done.Exception?.GetBaseException().Message}");
            }
        }
        if (_plugBuild is not null || (plugs.SequenceEqual(_plugsAsked) && storyFlags.SequenceEqual(_sceneryFlagsAsked)) || world.Plan is not { } plan || world.Art is not { } art)
        {
            return;
        }
        _plugsAsked = plugs;
        _sceneryFlagsAsked = [.. storyFlags];
        var flags = storyFlags.ToHashSet(StringComparer.Ordinal);
        _plugBuild = Task.Run(() => (world, WallPlugs.Items(plan, art, plugs)
            .AddRange(LitBoard.Build(BoardPlan.StoryProps(plan.Terrain, art, flags), plan.Lighting).Items)
            .OrderBy(i => i.Order).ToImmutableArray()));
    }

    private Task<(WorldCanvas World, ImmutableArray<LitItem> Items)>? _plugBuild;
    private ImmutableArray<WallPlug> _plugsAsked = [];
    private ImmutableArray<string> _sceneryFlagsAsked = ["__initial__"];

    /// <summary>Gives the things each new view of this region, whatever the walk is following: they're drawn as the server says, all through a fight.</summary>
    private void FeedThings()
    {
        if (_things is { } things && _explore is { } explore && !ReferenceEquals(explore, _thingsFed) && explore.Region == _worldRegion)
        {
            things.Receive(explore);
            _thingsFed = explore;
        }
    }

    /// <summary>A frame of walking: everything that moves, moved. It touches no node that isn't moving, and it doesn't wait for anything.</summary>
    private void WalkOn(WorldCanvas world, double delta)
    {
        if (!Reveal(world, delta))
        {
            if (world.Now != WorldCanvas.Stage.Failed)
            {
                GD.PushError($"Tactics world: '{_worldRegion}' stalled or exceeded {WorldTuning.OpenMaxWait} s while opening (stage {world.Now}, progress {world.LoadingProgress}); going on without it");
            }
            _unreadableWorld = _worldRegion;
            CloseWorld();
            if (_screen is { } screen)
            {
                Show(screen);
            }
            return;
        }
        if (world.Plan is not { } plan || _unitArt is not { } art)
        {
            return;
        }
        if (_walk is null)
        {
            _walk = new WalkShow(plan.Terrain, art, _sound, world.Ground?.People ?? default);
            _fed = null;
            _things = ThingsFor(plan.Terrain, art);
            (_thingsFed, _plugsAsked, _sceneryFlagsAsked) = (null, [], ["__initial__"]);
        }
        world.CampLight(_camp, _campPhaseSeconds, _explore?.Morning == true, delta);
        var campActive = CampScene.Active(_camp);
        var coverLook = _cover.Look(_walking, _battle, _overWorld);
        _header.Visible = _table.Visible = !campActive && coverLook.HeaderAndTable;
        if (_worldHud is { } campWorldHud) campWorldHud.Visible = !campActive && coverLook.PanelsShown;
        FeedWalk();
        FeedThings();

        var frame = _lastWalk = _walk.Frame(delta, _cover.Reveal.Revealed, CampStage(plan.Terrain, delta, _director is { } director ? WalkStage.Of(director, _stageShake) : null));
        if (frame.Cut)
        {
            GD.Print(FormattableString.Invariant($"TACTICS walk snapped to {frame.Tile.X:0.0} {frame.Tile.Y:0.0}"));
        }
        frame = frame with { Units = CampScene.Pose(frame.Units, _camp, _campPhaseSeconds), Walkers = CampScene.Pose(frame.Walkers, _camp, _campPhaseSeconds) };
        world.Place(frame.Fit);
        world.Aim(frame.Tile, frame.Velocity);
        var things = _things?.Frame(delta, _walk.Pixels, world.View);
        Plug(world, things?.Plugs ?? [], _fed?.PeopleFlags ?? []);
        world.ShowUnits(things is { Units.IsEmpty: false } shown ? ThingShow.Merge(frame.Units, [.. shown.Units.Where(u => !CampScene.Active(_camp) || u.Id.Value != "thing:" + _camp!.Source)]) : frame.Units, frame.Walkers, art);
        world.ShowEffects([.. things?.Effects ?? [], .. MendDraws()]);
        foreach (var sound in things?.Sounds ?? [])
        {
            _audio.Play(sound, ThingSounds.Db);
        }
        if (frame.MobCry is { } cry) _audio.Play(cry, -8);
        if (frame.Footstep is { } step)
        {
            _audio.Play(step, WorldTuning.FootstepDb);
        }
        if (frame.Bumped)
        {
            Bumped(frame);
        }
        _worldHud?.Clear(frame.Walkers, frame.Fit);
        _questHud?.Frame(_explore, _walking && !_battle && !campActive, plan.Terrain, frame.Fit, delta, frame.Walkers);
        WalkMoments(world, frame, delta);
    }

    /// <summary>
    /// A frame of the screen over the world (<see cref="WorldCover"/>): the region's title card until the world is there to see. False when
    /// the world must be given up: it failed, or didn't open in time.
    /// </summary>
    private bool Reveal(WorldCanvas world, double delta)
    {
        var stage = world.Now switch
        {
            WorldCanvas.Stage.Open => WorldStage.Open,
            WorldCanvas.Stage.Failed => WorldStage.Failed,
            _ => WorldStage.Building,
        };
        if (!_cover.Frame(delta, stage, world.Lifted, world.LoadingProgress))
        {
            return false;
        }
        if (_shots is not null && !_loadingShot && world.Now == WorldCanvas.Stage.Near)
        {
            _loadingShot = true;
            _shots.Capture("tactics-world-loading");
        }
        Cover();
        return true;
    }

    /// <summary>Applies <see cref="WorldCover.Look"/> to the backdrop, header, table and the world's panels. Nodes are touched only when it changes.</summary>
    private void Cover()
    {
        var look = _cover.Look(_walking, _battle, _overWorld);
        if (look == _coverShown)
        {
            return;
        }
        _coverShown = look;
        var tint = new Color(1, 1, 1, look.Card);
        _backdrop.Visible = look.Backdrop;
        _header.Visible = _table.Visible = look.HeaderAndTable;
        _backdrop.Modulate = _header.Modulate = _table.Modulate = tint;
        if (_worldHud is { } hud)
        {
            hud.Visible = look.PanelsShown;
            hud.Modulate = new Color(1, 1, 1, look.Panels);
        }
    }

    private void WalkMoments(WorldCanvas world, WalkFrame frame, double delta)
    {
        if (!_revealed && world.Revealed)
        {
            _revealed = true;
            GD.Print($"TACTICS world open: {world.BuildTimes}, {world.Waiting} blocks to record, {WorldCanvas.MemoryText()}");
            _shots?.Capture("tactics-exploration");
        }
        var moved = frame.Walked != _lastWalked;
        if (_walkLog?.Next(_clock.Elapsed.TotalSeconds, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), _walk!.Seq, _walk.Held, _walk.Party, _walk.Places) is { } at)
        {
            GD.Print(at);
        }
        if (LogWalks && _walkStats is { } stats && frame.Units.FirstOrDefault(u => u.CrownAt is not null) is { } leader)
        {
            if (moved)
            {
                stats.Add(delta, System.Numerics.Vector2.Round(frame.Fit.ToScreen(frame.Leader)), frame.Fit.ToScreen(leader.Feet), frame.Fit.Origin);
            }
            else if (_stillFor == 0)
            {
                stats.Print();
            }
        }

        _stillFor = moved ? 0 : _stillFor + delta;
        _lastWalked = frame.Walked;
        if (_shots is null || !_revealed || _walkShots >= WalkShots)
        {
            return;
        }
        if (moved && frame.Walked >= _shotAt + TilesBetweenShots)
        {
            WalkShot(frame, "walking");
        }
        else if (!moved && _stillFor >= RestBeforeShot && frame.Walked > _shotAt)
        {
            WalkShot(frame, "stopped");
        }
    }

    /// <summary>The leader bumped a wall: its knock, a <c>BUMP</c> line with <c>--walk-log</c>, and with <c>--shots</c> a few shots, which land mid-lean.</summary>
    private void Bumped(WalkFrame frame)
    {
        if (frame.Bump is { } knock)
        {
            _audio.Play(knock, WorldTuning.BumpDb);
        }
        if (LogWalks)
        {
            GD.Print(FormattableString.Invariant($"BUMP {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()} {frame.Tile.X:0.00} {frame.Tile.Y:0.00}"));
        }
        if (_shots is not null && _bumpShots < ShotsPerMoment)
        {
            _bumpShots++;
            _shots.Capture(FormattableString.Invariant($"tactics-world-bump-{frame.Tile.X:0}-{frame.Tile.Y:0}"));
        }
    }

    private void WalkShot(WalkFrame frame, string moment)
    {
        _shotAt = frame.Walked;
        _walkShots++;
        _shots?.Capture(FormattableString.Invariant($"tactics-world-{moment}-{frame.Tile.X:0}-{frame.Tile.Y:0}"));
    }

    private void LogWalk(WalkLeg leg)
    {
        if (LogWalks)
        {
            GD.Print(FormattableString.Invariant($"WALK {leg.Seq} {leg.To.X} {leg.To.Y} {DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}"));
        }
    }

    private void Finished(int seq)
    {
        LogBeat(seq);
        _signal("shown", new { seq });
    }

    private void Present(BeatTick tick)
    {
        if (tick.CaughtUp is { } caughtUp)
        {
            GD.Print($"TACTICS catch-up to beat {caughtUp.Seq}, {caughtUp.Skipped} skipped");
        }
        if (tick.Started is { } started)
        {
            (_playing, _playingSince) = (started, _clock.Elapsed.TotalSeconds);
        }
        foreach (var line in tick.Lines)
        {
            _narrator?.Say(line.Text, line.Theme, NarrationPriority.Commentary, clip: line.Clip is { } clip ? Path.Combine(_content[BasePack], clip) : null);
        }
        foreach (var sound in tick.Sounds)
        {
            _audio.Play(sound);
        }
        foreach (var moment in tick.Moments)
        {
            Shoot(moment);
        }
        ShowCaption(_player?.Caption);
        if (tick.BoardChanged || tick.Busy)
        {
            Redraw();
        }
    }

    private void LogBeat(int seq)
    {
        if (LogBeats && _playing is { } beat)
        {
            var now = _clock.Elapsed.TotalSeconds;
            var kinds = string.Join(',', beat.Cues.OfType<ApplyCue>().Select(c => c.Event.GetType().Name).Distinct());
            var swings = beat.Cues.Count(c => c is LungeCue or FlightCue);
            GD.Print(FormattableString.Invariant($"BEAT {seq} from {_playingSince:0.000} to {now:0.000} took {now - _playingSince:0.000} scripted {beat.Length:0.000} swings {swings} waiting {_player?.Waiting} [{kinds}]"));
        }
    }

    private void Shoot(string moment)
    {
        var taken = _moments.GetValueOrDefault(moment);
        if (_shots is not null && taken < ShotsPerMoment)
        {
            _moments[moment] = taken + 1;
            _shots.Capture($"tactics-beat-{moment}-{_playing?.Seq}");
        }
    }

    private void ShowCaption(SpokenLine? line)
    {
        _caption.Visible = line is not null;
        if (line is not null)
        {
            _captionText.Text = line.Speaker is { } speaker ? $"{speaker}: {line.Text}" : line.Text;
        }
    }

    public void Render(JsonElement view, JsonElement? previous)
    {
        if (view.Deserialize<TacticsHostView>(ArcJson.Options) is not { } v)
        {
            return;
        }

        _director ??= new StageDirector(MapFor);
        _director.Receive(v, _walk?.Places);
        var director = _director;
        var screen = PhaseScreen.Of(v, director);
        TellStory(v);
        LogQuestState(v);
        QuestAudio(v);

        // The battle ended with beats still to play: they finish first, and then this screen. They were the game's last, so nothing waits on them.
        if (screen.Board is null && v.LastBattle is { } last && _player?.Shown is not null && _loop is not null)
        {
            _player.Receive(last);
            if (!_player.Idle)
            {
                _loop.Held = () =>
                {
                    // Made again as the stage is now: the world may have taken the fight over while the blows played, and the
                    // screen made when the results came would take it off again (StageScreenTests, results held for the last blow).
                    Show(PhaseScreen.Of(v, director));
                    director.LastBlowPlayed();
                };
                return;
            }
        }
        if (_loop is not null)
        {
            _loop.Held = null;
        }
        Show(screen);
        ShowShop(v);
        ShowCamp(v);
        director.LastBlowPlayed();      // nothing if the results aren't up, or have had it
    }

    /// <summary>
    /// Gives the story every host view, so it knows whether the TV watched the party walk up to a scene (overworld 5d §10). The layer is
    /// made with the pack's art the first time there's a world to tell a story in.
    /// </summary>
    private string? _questLogged;
    private void LogQuestState(TacticsHostView view)
    {
        if (!LogStory || view.Explore is not { } explore) return;
        var key = $"{explore.Pinned?.Id}|{explore.Pinned?.Objective}|{explore.QuestLog?.Selected}|{string.Join(',', explore.PeopleFlags ?? [])}";
        if (key == _questLogged) return;
        _questLogged = key;
        GD.Print(explore.Pinned is { } pin ? $"QUEST tracker {pin.Id} {pin.Status} {pin.Objective}" : "QUEST tracker none");
        GD.Print(explore.QuestLog is { } log ? $"QUEST journal {log.Selected}" : "QUEST journal closed");
        if (explore.PeopleFlags is { } flags) GD.Print($"QUEST people {string.Join(',', flags)}");
        if (explore.QuestLog is not null) _shots?.Capture("npcs-e2e-journal");
    }

    private void TellStory(TacticsHostView view)
    {
        if (_story is null)
        {
            if (view.Explore is null || !_content.TryGetValue(BasePack, out var root))
            {
                return;
            }
            LoadSound();
            try
            {
                _unitArt ??= UnitArt.Load(root);
                _story = new StoryLayer(root, _unitArt, _voices);
            }
            catch (Exception ex) when (ex is IOException or JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                GD.PushError($"Tactics story: can't draw it: {ex.Message}");
                return;
            }
            AddChild(_story);
        }
        if (StoryOrder.Move(_story.GetIndex(), _caption.GetIndex()) is { } to)
        {
            MoveChild(_story, to);      // just under the fight's caption, over the world and its panels, which the check and the card dim
        }
        if (!_markerShot && view.Explore?.Markers is { IsDefaultOrEmpty: false } marks && marks.Any(m => m.Emote is not null) && view.Story is null)
        {
            _markerShot = true;
            _storyMoment = ("story-marker", 0);
        }
        PresentStory(_story.Receive(view, DateTimeOffset.UtcNow));
        if (view.Chapter is not null && _storyMoment?.Label != "story-card")
        {
            _storyMoment = ("story-card", 0);
        }
        if (LogStory)
        {
            LogMarks(view);
        }
    }

    private string? _marksLogged;
    private string? _cardLogged;

    /// <summary>
    /// <c>--story-log</c>: a <c>STORY marker &lt;beat&gt; &lt;x&gt; &lt;y&gt; &lt;emote|-&gt;</c> line for each beat on offer whenever they
    /// change (<c>STORY marker none</c> for none), and <c>STORY card &lt;chapter&gt;</c> as a chapter's card comes up: for the bots to
    /// walk to the "!" and the e2e run to see the chapter end.
    /// </summary>
    private void LogMarks(TacticsHostView view)
    {
        if (view.Explore?.Markers is { IsDefault: false } marks)
        {
            var key = marks.IsEmpty ? "none" : string.Join(" | ", marks.Select(m => $"{m.Beat} {m.At.X} {m.At.Y} {m.Emote ?? "-"}"));
            if (key != _marksLogged)
            {
                _marksLogged = key;
                foreach (var line in key.Split(" | "))
                {
                    GD.Print($"STORY marker {line}");
                }
            }
        }
        if (view.Chapter != _cardLogged)
        {
            _cardLogged = view.Chapter;
            if (view.Chapter is not null)
            {
                GD.Print($"STORY card {view.Chapter}");
            }
        }
    }

    /// <summary>What a story tick asks: the voice cut, a line said, a sound played, a line logged with <c>--story-log</c>, and a shot.</summary>
    private void PresentStory(StoryTick tick)
    {
        if (tick.Cut && _narrator is { } narrator && StoryVoice.Owns(narrator.Speaking, narrator.LastLine, tick.Stopping))
        {
            narrator.StopAll();     // only the story's own line: commentary after it isn't the story's to stop
        }
        if (tick.Say is { } say)
        {
            var clip = say.Clip is { } file && _content.TryGetValue(BasePack, out var root) ? Path.Combine(root, file) : null;
            _narrator?.Say(say.Text, say.Theme, NarrationPriority.Question, clip: clip);
        }
        if (tick.Sound is { } sound)
        {
            _audio.Play(sound);
        }
        if (tick.Screen is not { } screen)
        {
            return;
        }
        if (screen.Seq != _missingStoryVoiceSeq && screen.Caption is { } caption && _voices?.ForLine(caption.Line) is null)
        {
            _missingStoryVoiceSeq = screen.Seq;
            GD.Print($"TACTICS missing voice clip: {caption.Line}");
        }
        if (LogStory && (tick.Say is not null || screen.Seq != _storyLogged))
        {
            _storyLogged = screen.Seq;
            GD.Print($"STORY {screen.Seq} {screen.Caption?.Line ?? "-"} {(tick.Say is null ? "shown" : "said")} {screen.State}");
        }
        if (Moment(screen, tick) is { } moment && moment != _storyMoment?.Label)
        {
            _storyMoment = (moment, 0);
        }
    }

    private int _storyLogged = -1;
    private int _missingStoryVoiceSeq = -1;

    /// <summary>The screenshot a story screen is worth, by what it shows: once or twice each, so a run's shots cover every panel.</summary>
    private static string? Moment(StoryScreen screen, StoryTick tick) => screen switch
    {
        { Check.Roll: { Landed: true } roll } => roll.Success ? "story-roll-pass" : "story-roll-fail",
        { Check.Roll: not null } => tick.Tumbled ? "story-roll-tumbling" : null,
        { Check: not null } => "story-check",
        { Vote.Tie: true } => "story-tie",
        { Vote: { Party: false } vote } => vote.Rows.Any(r => r.Voters.Length > 0) ? "story-choice-made" : "story-choice",
        { Vote: { Rows.Length: > 4 } } => "story-vote-many",
        { Vote: { } vote } => vote.Rows.Any(r => r.Voters.Length > 0) ? "story-vote-tally" : "story-vote",
        { Interlude: not null } => "story-gap",
        { Caption: { Narrator: true } } => "story-line-narrator",
        { Caption: not null } => "story-line-speaker",
        _ => null,
    };

    /// <summary>Takes the moment's shot once the screen has had a moment to settle, and the world is there to see behind it.</summary>
    private void StoryShot(double delta)
    {
        if (_shots is null || _storyMoment is not { } moment)
        {
            return;
        }
        _storyMoment = moment with { Since = moment.Since + delta };
        if (moment.Since < 0.8 || (_world is not null && !_cover.Reveal.Revealed) || (moment.Label == "story-marker" && _world is null))
        {
            return;
        }
        var taken = _storyShots.GetValueOrDefault(moment.Label);
        if (taken < 2)
        {
            _storyShots[moment.Label] = taken + 1;
            _shots.Capture($"tactics-{moment.Label}");
        }
        _storyMoment = (moment.Label, double.NegativeInfinity);
    }

    private void Show(PhaseScreen screen, bool again = false)
    {
        _screen = screen;
        LoadSound();
        _meta.Text = screen.Meta.ToUpperInvariant();
        _title.Text = screen.Title;
        if (_titleTone != screen.Tone)
        {
            _titleTone = screen.Tone;
            _title.AddThemeColorOverride("font_color", Accent(screen.Tone, Colors.White));
        }
        _level.Text = screen.PartyLevel.ToString();
        _empty.Visible = screen.Empty is not null;
        _empty.Text = screen.Empty ?? "";
        _rows.Visible = screen.Empty is null;
        _table.SizeFlagsVertical = _rows.SizeFlagsVertical = _empty.SizeFlagsVertical = screen.TableFills ? SizeFlags.ExpandFill : SizeFlags.ShrinkBegin;
        ShowRows(screen);
        _overWorld = screen.OverWorld;
        var walking = ShowWorld(screen);
        ShowBoard(screen, walking, again);
        _audio.Music(screen.Music);
        _audio.Ambience(screen.Ambience);
        _resultBanner.Show(screen.Banner is { } banner ? _titleArt?.Strip(banner) : null, _content.GetValueOrDefault(BasePack));
        _notice.Visible = screen.Notice is not null && !_board.Visible && !walking;
        _noticeText.Text = screen.Notice ?? "";
        _hud?.ShowNotice(screen.Notice);

        if (screen.Shot != _lastShot)
        {
            _lastShot = screen.Shot;
            GD.Print($"TACTICS screen {screen.Shot}");
            if (!walking || screen.Board is not null || screen.OverWorld)
            {
                _shots?.Capture(screen.Shot);       // the world takes its own, when there's something to see; a fight or its results in it, here
            }
        }
    }

    /// <summary>
    /// Shows the world for <paramref name="screen"/>, or puts it away. It's built once, whatever the views do; going back to it after a fight is only
    /// showing it again. False if there's no world on this screen, or none that can be drawn: then the header and table say where the party is.
    /// </summary>
    private bool ShowWorld(PhaseScreen screen)
    {
        var drawn = screen.World is { } explore && screen.Walk is { } hud && _unreadableWorld != explore.Region && DrawWorld(explore, hud, screen.Notice);
        if (drawn && _world is not null)
            _world.Weather = screen.Ambience == "amb_rain" ? screen.World?.Weather : null;
        if (!drawn && _world is { Suspended: false } world)
        {
            world.Suspend();
            GD.Print($"TACTICS world put away: {WorldCanvas.MemoryText()}");
        }
        return drawn;
    }

    private bool DrawWorld(ExploreView explore, WalkHud hud, string? notice)
    {
        if (!_content.TryGetValue(BasePack, out var root))
        {
            return false;
        }
        try
        {
            _unitArt ??= UnitArt.Load(root);
            if (_worldHud is null)
            {
                _worldHud = new WorldHud(root, HudArt.Load(root, _unitArt)) { Visible = false };
                _coverShown = null;
                AddChild(_worldHud);
                _questHud ??= new QuestHud(root, HudArt.Load(root, _unitArt));
                AddChild(_questHud);
                MoveChild(_worldHud, _caption.GetIndex());
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        {
            _unreadableWorld = explore.Region;
            GD.PushError($"Tactics world: can't draw '{explore.Region}': {ex.Message}");
            return false;
        }

        if (_world is not null && _worldRegion != explore.Region)
        {
            CloseWorld();
        }
        _explore = explore;
        if (LogWalks)
        {
            var mobs = JsonSerializer.Serialize(explore.Mobs ?? [], ArcJson.Options);
            if (mobs != _mobLog) { GD.Print("MOBS " + mobs); _mobLog = mobs; }
        }
        if (_world is null)
        {
            OpenWorld(root, explore.Region);
            _cover.Opened();
        }
        else if (_world.Suspended)
        {
            var fight = _director?.InWorld == true;     // a fight in the world has its own way in: no card, no fade
            _world.Resume(fade: !fight);
            if (!fight)
            {
                _cover.Opened();
            }
            GD.Print($"TACTICS world back: {WorldCanvas.MemoryText()}");
        }
        if (_unitArt is { } partyArt)
            _world?.PartySheets(explore.Party.Select(p => partyArt.Sprite(p.Sprite)?.Path).OfType<string>());
        FeedWalk();
        _worldHud.Show(hud, notice);
        return true;
    }

    private void OpenWorld(string root, string region)
    {
        _worldRegion = region;
        (_walk, _things) = (null, null);
        (_revealed, _loadingShot) = (false, false);
        _walkStats = LogWalks ? new WalkStats() : null;
        _walkLog = LogWalks ? new WalkLog() : null;
        _world = new WorldCanvas(root, () => DemoRegion.Load(root, region));
        (_stagePlan, _worldSaid, _fed) = (null, null, null);
        _groundShown = new StageGround();
        _repaints.Take();       // the old world's blocks are gone with it
        AddChild(_world);
        GD.Print($"TACTICS world opening {region}");
    }

    /// <summary>Frees the world. A build still running is told to stop, and goes nowhere: the node it was for has gone.</summary>
    private void CloseWorld()
    {
        if (_world is { } world)
        {
            RemoveChild(world);
            world.QueueFree();
        }
        (_world, _walk, _things, _worldRegion) = (null, null, null, null);
    }

    /// <summary>
    /// The pack's sound, voices and title art, each on its own, so one bad manifest costs only what it holds. What can't be read is
    /// done without: a TV with no sound must not stop anything.
    /// </summary>
    private void LoadSound()
    {
        if (_soundRead || !_content.TryGetValue(BasePack, out var root))
        {
            return;
        }
        _soundRead = true;
        _sound = Read(() => SoundBank.Load(root));
        _voices = Read(() => VoiceClips.Load(root));
        _titleArt = Read(() => TitleArt.Load(root));
        if (_sound is not null)
        {
            _audio.Use(root, _sound);
        }
        if (_titleArt is not null)
        {
            ShowLogo(Path.Combine(root, _titleArt.Logo));
        }
        _resultBanner.Shots = _shots;
    }

    private static T? Read<T>(Func<T> load) where T : class
    {
        try
        {
            return load();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or FormatException or ArgumentException
            or InvalidOperationException or KeyNotFoundException)
        {
            GD.PushWarning($"Tactics sound: can't read {typeof(T).Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The logo takes the place of the name's slab at the left of the header.</summary>
    private void ShowLogo(string path)
    {
        var image = File.Exists(path) ? Image.LoadFromFile(path) : null;
        if (image is null || image.IsEmpty())
        {
            return;
        }
        var logo = new TextureRect
        {
            Texture = ImageTexture.CreateFromImage(image),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            TextureFilter = TextureFilterEnum.Nearest,
            CustomMinimumSize = new Vector2(ShellLayout.HeaderHeight * image.GetWidth() / (float)image.GetHeight(), ShellLayout.HeaderHeight),
        };
        _header.GetChild<Control>(0).Visible = false;
        _header.AddChild(logo);
        _header.MoveChild(logo, 0);
    }

    /// <summary>The board replaces the header and table in battle. If it can't be drawn, they stay, with the encounter's name.</summary>
    private void ShowBoard(PhaseScreen screen, bool walking, bool again)
    {
        _banner = screen.Title;
        var layout = StageLayout.Of(screen, walking, _world is { Ground: not null, Plan: not null }, _player?.Shown is not null, again);
        if (layout.InPlace)
        {
            // On the world: the fight's board, or at its results the battle as it ended, which the player still has.
            _inPlace = true;      // so the board's first redraw is on the world, not the plain board's
            var staged = screen.Board is not { } fight || DrawBoard(fight, layout.FeedBoard);
            _board.Visible = false;
            if (staged && _board.Planned)
            {
                _board.LetGo();     // the world has its own copy of the fight's art: a TV back mid-fight drew it on the plain board first
                GD.Print($"TACTICS plain board let go: {WorldCanvas.MemoryText()}");
            }
            _inPlace = staged;
            if (!staged)
            {
                LeaveStage();
            }
            if (_hud is not null)
            {
                _hud.Visible = staged && layout.Hud;
            }
            _caption.Visible &= staged;
            (_walking, _battle) = (walking, staged && layout.Battle);
            _loop?.Guard(Redraw);
            Cover();
            return;
        }

        LeaveStage();
        var drawn = layout.PlainBoard && DrawBoard(screen.Board!, layout.FeedBoard);
        if (!drawn)
        {
            _player?.Reset();
            _caption.Visible = false;
        }
        _board.Visible = drawn;
        if (_hud is not null)
        {
            _hud.Visible = drawn;
        }
        (_walking, _battle) = (walking, drawn);
        Cover();
    }

    /// <summary>Puts the fight's board in the world away, and has the world's blocks a fight changed built again as the region has them.</summary>
    private void LeaveStage()
    {
        _inPlace = false;
        if (_stagePlan is { } plan && _world is { Plan: { } worldPlan } world)
        {
            var healed = _groundShown.Heal(plan.Stage);
            if (!healed.IsEmpty)
            {
                _repaints.Ask(plan, healed, null);
            }
            world.HideStage();
        }
        (_stagePlan, _stageShake, _stageTerrainFor) = (null, System.Numerics.Vector2.Zero, null);
        _groundShown = new StageGround();
    }

    private bool DrawBoard(BattleView battle, bool feed = true)
    {
        if (!_content.TryGetValue(BasePack, out var root))
        {
            return false;
        }
        try
        {
            _art ??= ArtIndex.Load(root);
            _unitArt ??= UnitArt.Load(root);
            _fx ??= FxArt.Load(root);
            _cinemaRules ??= RulesPack.Load([Path.Combine(root, "rules")]);
            _player ??= new BeatPlayer(_fx, _unitArt, _sound, _voices);
            _player.EnhancedEffects = true;
            _player.ReducedEffects = ReducedEffects;
            _player.CutIn = (beat, before, script) => _map is null ? null : CutInPlayback.For(beat, before, script, _cinemaRules,
                new BoardTerrain(_map, before.Board), BattleCutIn.Preference(Math.Clamp(before.Units.Count(u => u.Side == "Party" && u.Id.Value.StartsWith("hero", StringComparison.Ordinal) && int.TryParse(u.Id.Value.AsSpan(4), out _)), 1, 6), CinematicsOverride), _player.CarriedCamera);
            _loop ??= new BeatLoop(_player, Finished, Present, message => GD.PushError($"Tactics board: {message}"));
            if (_hud is null)
            {
                var hudArt = HudArt.Load(root, _unitArt);
                _hud = new HudLayer(root, hudArt, new StoryArt(root, hudArt, _unitArt));
                AddChild(_hud);
                MoveChild(_hud, _caption.GetIndex());   // over the board and under the caption
            }
            if (_map?.Id != battle.Map)
            {
                _map = BoardMap.Parse(File.ReadAllText(Path.Combine(root, "rules", "maps", $"{battle.Map}.json")));
                _fit = BoardFit.For(new BoardTerrain(_map, null), _art);   // once per map: a fight never moves the board
                _terrain = null;
                _player.Reset();
            }
            if (feed || _player.Shown is null)
            {
                _player.Receive(battle);
            }
            _loop.Guard(Redraw);     // not caught below: a redraw that fails isn't a board that can't be drawn
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException or ArgumentException
            or InvalidOperationException or KeyNotFoundException)
        {
            if (_unreadableMap != battle.Map)
            {
                _unreadableMap = battle.Map;
                GD.PushError($"Tactics board: can't draw map '{battle.Map}': {ex.Message}");
            }
            return false;
        }
    }

    /// <summary>Draws the battle as the player has seen it. The board's plan is only rebuilt when the board has changed.</summary>
    private void Redraw()
    {
        if (_inPlace)
        {
            RedrawStage();
            return;
        }
        if (_player?.Shown is not { } shown || _map is null || _fit is not { } fit || _art is null || _unitArt is null || !_content.TryGetValue(BasePack, out var root))
        {
            return;
        }
        if (_terrain is null || !_board.Planned || !DisplayedBattle.SameBoard(_planned, shown.Board))
        {
            _terrain = new BoardTerrain(_map, shown.Board);
            _planned = shown.Board;
            var plan = BoardPlan.Build(_terrain, _art);
            _board.SetPlan(plan, LitBoard.Build(plan, BoardLighting.Of(plan, _map.Id, _art.Lighting)), fit, root);
        }
        var frame = _player.Frame(_terrain);
        var framed = CinematicFit.Of(fit, _player.Camera);
        _board.SetFit(framed);
        _board.SetCinematic(_player.Camera, framed.Scale);
        _abilityBanner.ShowName(_player.AbilityBanner);
        var units = UnitPlan.Build(shown, _terrain, _unitArt, framed, frame.Places, frame.Poses);
        var (marked, draws) = MarkRule.Apply(units, frame, _unitArt, MarkRule.Shown(StageStep.Off, covered: false));
        _board.SetUnits(marked, _unitArt);
        _board.SetFlashes(frame.Flashes);
        _board.SetSpellLights(frame.Lights);
        _board.SetTelegraph(_player.Telegraph, _terrain);
        _board.SetEffects(draws, ReducedEffects ? System.Numerics.Vector2.Zero : frame.Shake);
        _said = BattleHud.Of(shown, _player.Live ?? shown, _banner, _said, _player.AiFinished);
        _hud?.Show(_said, units, framed, _player.AiFinished);
        _hud?.ShowCards(shown, _said.Lead, _player.Target, units, framed);
        ShowRetreat(_player.Live ?? shown);
    }

    /// <summary>
    /// Draws the battle as the player has seen it on the world: the stage's plan from the region's tiles, built again when the board changes, the
    /// world's blocks round a change built again off the frame, and the units with the director's walk-in over the beat's own places.
    /// </summary>
    private void RedrawStage()
    {
        if (_player?.Shown is not { } shown || _map is null || _art is null || _unitArt is null || _director?.Where is not { } where
            || _world is not { Ground: { } ground, Plan: { } worldPlan } world || _lastWalk is not { } walk || where.Map != _map.Id)
        {
            return;
        }
        var ask = _groundShown.Show(shown.Board, where);
        if (_stagePlan is null || _stagePlan.Stage != where)
        {
            _stagePlan = StagePlan.Build(ground, where, shown.Board, _art);
        }
        else if (ask.Restage)
        {
            _stagePlan = _stagePlan.With(shown.Board);
        }
        if (!ask.Blocks.IsEmpty)
        {
            _repaints.Ask(_stagePlan, ask.Blocks, _groundShown.World);
        }
        if (_terrain is null || !ReferenceEquals(_stageTerrainFor, shown.Board))
        {
            (_terrain, _stageTerrainFor) = (new BoardTerrain(_map, shown.Board), shown.Board);
        }
        if (world.ShowStage(_stagePlan) is not { } board)
        {
            return;
        }

        var director = _director;
        var beat = _player.Frame(_terrain);
        var camera = _player.Camera;
        world.Cinematic = camera is null ? null : camera with { Focus = camera.Focus + BoardFit.StageOffset(where) };
        world.Place(walk.Fit);
        var cameraFit = CinematicFit.Of(walk.Fit, world.Cinematic, new System.Numerics.Vector2(960, 540));
        var stage = StageFrame.Of(beat, director, where, cameraFit, _stageShake);
        board.SetCinematic(camera, stage.HudFit.Scale, parentDims: true);
        _abilityBanner.ShowName(_player.AbilityBanner);
        var units = stage.Shown(UnitPlan.Build(shown, _terrain, _unitArt, stage.Fit, stage.Places, stage.Poses, where.Origin));
        var (marked, draws) = MarkRule.Apply(units, beat, _unitArt, MarkRule.Shown(director.Stage, _overWorld));
        board.SetUnits(marked, _unitArt);
        board.SetFlashes(beat.Flashes);
        board.SetSpellLights(beat.Lights);
        board.SetTelegraph(_player.Telegraph, _terrain, where.Origin);
        board.SetEffects([.. draws.Select(d => d.AtWorldOrigin(where.Origin))], System.Numerics.Vector2.Zero);
        board.GridAlpha = director.GridAlpha;
        _stageShake = ReducedEffects ? System.Numerics.Vector2.Zero : beat.Shake;      // on the camera: the world and the board shake together
        _said = BattleHud.Of(shown, _player.Live ?? shown, _banner, _said, _player.AiFinished);
        _hud?.Show(_said, units, stage.HudFit, _player.AiFinished);
        _hud?.ShowCards(shown, _said.Lead, _player.Target, units, stage.HudFit);
        ShowRetreat(_player.Live ?? shown);
    }

    private void ShowRows(PhaseScreen screen)
    {
        if (screen.Rows.IsEmpty && _rows.GetChildCount() == 0)
        {
            return;     // a walk's views come 3 to 5 times a second, and there's nothing here to do
        }
        foreach (var child in _rows.GetChildren())
        {
            _rows.RemoveChild(child);
            child.QueueFree();
        }
        foreach (var row in screen.Rows)
        {
            _rows.AddChild(Row(row, Accent(screen.Tone, ArcTheme.Gold)));
        }
        if (screen.EmptySlots > 0)
        {
            var filler = ArcTheme.Spacer(false, true);
            filler.SizeFlagsStretchRatio = screen.EmptySlots;
            _rows.AddChild(filler);
        }
    }

    private static Color Accent(Tone tone, Color neutral) => tone switch
    {
        Tone.Good => ArcTheme.Good,
        Tone.Bad => ArcTheme.Bad,
        _ => neutral,
    };

    private static Control Row(ScreenRow row, Color accent)
    {
        var bar = new ColorRect { Color = accent, CustomMinimumSize = new Vector2(7, 54), SizeFlagsVertical = SizeFlags.ShrinkCenter };

        var name = TvText.Label(row.Title, ArcTheme.Bold, 46);
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.ClipText = true;
        var text = ArcTheme.VBox(0, name);
        text.Alignment = BoxContainer.AlignmentMode.Center;
        text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        if (row.Detail.Length > 0)
        {
            text.AddChild(TvText.Label(row.Detail, ArcTheme.SemiBold, 26, ArcTheme.Dim));
        }

        var content = ArcTheme.HBox(22, bar, text);
        if (row.Chip is { } chip)
        {
            var label = TvText.Label(chip, ArcTheme.Spaced("Black", 2), 18, new Color("15151e"));
            var panel = ArcTheme.Panel(ArcTheme.Flat(accent), ArcTheme.Margin(14, 3, 12, 3, label));
            panel.SizeFlagsVertical = SizeFlags.ShrinkCenter;
            content.AddChild(panel);
        }
        if (row.Value is { } value)
        {
            var label = TvText.Label(value, ArcTheme.Black, 56);
            label.HorizontalAlignment = HorizontalAlignment.Right;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.CustomMinimumSize = new Vector2(220, 0);
            content.AddChild(label);
        }

        var root = ArcTheme.Panel(LobbyScreen.BottomEdge(Colors.Transparent, 0, 0), ArcTheme.Margin(32, 0, 32, 0, content));
        root.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.Modulate = row.Dim ? new Color(1, 1, 1, 0.4f) : Colors.White;
        return root;
    }
}
