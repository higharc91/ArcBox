using ArcBox.Host.Games.Redline.Cars;
using ArcBox.Host.Games.Redline.World;
using Godot;
using Redline.Presentation;
using Redline.Presentation.Meshes;
using NVector3 = System.Numerics.Vector3;

namespace ArcBox.Host.Games.Redline.Camera;

/// <summary>
/// Turns the director's <see cref="Shot"/> into the live camera (§4.9). Geometry comes from <see cref="ShotFraming"/>; the
/// rig adds timing: a smooth <see cref="BlendSeconds"/> blend between shots of the same kind, a hard cut between kinds,
/// the Driver Select hold of <see cref="HoldSeconds"/> on a newly claimed car (cut in and out, like a broadcast
/// close-up), and the Finish sequence (over the line, then the podium).
/// </summary>
public partial class CameraRig : Node3D
{
    public const double BlendSeconds = 0.6;
    public const double HoldSeconds = 2;
    public const double FinishLineSeconds = 3.5;

    private CircuitAssets _assets = null!;
    private Func<IReadOnlyDictionary<string, CarNode>> _cars = null!;
    private Func<IReadOnlyList<string>> _podium = () => [];

    private Shot? _shot;
    private double _shotStart;
    private int _side = 1;
    private string? _holdCode;
    private double _holdStart = double.NegativeInfinity;

    private CameraPose _current;
    private CameraPose _blendFrom;
    private double _blendStart = double.NegativeInfinity;
    private double _now;

    public Camera3D Camera { get; } = new() { Name = "Camera", Near = 0.25f, Far = 9000, Current = true };

    public override void _Ready()
    {
        AddChild(Camera);
    }

    /// <param name="cars">Live cars by driver code.</param>
    /// <param name="podium">Driver codes in finishing order, for the Finish shot.</param>
    public void Setup(CircuitAssets assets, Func<IReadOnlyDictionary<string, CarNode>> cars, Func<IReadOnlyList<string>> podium)
    {
        _assets = assets;
        _cars = cars;
        _podium = podium;
    }

    /// <summary>The shot now on air (for logs and the HUD).</summary>
    public Shot? Shot => _shot;

    public void SetShot(Shot shot)
    {
        var sameKind = _shot is not null && _shot.GetType() == shot.GetType();
        if (sameKind && Key(_shot!) == Key(shot))
        {
            return;
        }

        if (shot is LineupShot lineup)
        {
            if (lineup.Subject is { } code)
            {
                _holdCode = code;
                _holdStart = _now;
            }
            else if (_shot is LineupShot)
            {
                return;   // no new claim: keep whatever hold or orbit is running
            }
        }

        // Same kind blends (a new battle, a new subject); a new kind, or a Driver Select close-up on a fresh claim (whose
        // straight-line blend from the orbit would fly through the grid), is a hard cut.
        if (sameKind && shot is not LineupShot)
        {
            _blendFrom = _current;
            _blendStart = _now;
        }
        else
        {
            _blendStart = double.NegativeInfinity;
        }
        _shot = shot;
        _shotStart = _now;
        _side = ChooseSide(shot);
    }

    /// <summary>Moves the camera for time <paramref name="now"/> (scene seconds).</summary>
    public void Advance(double now)
    {
        _now = now;
        if (_shot is null || _assets is null)
        {
            return;
        }
        var target = Pose(_shot, now - _shotStart);
        var k = (now - _blendStart) / BlendSeconds;
        _current = k is >= 0 and < 1
            ? CameraPose.Lerp(_blendFrom, target, (float)RevealMotion.SmoothStep(k))
            : target;
        Apply(_current);
    }

    private void Apply(CameraPose pose)
    {
        var eye = MeshConvert.V(pose.Eye);
        var target = MeshConvert.V(pose.Target);
        if (eye.DistanceSquaredTo(target) < 1e-4f)
        {
            return;
        }
        Camera.Fov = pose.Fov;
        Camera.GlobalTransform = Transform3D.Identity.Translated(eye).LookingAt(target, Vector3.Up);
    }

