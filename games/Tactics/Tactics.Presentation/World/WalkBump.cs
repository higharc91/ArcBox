using System.Numerics;
using Tactics.Game;

namespace Tactics.Presentation.World;

/// <summary>
/// The leader bumping a wall (interaction review 5b, item 2): a press that does nothing mustn't look like letting go, or like a phone that
/// dropped. When <see cref="ExploreView.Bumps"/> goes up, the leader leans <see cref="WorldTuning.BumpTiles"/> toward the tile they face and
/// comes back, over <see cref="WorldTuning.BumpSeconds"/>. A first view, and a new epoch, only say where the count stands: a TV that comes
/// back, or a server that starts again, doesn't bump for a count from before. The offset is on the map, in tiles, for the leader alone.
/// </summary>
public sealed class WalkBump
{
    private bool _started;
    private int _epoch;
    private int _bumps;
    private Vector2 _toward;
    private double _since = double.PositiveInfinity;

    /// <summary>True while a bump is under way.</summary>
    public bool Active => _since < WorldTuning.BumpSeconds;

    /// <summary>How far the leader is pushed now, on the map, in tiles: zero when no bump is under way.</summary>
    public Vector2 Offset => Active ? _toward * (WorldTuning.BumpTiles * MathF.Sin((float)(_since / WorldTuning.BumpSeconds) * MathF.PI)) : Vector2.Zero;

    /// <summary>Takes a view. True when it starts a bump.</summary>
    public bool Receive(ExploreView view)
    {
        var known = _started && view.Epoch == _epoch;
        var bumped = known && view.Bumps > _bumps;
        (_started, _epoch, _bumps) = (true, view.Epoch, view.Bumps);
        if (!bumped)
            return false;

        var way = new Vector2(view.Facing.X - view.At.X, view.Facing.Y - view.At.Y);
        _toward = way == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(way);
        _since = 0;
        return true;
    }

    /// <summary>
    /// The TV's connection came back: the next view only says where the count stands, as a first view does, because what was refused while
    /// the TV was away is old news. A bump under way ends.
    /// </summary>
    public void Rebase() => (_started, _since) = (false, double.PositiveInfinity);

    /// <summary>Ends a bump under way, as if it had played: one nobody could see.</summary>
    public void Cancel() => _since = double.PositiveInfinity;

    /// <summary>Moves the bump on by <paramref name="seconds"/>.</summary>
    public void Advance(double seconds)
    {
        if (Active && seconds > 0)
            _since += seconds;
    }
}
