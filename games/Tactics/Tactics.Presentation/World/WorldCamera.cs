using System.Numerics;
using Tactics.Presentation.Board;

namespace Tactics.Presentation.World;

/// <summary>
/// The camera over a region: where the world's origin goes each frame, at a fixed scale, on whole layout pixels (overworld 5b §3).
/// It follows a point on the map with look-ahead, and keeps <see cref="WorldTuning.EdgeMargin"/> tiles from the region's edges, easing
/// into that limit rather than stopping dead. A region too small for the margins is centred on that axis.
/// <para>
/// Every filter here treats its input as moving in a straight line between frames, which makes its lag exact at any frame time: frames of
/// different lengths, a hitch included, can't shake a steadily walking party against the world.
/// </para>
/// <para>
/// Call <see cref="Follow"/> once a frame, <see cref="Hold"/> instead while a fight has the camera, and <see cref="Snap"/> for a cut.
/// </para>
/// </summary>
public sealed class WorldCamera
{
    private readonly int _width;
    private readonly int _height;
    private bool _placed;
    private Vector2 _centre;       // where the filters have got to, unclamped
    private Vector2 _look;
    private Vector2 _lastAim;
    private float _floor;          // the height the view is at
    private float _lastHeight;
    private float _edge = 1;       // how much of the clamp applies: none in a fight

    /// <summary>A camera over a region of <paramref name="width"/> by <paramref name="height"/> tiles.</summary>
    public WorldCamera(int width, int height)
    {
        (_width, _height) = (width, height);
        Fit = new BoardFit(Vector2.Zero, WorldTuning.Scale);
    }

    /// <summary>Where the world's art goes on the 1920x1080 layout, this frame.</summary>
    public BoardFit Fit { get; private set; }

    /// <summary>The map point at the middle of the view, clamp and all.</summary>
    public Vector2 Centre { get; private set; }

    /// <summary>
    /// Follows <paramref name="map"/>, the party's <see cref="WalkTrack.Focus"/>. <paramref name="heading"/> is
    /// <see cref="WalkTrack.Heading"/>: its length is how much to look ahead, and on a sustained walk it's the line whose side-to-side the
    /// camera ignores. The first call is a cut.
    /// </summary>
    public void Follow(Vector2 map, float height, Vector2 heading, double dt)
    {
        if (!_placed)
        {
            Snap(map, height);
            return;
        }
        if (dt <= 0)
            return;

        var fade = (float)Math.Exp(-dt / WorldTuning.LookTau);
        var look = Vector2.Lerp(heading * WorldTuning.LookAhead, _look, fade);
        var aim = map + look;
        Glide(aim, height, heading, dt);
        _look = look;
        Ease(1, dt);
        Show();
    }

    /// <summary>Glides to <paramref name="map"/> and stays, with no look-ahead and no clamp: a stage near the region's edge still sits in the middle.</summary>
    public void Hold(Vector2 map, float height, double dt)
    {
        if (!_placed)
            Snap(map, height);
        if (dt <= 0)
            return;
        var fade = (float)Math.Exp(-dt / WorldTuning.LookTau);
        _look *= fade;
        Glide(map, height, Vector2.Zero, dt);
        Ease(0, dt);
        Show();
    }

    /// <summary>Cuts to <paramref name="map"/>, for when the party was put somewhere rather than walked there.</summary>
    public void Snap(Vector2 map, float height)
    {
        _placed = true;
        (_centre, _lastAim, _look) = (map, map, Vector2.Zero);
        (_floor, _lastHeight) = (height, height);
        Show();
    }

    private void Glide(Vector2 aim, float height, Vector2 heading, double dt)
    {
        // Along the line of a walk the camera keeps up; across it, it takes the average. With no line the two are the same.
        var share = Math.Min(heading.Length(), 1);
        var along = share > 0 ? heading / heading.Length() : Vector2.UnitX;
        var across = new Vector2(-along.Y, along.X);
        var acrossTau = double.Lerp(WorldTuning.CameraTau, WorldTuning.CameraRoadTau, share);
        var a = Tracked(Vector2.Dot(_centre, along), Vector2.Dot(_lastAim, along), Vector2.Dot(aim, along), WorldTuning.CameraTau, dt);
        var b = Tracked(Vector2.Dot(_centre, across), Vector2.Dot(_lastAim, across), Vector2.Dot(aim, across), acrossTau, dt);
        _centre = along * a + across * b;
        _lastAim = aim;
        _floor = Tracked(_floor, _lastHeight, height, WorldTuning.HeightTau, dt);
        _lastHeight = height;
    }

    /// <summary>
    /// One step of a first-order follow, for an input that moved in a straight line from <paramref name="from"/> to <paramref name="to"/>
    /// over the step. The usual <c>lerp(value, to, 1 - exp(-dt / tau))</c> trails a moving input by an amount that changes with
    /// <paramref name="dt"/>, and that's what jitter is.
    /// </summary>
    internal static float Tracked(float value, float from, float to, double tau, double dt)
    {
        var fade = Math.Exp(-dt / tau);
        var kept = tau / dt * (1 - fade);
        return (float)(fade * value + (1 - kept) * to + (kept - fade) * from);
    }

    private void Ease(float wanted, double dt)
        => _edge = float.Lerp(wanted, _edge, (float)Math.Exp(-dt / WorldTuning.EdgeReturnTau));

    private void Show()
    {
        var clamped = new Vector2(Limited(_centre.X, _width), Limited(_centre.Y, _height));
        Centre = Vector2.Lerp(_centre, clamped, _edge);
        var area = BoardFit.Area;
        var art = BoardProjection.Corner(Centre.X, Centre.Y, _floor) * WorldTuning.Scale;
        var origin = new Vector2(area.X + area.Width / 2 - art.X, area.Y + area.Height / 2 - art.Y);
        Fit = new BoardFit(new Vector2(MathF.Round(origin.X), MathF.Round(origin.Y)), WorldTuning.Scale);
    }

    /// <summary>A clamp to the margins with its corners rounded off, so that the camera slows into the limit. It never passes the limit.</summary>
    private static float Limited(float value, int size)
    {
        var (low, high) = (WorldTuning.EdgeMargin, size - WorldTuning.EdgeMargin);
        if (low >= high)
            return size / 2f;
        var ease = Math.Min(WorldTuning.EdgeEase, (high - low) / 2);
        var raised = low + Rounded(value - low, ease);
        return high - Rounded(high - raised, ease);
    }

    /// <summary>The greater of a value and 0, with the corner rounded over <paramref name="ease"/> each way.</summary>
    private static float Rounded(float value, float ease)
        => value >= ease ? value : value <= -ease ? 0 : (value + ease) * (value + ease) / (4 * ease);
}
