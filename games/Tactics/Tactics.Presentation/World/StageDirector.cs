using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>Where a fight in the world is in its timeline (overworld 5c §5). They only go forward, except for a new battle.</summary>
public enum StageStep
{
    /// <summary>No fight in the world: nothing seen yet, or a battle on a plain board.</summary>
    Off,
    /// <summary>The walk-in: heroes to their slots, the grid up, everyone else stepping in. Beats wait.</summary>
    Forming,
    /// <summary>The beats play, in place, or on the plain board while the world can't be drawn.</summary>
    Fighting,
    /// <summary>The last blow has played; the grid goes.</summary>
    Fading,
    /// <summary>The results, over the world and the final positions.</summary>
    Results,
    /// <summary>After a win or a flight: the walkers go to where they stand in the world.</summary>
    FallingIn,
    /// <summary>After a loss: a fade to the recovery spawn.</summary>
    Waking,
    /// <summary>Walking again: the world's party is the walk track's.</summary>
    Walking,
}

/// <summary>Whether the TV can draw the world a fight is in.</summary>
public enum WorldState
{
    /// <summary>It's still being built, or it's another region: the plain board shows the fight meanwhile.</summary>
    Loading,
    /// <summary>It's drawn.</summary>
    Ready,
    /// <summary>It can't be drawn: the plain board shows the fight, and keeps on showing it.</summary>
    Failed,
}

/// <summary>Where the camera holds (<see cref="WorldCamera.Hold"/>) while a fight has it, in world tiles.</summary>
public readonly record struct StageCamera(Vector2 Map, float Height);

/// <summary>
/// The timeline of a fight in the world (overworld 5c §5, §7), from the host views alone: into the fight, through it and out of it, and what
/// a TV that comes back mid-way shows. Pure: <c>TacticsScene</c> feeds it views and time and draws what it says.
/// <para>
/// Each frame: <see cref="Receive"/> any new view (with the walk track's places, for where the heroes set off from), <see cref="Tick"/>, then
/// read the rest. While <see cref="BeatsRun"/> is false the beat player isn't ticked, so beat 1's <c>shown</c> comes after the walk-in: that
/// only ever shortens the game's own 3 s wait (<c>TacticsTiming.FormUp</c>), as the walk-in is at most <see cref="StageTuning.FormingMost"/>.
/// Call <see cref="LastBlowPlayed"/> when the results screen is shown, which is once the last beat has played (<c>BeatLoop.Held</c>).
/// </para>
/// <para>
/// A TV coming back: mid-fight it starts at <see cref="StageStep.Fighting"/>, grid on and camera on the stage, and the beat queue catches up;
/// a first view with beats past the opening two skips the walk-in, as <see cref="Beats.BeatQueue"/> does; at the results it shows the world
/// at the party's new tile. While the world loads the plain board plays the fight, and the stage takes over with a fade once it's ready; a
/// world that can't be drawn leaves the fight on the plain board.
/// </para>
/// </summary>
/// <param name="maps">The battle maps, by id, for the stage's size.</param>
public sealed class StageDirector(Func<string, BattleMap?> maps)
{
    private const int OpeningBeats = 2;     // BeatQueue's: what the game sends before it waits for the TV

    private static readonly ImmutableDictionary<UnitId, UnitPlace> NoPlaces = ImmutableDictionary<UnitId, UnitPlace>.Empty;
    private static readonly ImmutableDictionary<UnitId, UnitPose> NoPoses = ImmutableDictionary<UnitId, UnitPose>.Empty;
    private static readonly ImmutableDictionary<string, UnitPlace> NoWalkers = ImmutableDictionary<string, UnitPlace>.Empty.WithComparers(StringComparer.Ordinal);

