using System.Collections.Immutable;
using System.Numerics;
using Tactics.Game;
using Tactics.Logic;
using Tactics.Presentation.Audio;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>A frame of the party in the world, ready to draw.</summary>
/// <param name="Units">The party, back to front.</param>
/// <param name="Fit">Where the world goes on the layout.</param>
/// <param name="Tile">Where the party is, on the map, for what to load next.</param>
/// <param name="Velocity">Which way it's going, in tiles a second.</param>
/// <param name="Footstep">The sound of a foot coming down this frame, or null.</param>
/// <param name="Cut">The camera jumped this frame, rather than moved.</param>
/// <param name="Walked">How far the leader has walked, in tiles.</param>
/// <param name="Leader">Where the leader is, in art pixels, before it goes on the screen's pixels.</param>
/// <param name="Bumped">The leader bumped a wall this frame (<see cref="WalkBump"/>).</param>
/// <param name="Bump">The bump's sound, the step of the leader's tile, to play at <see cref="WorldTuning.BumpDb"/>; null with no bump or no sound.</param>
public sealed record WalkFrame(ImmutableArray<BoardUnit> Units, BoardFit Fit, Vector2 Tile, Vector2 Velocity, string? Footstep, bool Cut, double Walked, Vector2 Leader,
    bool Bumped = false, string? Bump = null)
{
    /// <summary>Those of <see cref="Units"/> who aren't <see cref="BoardUnit.Dressing"/>: the party and the story's figures, who are what's kept in view.</summary>
    public ImmutableArray<BoardUnit> Walkers { get; init; } = Units;
}

/// <summary>
/// What a fight in the world (<see cref="StageDirector"/>) asks of the walk this frame.
/// </summary>
/// <param name="Hold">Where the camera holds, with no look-ahead and no clamp, or null to follow the party.</param>
/// <param name="Cuts">The director's <see cref="StageDirector.CameraCuts"/>: the camera cuts to <paramref name="Hold"/> each time it goes up.</param>
/// <param name="Walkers">Where the walkers are drawn instead of where the track has them, as they fall in; null for the track's.</param>
/// <param name="Hidden">The party isn't drawn: the fight's own units are.</param>
/// <param name="Shake">How far the camera is off its place, in art pixels: a heavy hit shakes the world and the board in it together.</param>
public sealed record WalkStage(StageCamera? Hold, int Cuts, IReadOnlyDictionary<string, UnitPlace>? Walkers, bool Hidden, Vector2 Shake)
{
    /// <summary>The stage of a <see cref="StageDirector"/> and the beat's shake.</summary>
    public static WalkStage Of(StageDirector director, Vector2 shake = default)
        => new(director.CameraTarget, director.CameraCuts, director.Walkers, !director.WalkersShown, shake);
}

/// <summary>
/// The party on the TV, frame by frame: a <see cref="WalkTrack"/>, a <see cref="WorldCamera"/> and the party's units, on one region's ground.
/// <see cref="Receive"/> takes each view, as it comes, and <see cref="Frame"/> runs once a frame, whether a view came or not. Nothing here
/// depends on a view having arrived, so footsteps go by the distance walked, and a slow stream of views only slows the picture.
/// </summary>
public sealed class WalkShow
{
    private const float TilesPerSecond = 1 / 0.3f;      // the server's walking pace: only for how far ahead to load

    private readonly BoardTerrain _terrain;
    private readonly UnitArt _art;
    private readonly SoundBank? _sounds;
    private readonly WalkTrack _track = new();
    private readonly WorldPeople? _people;
    private ImmutableHashSet<string> _peopleFlags = ImmutableHashSet<string>.Empty;
    private readonly WalkBump _bump = new();
    private readonly WorldCamera _camera;
    private readonly Func<Vector2, bool, Vector2> _pixels;

    private ImmutableArray<WalkerView> _party = [];
    private ImmutableArray<StoryMarker> _markers = [];
    private ImmutableArray<CastView> _cast = [];
    private bool _started;
    private int _epoch;
    private int _seq;
    private int _told;
    private int _cuts;
    private int _stageCuts;
    private int _skips;
    private double _footfalls;
    private bool _bumped;
    private IReadOnlyDictionary<string, UnitPlace> _places = new Dictionary<string, UnitPlace>();

    /// <summary>Where feet go on the screen's pixels (<see cref="WorldCamera.OnGround"/>): for what stands in the world beside the party.</summary>
    public Func<Vector2, bool, Vector2> Pixels => _pixels;