    private CameraPose Pose(Shot shot, double t)
    {
        var samples = _assets.Samples;
        float Ground(float x, float z) => _assets.Ground.Height(x, z);
        var cars = _cars();

        switch (shot)
        {
            case GridShot:
                {
                    var p = ShotFraming.Grid(samples);
                    // A slow creep forward keeps the frame alive.
                    var drift = (float)Math.Min(t, 12) * 0.8f;
                    var dir = NVector3.Normalize(p.Target - p.Eye) * drift;
                    return p with { Eye = p.Eye + dir, Target = p.Target + dir };
                }
            case LineupShot:
                if (_holdCode is { } code && _now - _holdStart < HoldSeconds && cars.TryGetValue(code, out var held))
                {
                    return ShotFraming.LineupHold(held.TrackFrame, held.Lateral >= 0 ? 1 : -1, Ground);
                }
                return ShotFraming.LineupOrbitPose(samples, _now, Ground);
            case HeliShot heli:
                {
                    var subjects = Framed(heli.Subjects, cars);
                    if (subjects.Count == 0)
                    {
                        return _current;
                    }
                    var lead = cars[heli.Subjects.First(cars.ContainsKey)];
                    return ShotFraming.Heli(subjects, lead.TrackFrame.Forward, _side, t, Ground);
                }
            case TracksideShot side when cars.TryGetValue(side.Subject, out var car):
                return ShotFraming.Trackside(samples, car.RaceMeters, car.Lateral, car.TrackFrame.Position, _side, Ground);
            case FinishShot:
                {
                    if (t < FinishLineSeconds)
                    {
                        return ShotFraming.FinishLine(samples);
                    }
                    var podium = _podium().Where(cars.ContainsKey).Take(3).ToList();
                    if (podium.Count == 0)
                    {
                        return ShotFraming.FinishLine(samples);
                    }
                    var winner = cars[podium[0]];
                    var near = podium.Where(c => Math.Abs(cars[c].RaceMeters - winner.RaceMeters) < 80).ToList();
                    return ShotFraming.Heli([.. near.Select(c => cars[c].TrackFrame.Position)], winner.TrackFrame.Forward, -1, t - FinishLineSeconds, Ground);
                }
            default:
                return _current;
        }
    }

    /// <summary>The subjects plus any car within a few metres of one, so a pack reads as a pack.</summary>
    private static List<NVector3> Framed(IReadOnlyList<string> codes, IReadOnlyDictionary<string, CarNode> cars)
    {
        var subjects = codes.Where(cars.ContainsKey).Select(c => cars[c]).ToList();
        var framed = new List<NVector3>();
        foreach (var car in cars.Values)
        {
            if (subjects.Any(s => s == car || Math.Abs(s.RaceMeters - car.RaceMeters) < 12))
            {
                framed.Add(car.TrackFrame.Position);
            }
        }
        return framed;
    }

    /// <summary>
    /// Which side of the track the camera works from, fixed for the shot. Helicopter: the pit side on the start straight
    /// (the main grandstand is on the other), otherwise the outside of the bend. Trackside: the side of the track the car
    /// is on, so the camera never looks across other cars.
    /// </summary>
    private int ChooseSide(Shot shot)
    {
        var cars = _cars();
        switch (shot)
        {
            case HeliShot heli when heli.Subjects.FirstOrDefault(cars.ContainsKey) is { } code:
                {
                    var s = Wrap(cars[code].RaceMeters);
                    var lap = _assets.Circuit.LapLength;
                    if (s > lap - 700 || s < 120)
                    {
                        return 1;
                    }
                    var q = _assets.Samples[(int)(s / TrackGeometry.Step) % _assets.Samples.Count];
                    return q.Turn > 0 ? 1 : -1;
                }
            case TracksideShot side when cars.TryGetValue(side.Subject, out var car):
                return car.Lateral < -0.5 ? -1 : 1;
            default:
                return 1;
        }
    }

    private double Wrap(double s)
    {
        var lap = _assets.Circuit.LapLength;
        s %= lap;
        return s < 0 ? s + lap : s;
    }

    private static string Key(Shot shot) => shot switch
    {
        HeliShot h => "heli:" + string.Join(",", h.Subjects),
        TracksideShot t => "trackside:" + t.Subject,
        LineupShot l => "lineup:" + l.Subject,
        _ => shot.GetType().Name,
    };
}
