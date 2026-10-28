using Godot;
using Redline.Presentation;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.Cars;

/// <summary>
/// One 2026 car in the scene. The node's origin is on the ground midway between the axles, +Z forward, +X to the car's
/// left (the <c>CarMeshBuilder</c> frame), so <see cref="Place"/> can put it straight onto a <see cref="TrackPose"/>.
/// Wheels are separate pivots that roll with distance travelled; a <see cref="SpinSmoke"/> sits at the rear axle. A
/// Blender car carries two LODs whose meshes swap by camera distance (<see cref="CarLod"/>, <see cref="SetLowDetail"/>).
/// </summary>
public partial class CarNode : Node3D
{
    /// <summary>Render layer for car geometry, so the contact-shadow decal (layer 1 only) never paints the car itself.</summary>
    public const uint CarLayer = 2;

    private readonly List<(Node3D Pivot, float Radius)> _wheels = [];
    private SpinSmoke? _smoke;
    private Node3D? _lod0Root;
    private Node3D? _lod1Root;
    private readonly List<GeometryInstance3D> _lod0 = [];
    private readonly List<GeometryInstance3D> _lod1 = [];

    public string Code { get; set; } = "";

    /// <summary>Metres along the race (not wrapped) and metres left of centre where the car currently is.</summary>
    public double RaceMeters { get; private set; }

    public double Lateral { get; private set; }

    /// <summary>Metres per second along the race right now (negative sliding back; 0 when parked in its slot). The engine sound follows it.</summary>
    public double Speed { get; set; }

    /// <summary>The track frame under the car (before any spin yaw).</summary>
    public TrackPose TrackFrame { get; private set; }

    public void AddWheel(Node3D pivot, float radius)
    {
        _wheels.Add((pivot, radius));
        AddChild(pivot);
    }

    /// <summary>Rolls a wheel pivot that is already in the car's tree (a Blender car's wheel node).</summary>
    public void RegisterWheel(Node3D pivot, float radius) => _wheels.Add((pivot, radius));

    /// <summary>The two detail levels of a Blender car (LOD1 may be missing: then LOD0 draws at every distance).</summary>
    public void SetLods(Node3D lod0, Node3D? lod1)
    {
        (_lod0Root, _lod1Root) = (lod0, lod1);
        Collect(lod0, _lod0);
        if (lod1 is not null)
        {
            Collect(lod1, _lod1);
        }

        static void Collect(Node n, List<GeometryInstance3D> into)
        {
            if (n is GeometryInstance3D g)
            {
                into.Add(g);
            }
            foreach (var c in n.GetChildren())
            {
                Collect(c, into);
            }
        }
    }

    /// <summary>Low preset: LOD1 at every distance; otherwise LOD0 near the camera and LOD1 beyond 60 m.</summary>
    public void SetLowDetail(bool low)
    {
        if (_lod0Root is null || _lod1Root is null)
        {
            return;
        }
        var (b0, b1) = CarLod.Bands(low);
        _lod0Root.Visible = b0.Visible;
        _lod1Root.Visible = b1.Visible;
        foreach (var (geoms, band) in new[] { (_lod0, b0), (_lod1, b1) })
        {
            foreach (var g in geoms)
            {
                g.VisibilityRangeBegin = band.Begin;
                g.VisibilityRangeBeginMargin = band.Begin > 0 ? band.Margin : 0;
                g.VisibilityRangeEnd = band.End;
                g.VisibilityRangeEndMargin = band.End > 0 ? band.Margin : 0;
                g.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Disabled;
            }
        }
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
