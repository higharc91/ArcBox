using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation;

/// <summary>
/// A car's frame on the track in the mesh convention (Y up, +X east, +Z south): <see cref="Position"/> on the asphalt,
/// <see cref="Forward"/> along travel including the slope, <see cref="Left"/> level across the track and
/// <see cref="Up"/> = Forward × Left. Columns (Left, Up, Forward) are exactly the car mesh's (+X, +Y, +Z) axes, so they
/// form the car's rotation directly.
/// </summary>
public readonly record struct TrackPose(Vector3 Position, Vector3 Forward, Vector3 Left, Vector3 Up)
{
    /// <summary>Half the span used to measure heading and slope (about the car's wheelbase).</summary>
    public const double Span = 1.7;

    /// <summary>The pose at <paramref name="raceMeters"/> (wrapped to the lap) and <paramref name="lateral"/> metres left of centre.</summary>
    public static TrackPose At(IReadOnlyList<TrackSample> samples, double raceMeters, double lateral)
    {
        ArgumentNullException.ThrowIfNull(samples);
        var position = TrackMeshBuilder.TrackPoint(samples, raceMeters, lateral);
        var ahead = TrackMeshBuilder.TrackPoint(samples, raceMeters + Span, lateral);
        var behind = TrackMeshBuilder.TrackPoint(samples, raceMeters - Span, lateral);
        var forward = Vector3.Normalize(ahead - behind);
        var left = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, forward));
        var up = Vector3.Cross(forward, left);
        return new TrackPose(position, forward, left, up);
    }

    /// <summary>This pose turned by <paramref name="yaw"/> radians about <see cref="Up"/> (counter-clockwise seen from above).</summary>
    public TrackPose Yawed(double yaw)
    {
        if (yaw == 0)
            return this;
        var (sin, cos) = Math.SinCos(yaw);
        // Positive yaw turns Forward towards Left.
        var f = Vector3.Normalize(Forward * (float)cos + Left * (float)sin);
        var l = Vector3.Normalize(Left * (float)cos - Forward * (float)sin);
        return this with { Forward = f, Left = l };
    }
}