    /// <summary>The seconds it's been moved on by, which is the people's clock.</summary>
    public double Clock { get; private set; }

    /// <summary>The newest step this show has heard of.</summary>
    public int Seq => _seq;

    /// <summary>Whether the newest view had a direction held.</summary>
    public bool Held { get; private set; }

    /// <summary>The scene's cast standing in the world, as the newest view has it (<see cref="ExploreView.Cast"/>).</summary>
    public IReadOnlyList<CastView> Cast => _cast;

    /// <summary>The party, in the newest view's order.</summary>
    public IReadOnlyList<WalkerView> Party => _party;

    /// <summary>Where each walker was put by the last <see cref="Frame"/>, by <see cref="WalkerView.Id"/>: the track's places, without a bump's lean.</summary>
    public IReadOnlyDictionary<string, UnitPlace> Places => _places;

    /// <summary>A show on <paramref name="terrain"/>. With no <paramref name="sounds"/>, it's a silent one.</summary>
    public WalkShow(BoardTerrain terrain, UnitArt art, SoundBank? sounds, ImmutableArray<RegionPerson> people = default)
    {
        (_terrain, _art, _sounds) = (terrain, art, sounds);
        _people = people.IsDefaultOrEmpty ? null : new WorldPeople(people, terrain, art);
        _camera = new WorldCamera(terrain.Map.Width, terrain.Map.Height);
        _pixels = (feet, standing) => standing ? WorldCamera.OnGround(feet) : _camera.OnPixels(feet);
    }

    /// <summary>Takes a new view, and gives back the steps in it that this TV hadn't heard of. A view older than the last changes nothing.</summary>
    public IReadOnlyList<WalkLeg> Receive(ExploreView view)
    {
        if (_started && view.Epoch == _epoch && view.Seq < _seq)
            return [];

        if (!_started || view.Epoch != _epoch)
            _told = view.Seq;        // what's in a first view was walked before this TV came
        (_started, _epoch, _seq, _party, Held) = (true, view.Epoch, view.Seq, view.Party.IsDefault ? [] : view.Party, view.Held);
        _markers = view.Markers is { IsDefault: false } markers ? markers : [];
        if (view.Traders is { } traders)
            _markers = [.. _markers, .. traders.Where(t => Math.Max(Math.Abs(t.At.X - view.At.X), Math.Abs(t.At.Y - view.At.Y)) <= 1)
                .Where(t => !t.Open).Select(t => new StoryMarker("shop:" + t.Id, t.At, null, "shop"))];
        _cast = view.Cast is { IsDefault: false } cast ? cast : [];
        if (view.PeopleFlags is { } flags && !_peopleFlags.SetEquals(flags)) _peopleFlags = flags.ToImmutableHashSet(StringComparer.Ordinal);
        if (view.PeopleFlags is null) _peopleFlags = ImmutableHashSet<string>.Empty;
        if (view.Traders is { } openTraders)
            _cast = [.. _cast, .. openTraders.Where(t => t.Open && t.Sprite is not null).Select(t => new CastView("trader:" + t.Id, t.Id, t.Sprite!, t.At, view.At))];
        _track.Keep([.. _cast.Select(c => c.At)]);      // followers stand clear of the scene's cast
        _track.Receive(view);       // a press into a wall turns the leader to face it there
        _bumped |= _bump.Receive(view);     // and leans them into it, in Frame

        var told = _told;
        _told = Math.Max(_told, view.Seq);
        return view.Legs.IsDefault ? [] : [.. view.Legs.Where(l => l.Seq > told)];
    }

    /// <summary>Puts followers where a fight left them, after its new epoch (<see cref="WalkTrack.Seed"/>, <see cref="StageDirector.Seed"/>).</summary>
    public void Seed(IReadOnlyDictionary<string, UnitPlace> places) => _track.Seed(places);

    /// <summary>The TV's connection came back: what it missed is old news, so the next view only says how many walls had refused a step.</summary>
    public void Rebase()
    {
        _bump.Rebase();
        _bumped = false;
    }

