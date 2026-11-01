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
public sealed record WalkFrame(ImmutableArray<BoardUnit> Units, BoardFit Fit, Vector2 Tile, Vector2 Velocity, string? Footstep, bool Cut, double Walked, Vector2 Leader);

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
    private readonly WorldCamera _camera;
    private readonly Func<Vector2, bool, Vector2> _pixels;

    private ImmutableArray<WalkerView> _party = [];
    private bool _started;
    private int _epoch;
    private int _seq;
    private int _told;
    private int _cuts;
    private int _skips;
    private double _footfalls;
    private IReadOnlyDictionary<string, UnitPlace> _places = new Dictionary<string, UnitPlace>();

    /// <summary>The newest step this show has heard of.</summary>
    public int Seq => _seq;

    /// <summary>Whether the newest view had a direction held.</summary>
    public bool Held { get; private set; }

    /// <summary>The party, in the newest view's order.</summary>
    public IReadOnlyList<WalkerView> Party => _party;

    /// <summary>Where each walker was put by the last <see cref="Frame"/>, by <see cref="WalkerView.Id"/>.</summary>
    public IReadOnlyDictionary<string, UnitPlace> Places => _places;

    /// <summary>A show on <paramref name="terrain"/>. With no <paramref name="sounds"/>, it's a silent one.</summary>
    public WalkShow(BoardTerrain terrain, UnitArt art, SoundBank? sounds)
    {
        (_terrain, _art, _sounds) = (terrain, art, sounds);
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
        _track.Receive(view);       // a press into a wall turns the leader to face it there
        // The wall bump (interaction review 5b, item 2) starts here, once the view says Blocked: the leader's nudge toward Facing goes on the
        // leader's place in Frame, after Places, so the track's path and the followers never see it, and its sound goes out as Footstep does.

        var told = _told;
        _told = Math.Max(_told, view.Seq);
        return view.Legs.IsDefault ? [] : [.. view.Legs.Where(l => l.Seq > told)];
    }

    /// <summary>Moves everything on by <paramref name="seconds"/>.</summary>
    public WalkFrame Frame(double seconds)
    {
        _track.Advance(seconds);
        var places = _places = _track.Places(_terrain);
        var focus = _track.Focus;

        var cut = _track.Cuts != _cuts;
        if (cut)
            _camera.Snap(focus.Map, focus.Height);
        else
            _camera.Follow(focus.Map, focus.Height, _track.Heading, seconds);
        var footstep = Footstep(focus.Map, jumped: cut || _track.Skips != _skips);
        (_cuts, _skips) = (_track.Cuts, _track.Skips);

        var units = UnitPlan.Walkers(_party, places, _track.Poses, _art, _pixels);
        return new WalkFrame(units, _camera.Fit, focus.Map, _track.Heading * TilesPerSecond, footstep, cut, _track.Walked, focus.Feet);
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
