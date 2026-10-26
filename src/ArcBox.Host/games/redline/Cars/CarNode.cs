using Godot;
using Redline.Presentation;

namespace ArcBox.Host.Games.Redline.Cars;

/// <summary>
/// One 2026 car in the scene. The node's origin is on the ground midway between the axles, +Z forward, +X to the car's
/// left (the <c>CarMeshBuilder</c> frame), so <see cref="Place"/> can put it straight onto a <see cref="TrackPose"/>.
/// Wheels are separate pivots that roll with distance travelled; a <see cref="SpinSmoke"/> sits at the rear axle.
/// </summary>
public partial class CarNode : Node3D
{
    /// <summary>Render layer for car geometry, so the contact-shadow decal (layer 1 only) never paints the car itself.</summary>
    public const uint CarLayer = 2;

    private readonly List<(Node3D Pivot, float Radius)> _wheels = [];
    private SpinSmoke? _smoke;

    public string Code { get; set; } = "";

    /// <summary>Metres along the race (not wrapped) and metres left of centre where the car currently is.</summary>
    public double RaceMeters { get; private set; }

    public double Lateral { get; private set; }

    /// <summary>The track frame under the car (before any spin yaw).</summary>
    public TrackPose TrackFrame { get; private set; }

    public void AddWheel(Node3D pivot, float radius)
    {
        _wheels.Add((pivot, radius));
        AddChild(pivot);
    }

    public void SetSmoke(SpinSmoke smoke)
    {
        _smoke = smoke;
        AddChild(smoke);
    }

    /// <summary>Puts the car on the track at (raceMeters, lateral) turned by <paramref name="yaw"/> radians.</summary>
    public void Place(IReadOnlyList<TrackSample> samples, double raceMeters, double lateral, double yaw)
    {
        RaceMeters = raceMeters;
        Lateral = lateral;
        TrackFrame = TrackPose.At(samples, raceMeters, lateral);
        var p = TrackFrame.Yawed(yaw);
        Transform = new Transform3D(
            new Basis(V(p.Left), V(p.Up), V(p.Forward)),
            V(p.Position));
    }

    /// <summary>Rolls the wheels by <paramref name="metres"/> travelled along the car's own forward axis.</summary>
    public void Roll(double metres)
    {
        foreach (var (pivot, radius) in _wheels)
        {
            var angle = pivot.Rotation.X + (float)(metres / radius);
            pivot.Rotation = new Vector3(Mathf.Wrap(angle, -Mathf.Pi, Mathf.Pi), 0, 0);
        }
    }

    public void Smoke(float intensity)
    {
        if (_smoke is not null)
        {
            _smoke.Intensity = intensity;
        }
    }

    private static Vector3 V(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