    /// <summary>
    /// Moves everything on by <paramref name="seconds"/>. Until the world is <paramref name="revealed"/> nobody can see the party, so a bump
    /// then is dropped, sound and all.
    /// </summary>
    /// <para>
    /// With <paramref name="stage"/>, a fight in the world has the camera and the party's places: the camera holds on the stage (and cuts when
    /// the director says), and while the party falls in it glides rather than cutting to the track's new epoch.
    /// </para>
    public WalkFrame Frame(double seconds, bool revealed = true, WalkStage? stage = null)
    {
        Clock += seconds;
        _track.Advance(seconds);
        var places = _places = _track.Places(_terrain);
        var focus = _track.Focus;

        var cut = _track.Cuts != _cuts;
        var stageCut = stage is not null && stage.Cuts != _stageCuts;
        _stageCuts = stage?.Cuts ?? _stageCuts;
        if (stage?.Hold is { } hold)
        {
            if (stageCut)
                _camera.Snap(hold.Map, hold.Height);
            else
                _camera.Hold(hold.Map, hold.Height, seconds);
            cut = stageCut;
        }
        else if (cut && stage?.Walkers is null)
            _camera.Snap(focus.Map, focus.Height);
        else
        {
            cut = false;
            _camera.Follow(focus.Map, focus.Height, _track.Heading, seconds);
        }
        var footstep = Footstep(focus.Map, jumped: cut || _track.Skips != _skips);
        (_cuts, _skips) = (_track.Cuts, _track.Skips);

        if (!revealed)
            _bump.Cancel();
        _bump.Advance(seconds);
        var drawn = stage?.Walkers is { } falling ? Over(places, falling) : Nudged(places);
        var bumped = _bumped && drawn != places;     // a knock only with a lean to see
        _bumped = false;
        var bump = bumped ? _sounds?.Step(_terrain.Map, new Tile((int)MathF.Floor(focus.Map.X), (int)MathF.Floor(focus.Map.Y))) : null;

        ImmutableArray<BoardUnit> walkers = stage?.Hidden == true ? [] : [.. UnitPlan.Walkers(_party, drawn, _track.Poses, _art, _pixels)
            .Concat(StoryMarks.Of(_markers, _terrain, _art, _pixels).Select(m => m.Unit).OfType<BoardUnit>())
            .Concat(StoryMarks.Cast(_cast, _terrain, _art, _pixels)).OrderBy(u => u.Order)];
        var people = _people?.Units(Clock, focus.Feet, _markers, _pixels, _cast, _peopleFlags,
            new Tile((int)MathF.Floor(focus.Map.X), (int)MathF.Floor(focus.Map.Y))) ?? [];
        ImmutableArray<BoardUnit> units = people.IsEmpty ? walkers : [.. walkers.Concat(people).OrderBy(u => u.Order)];
        var fit = stage is { Shake: var shake } && shake != Vector2.Zero ? _camera.Fit with { Origin = _camera.Fit.Origin + shake * _camera.Fit.Scale } : _camera.Fit;
        return new WalkFrame(units, fit, focus.Map, _track.Heading * TilesPerSecond, footstep, cut, _track.Walked, focus.Feet, bumped, bump) { Walkers = walkers };
    }

    /// <summary>
    /// The places with the leader leaning into a wall, while a bump is under way. Only the leader moves, on top of the track's place for them,
    /// so the path, the followers and the camera never see it. The places are the track's own otherwise.
    /// </summary>
    private IReadOnlyDictionary<string, UnitPlace> Nudged(IReadOnlyDictionary<string, UnitPlace> places)
    {
        if (!_bump.Active || _party.FirstOrDefault(w => w.Leads) is not { } leader || !places.TryGetValue(leader.Id, out var at))
            return places;
        return new Dictionary<string, UnitPlace>(places) { [leader.Id] = at with { Map = at.Map + _bump.Offset } };
    }

    private static Dictionary<string, UnitPlace> Over(IReadOnlyDictionary<string, UnitPlace> places, IReadOnlyDictionary<string, UnitPlace> over)
    {
        var drawn = new Dictionary<string, UnitPlace>(places);
        foreach (var (id, place) in over)
            drawn[id] = place;
        return drawn;
    }

    /// <summary>One foot per so many tiles, however the frames fall. What the party skipped or was put past wasn't walked, so it makes no sound.</summary>
    private string? Footstep(Vector2 map, bool jumped)
    {
        var footfalls = Math.Floor(_track.Walked / WorldTuning.FootstepTiles);
        if (footfalls <= _footfalls)
            return null;
        _footfalls = footfalls;
        return jumped ? null : _sounds?.Step(_terrain.Map, new Tile((int)MathF.Floor(map.X), (int)MathF.Floor(map.Y)));
    }
}