    private WorldState _world;
    private BoardTerrain? _terrain;
    private BattleView? _battle;         // the fight's newest view, then how it ended
    private int _seq;                    // its newest beat
    private bool _inPlace;               // the fight is drawn on the world
    private bool _fresh;                 // the results came with no fight before them
    private bool _awaitingBlow;          // the results are in, and the last beat is still playing
    private bool _dark;                  // the walk-in or the wake has passed its darkest moment
    private double _clock;               // seconds into the current step
    private double _takeover = -1;       // seconds since the world took over from the plain board, if it has
    private ExploreView? _latest;
    private ExploreView? _held;          // the party as it was when the fight began
    private bool _holding;
    private FallIn? _fall;

    /// <summary>Where the fight is in its timeline.</summary>
    public StageStep Stage { get; private set; }

    /// <summary>Where the fight stands in the world, or null with none.</summary>
    public BattleStage? Where { get; private set; }

    /// <summary>The walk-in, while it's planned.</summary>
    public BattleIntro? Intro { get; private set; }

    /// <summary>The fight is drawn on the world (<see cref="StagePlan"/>, <see cref="BoardFit.Stage"/>), rather than on the plain board or not at all.</summary>
    public bool InPlace { get; private set; }

    /// <summary>How much of the grid shows.</summary>
    public float GridAlpha { get; private set; } = 1;

    /// <summary>The fight's units not on the stage yet.</summary>
    public ImmutableHashSet<UnitId> Hidden { get; private set; } = [];

    /// <summary>The fight's units not on their tiles, in the battle map's tiles, for <see cref="UnitPlan.Build"/>.</summary>
    public ImmutableDictionary<UnitId, UnitPlace> Places { get; private set; } = NoPlaces;

    /// <summary>The fight's units walking in.</summary>
    public ImmutableDictionary<UnitId, UnitPose> Poses { get; private set; } = NoPoses;

    /// <summary>The beat player may play: false only during the walk-in.</summary>
    public bool BeatsRun { get; private set; } = true;

    /// <summary>How dark the screen is, over everything: 1 is black.</summary>
    public float Fade { get; private set; }

    /// <summary>Where the camera holds while the fight has it, or null to follow the party.</summary>
    public StageCamera? CameraTarget { get; private set; }

    /// <summary>Counts up each time the camera should cut to <see cref="CameraTarget"/> rather than glide there.</summary>
    public int CameraCuts { get; private set; }

    /// <summary>The world's walkers are drawn, rather than the fight's units.</summary>
    public bool WalkersShown { get; private set; } = true;

    /// <summary>The view the walk track should follow now: the party as it stood until the fight is over, so its new epoch waits for the results.</summary>
    public ExploreView? Walk { get; private set; }

    /// <summary>Where the walkers are drawn while they fall in, in world tiles; null for the walk track's own places.</summary>
    public ImmutableDictionary<string, UnitPlace>? Walkers { get; private set; }

    /// <summary>Where the followers stand after falling in, for <see cref="WalkTrack.Seed"/> once the track has taken <see cref="Walk"/>'s new epoch.</summary>
    public ImmutableDictionary<string, UnitPlace> Seed => _fall?.Seed ?? NoWalkers;

    /// <summary>
    /// Whether the world can be drawn, and its ground as drawn (<see cref="WorldPlan.Terrain"/>). A world of another region counts as loading.
    /// </summary>
    public void SetWorld(WorldState state, BoardTerrain? terrain = null)
    {
        (_world, _terrain) = (state, terrain ?? _terrain);
        if (state == WorldState.Failed && _inPlace)
        {
            _inPlace = false;
            if (Stage == StageStep.Forming)
                Go(StageStep.Fighting);
        }
        else if (Drawable && !_inPlace && !_fresh && Stage is StageStep.Fighting or StageStep.Fading or StageStep.Results)
        {
            (_inPlace, _takeover) = (true, 0);
            CameraCuts++;
        }
        Refresh();
    }

    /// <summary>
    /// A new view. <paramref name="walkers"/> is where the walk track has the party now (<see cref="WalkTrack.Places"/>, world tiles): where the
    /// heroes set off from if this view starts a fight.
    /// </summary>
    public void Receive(TacticsHostView view, IReadOnlyDictionary<string, UnitPlace>? walkers = null)
    {
        if (view.Explore is { } explore)
            _latest = explore;
        switch (view.Phase)
        {
            case nameof(TacticsPhase.Battle) or nameof(TacticsPhase.Resuming) when view.Battle is { } battle:
                Battle(battle, walkers, view.Phase == nameof(TacticsPhase.Resuming));
                break;
            case nameof(TacticsPhase.Results):
                Results(view.LastBattle);
                break;
            default:
                AfterFight();
                break;
        }
        Refresh();
    }

    /// <summary>The last beat has played and the results are up: the grid can go.</summary>
    public void LastBlowPlayed()
    {
        if (!_awaitingBlow)
            return;
        _awaitingBlow = false;
        if (Stage == StageStep.Fighting)
            Go(StageStep.Fading);
        Refresh();
    }

    /// <summary>Moves on by <paramref name="seconds"/>.</summary>
    public void Tick(double seconds)
    {
        if (seconds <= 0)
            return;
        _clock += seconds;
        if (_takeover >= 0)
            _takeover = _takeover + seconds >= StageTuning.Takeover ? -1 : _takeover + seconds;
        switch (Stage)
        {
            case StageStep.Forming when Intro is { } intro:
                if (intro.Fades && !_dark && _clock >= StageTuning.FadeDown)
                    (_dark, CameraCuts) = (true, CameraCuts + 1);
                if (_clock >= intro.End)
                    Go(StageStep.Fighting);
                break;
            case StageStep.Fading when _clock >= StageTuning.GridOut:
                Go(StageStep.Results);
                break;
            case StageStep.FallingIn when _clock >= StageTuning.FallIn:
                Finish();
                break;
            case StageStep.Waking:
                if (!_dark && _clock >= StageTuning.FadeDown)
                {
                    (_dark, _holding, _inPlace) = (true, false, false);
                    CameraCuts++;
                }
                if (_clock >= StageTuning.FadeDown + StageTuning.FadeUp)
                    Finish();
                break;
        }
        Refresh();
    }

    private bool Drawable => _world == WorldState.Ready && _terrain is not null && _battle?.Stage is { } stage && _terrain.Map.Id == stage.Region;

    private void Battle(BattleView battle, IReadOnlyDictionary<string, UnitPlace>? walkers, bool resuming)
    {
        var seq = battle.Beats.IsDefaultOrEmpty ? 0 : battle.Beats[^1].Seq;
        var fresh = Stage is not (StageStep.Forming or StageStep.Fighting) || seq < _seq || _battle?.Encounter != battle.Encounter
            || _battle?.Stage?.Origin != battle.Stage?.Origin;
        (_battle, _seq) = (battle, fresh ? seq : Math.Max(seq, _seq));
        if (!fresh)
            return;

        (Intro, _fall, _awaitingBlow, _fresh, _dark, _takeover, _inPlace) = (null, null, false, false, false, -1, false);
        if (battle.Stage is not { } view || maps(battle.Map) is not { } map)
        {
            Where = null;
            _holding = false;
            Go(StageStep.Off);
            return;
        }
        Where = new BattleStage(battle.Map, view.Origin, view.Height, map.Width, map.Height);
        (_holding, _held) = (true, _latest);
        if (!Drawable)
        {
            Go(StageStep.Fighting);
            return;
        }
        _inPlace = true;
        if (resuming || seq > OpeningBeats)
        {
            CameraCuts++;
            Go(StageStep.Fighting);
            return;
        }
        Intro = BattleIntro.Plan(battle, Where.Value, _terrain!, walkers);
        Go(StageStep.Forming);
    }

    private void Results(BattleView? last)
    {
        switch (Stage)
        {
            case StageStep.Forming or StageStep.Fighting:
                _battle = last ?? _battle;
                Intro = null;
                _awaitingBlow = true;
                if (Stage == StageStep.Forming)
                    Go(StageStep.Fighting);
                break;
            case StageStep.Fading or StageStep.Results:
                _battle = last ?? _battle;
                break;
            default:
                // A TV that never saw the fight: no beats are left, so the world shows the party where it is now, with the results over it.
                _battle = last;
                if (last?.Stage is not { } view || maps(last.Map) is not { } map)
                {
                    Where = null;
                    Finish();
                    break;
                }
                Where = new BattleStage(last.Map, view.Origin, view.Height, map.Width, map.Height);
                (_fresh, _holding, _inPlace, _fall, Intro) = (true, false, false, null, null);
                Go(StageStep.Results);
                break;
        }
    }

    private void AfterFight()
    {
        switch (Stage)
        {
            case StageStep.Forming or StageStep.Fighting or StageStep.Fading or StageStep.Results:
                if (_fresh || !_inPlace || Where is not { } stage || _latest is not { } explore || _terrain is null || _battle is null)
                {
                    Finish();
                    break;
                }
                Intro = null;
                if (explore.Arrived == "woke")
                {
                    _dark = false;
                    Go(StageStep.Waking);
                    break;
                }
                _fall = FallIn.Plan(_battle, stage, explore, _terrain);
                (_holding, _inPlace) = (false, false);
                Go(StageStep.FallingIn);
                break;
            case StageStep.Off:
                Finish();
                break;
        }
    }

    private void Finish()
    {
        (_holding, _inPlace, _awaitingBlow, Intro) = (false, false, false, null);
        Go(StageStep.Walking);
    }

    private void Go(StageStep step) => (Stage, _clock) = (step, 0);

    private void Refresh()
    {
        var frame = Stage == StageStep.Forming && Intro is { } intro && _terrain is not null ? intro.At(_clock, _terrain) : null;
        Places = frame?.Places ?? NoPlaces;
        Poses = frame?.Poses ?? NoPoses;
        Hidden = frame?.Hidden ?? [];
        BeatsRun = Stage != StageStep.Forming;
        InPlace = _inPlace && Stage is StageStep.Forming or StageStep.Fighting or StageStep.Fading or StageStep.Results or StageStep.Waking;
        GridAlpha = Stage switch
        {
            StageStep.Off => 1,
            StageStep.Forming => frame?.GridAlpha ?? 1,
            StageStep.Fighting => 1,
            StageStep.Fading => (float)Math.Clamp(1 - _clock / StageTuning.GridOut, 0, 1),
            _ => 0,
        };
        var fade = Stage switch
        {
            StageStep.Forming => frame?.Fade ?? 0,
            StageStep.Waking => _clock < StageTuning.FadeDown ? _clock / StageTuning.FadeDown : 1 - (_clock - StageTuning.FadeDown) / StageTuning.FadeUp,
            _ => 0,
        };
        if (_takeover >= 0)
            fade = Math.Max(fade, 1 - _takeover / StageTuning.Takeover);
        Fade = (float)Math.Clamp(fade, 0, 1);
        WalkersShown = Stage is StageStep.Off or StageStep.FallingIn or StageStep.Walking || (Stage == StageStep.Waking && _dark)
            || (Stage == StageStep.Results && _fresh);
        Walkers = Stage == StageStep.FallingIn ? _fall?.At(_clock) : null;
        Walk = _holding && _held is { } held && _latest is { } latest && latest.Epoch != held.Epoch ? held : _latest;
        CameraTarget = InPlace && Where is { } where && _terrain is not null ? Middle(where, _terrain) : null;
    }

    private static StageCamera Middle(BattleStage stage, BoardTerrain world)
    {
        var middle = new Vector2(stage.Origin.X + stage.Width / 2f, stage.Origin.Y + stage.Depth / 2f);
        var tile = new Tile((int)middle.X, (int)middle.Y);
        return new StageCamera(middle, world.Map.InBounds(tile) ? world.Top(tile) : stage.Height);
    }
}
