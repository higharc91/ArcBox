using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Ceremony;

/// <summary>Where a podium car is parked: metres along the race, metres left of centre, and yaw (radians, +turns left).</summary>
public readonly record struct PodiumPark(double RaceMeters, double Lateral, double Yaw);

/// <summary>
/// Podium spec §5: the podium set's geometry, in the mesh convention (Y up, metres). The set stands on the pit side of the
/// start straight, <see cref="SiteBeforeLine"/> metres before the line, in front of the pit building and facing the main
/// grandstand: a stage with three stepped blocks (2 left, 1 centre and highest, 3 right as the camera sees them), a
/// backdrop behind them, and the three cars parked nose-in in front.
/// <para>
/// Local frame: origin on the ground at the middle of the stage's front edge; +X to the right as the camera sees it
/// (along the direction of travel), +Y up, +Z towards the camera (towards the grandstand). The stage occupies
/// −<see cref="StageDepth"/> ≤ z ≤ 0.
/// </para>
/// </summary>
public sealed class PodiumLayout
{
    /// <summary>Metres before the start/finish line, midway along the pit building and the main grandstand.</summary>
    public const double SiteBeforeLine = 150;

    /// <summary>The stage's front edge, metres left of the centre line (the backdrop then stands just inside the pit wall).</summary>
    public const float FrontLateral = 5.0f;

    public const float StageWidth = 13f;
    public const float StageDepth = 5.6f;
    public const float StageHeight = 0.9f;

    public const float StepWidth = 2.5f;
    public const float StepDepth = 2.3f;
    /// <summary>Centre-to-centre spacing of the steps.</summary>
    public const float StepPitch = 2.8f;
    /// <summary>Centre of the steps along Z.</summary>
    public const float StepZ = -3.1f;

    public const float BackdropZ = -5.4f;
    public const float BackdropWidth = 16f;
    /// <summary>Height of the backdrop above the stage.</summary>
    public const float BackdropHeight = 7.2f;

    /// <summary>Parked cars' centres, in front of the stage.</summary>
    public const float CarZ = 4.7f;
    public const float CarPitch = 4.3f;
    /// <summary>The outer cars turn their noses this far towards the centre, like a parc fermé line-up.</summary>
    public const float CarAngleDegrees = 14f;

    /// <summary>A driver figure's height (helmet off, cap on).</summary>
    public const float FigureHeight = 1.78f;

    /// <param name="origin">The local origin in the world.</param>
    /// <param name="right">Local +X (level, along the direction of travel).</param>
    /// <param name="front">Local +Z (level, towards the grandstand side of the track).</param>
    private PodiumLayout(double siteS, Vector3 origin, Vector3 right, Vector3 front)
    {
        SiteS = siteS;
        Origin = origin;
        Right = right;
        Front = front;
    }

    /// <summary>Metres along the lap of the local origin.</summary>
    public double SiteS { get; }

    public Vector3 Origin { get; }
    public Vector3 Right { get; }
    public Vector3 Up => Vector3.UnitY;
    public Vector3 Front { get; }

    public static PodiumLayout For(IReadOnlyList<TrackSample> samples, double lapLength)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(lapLength, 0);
        var s = lapLength - SiteBeforeLine;
        var pose = TrackPose.At(samples, s, FrontLateral);
        var forward = Vector3.Normalize(pose.Forward with { Y = 0 });
        var left = new Vector3(forward.Z, 0, -forward.X);
        return new PodiumLayout(s, pose.Position, forward, -left);
    }

    /// <summary>A local point in the world.</summary>
    public Vector3 World(Vector3 local) => Origin + (Right * local.X) + (Up * local.Y) + (Front * local.Z);

    /// <summary>A local direction in the world.</summary>
    public Vector3 WorldDirection(Vector3 local) => (Right * local.X) + (Up * local.Y) + (Front * local.Z);

    /// <summary>Step 1 in the centre, 2 to its left and 3 to its right (as the camera sees them).</summary>
    public static float StepX(int step) => step switch
    {
        1 => 0,
        2 => -StepPitch,
        3 => StepPitch,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Podium steps are 1 to 3."),
    };

    /// <summary>The height of the step's top above the ground: 1 highest, then 2, then 3.</summary>
    public static float StepTop(int step) => StageHeight + step switch
    {
        1 => 1.0f,
        2 => 0.65f,
        3 => 0.35f,
        _ => throw new ArgumentOutOfRangeException(nameof(step), step, "Podium steps are 1 to 3."),
    };

    /// <summary>The middle of the step's top face (where the figure stands), in local coordinates.</summary>
    public static Vector3 StepTopCentre(int step) => new(StepX(step), StepTop(step), StepZ);

    /// <summary>Where the figure on <paramref name="step"/> holds the bottle (its base), in local coordinates.</summary>
    public static Vector3 Bottle(int step) => new(StepX(step) + 0.12f, StepTop(step) + 1.02f, StepZ + 0.36f);

    /// <summary>The name label's anchor above the figure's head, in local coordinates.</summary>
    public static Vector3 NameAnchor(int step) => new(StepX(step), StepTop(step) + FigureHeight + 0.3f, StepZ);

    /// <summary>The parked car for <paramref name="step"/>: nose towards the stage, the outer two angled in.</summary>
    public PodiumPark Car(int step)
    {
        var x = step switch { 1 => 0f, 2 => -CarPitch, 3 => CarPitch, _ => throw new ArgumentOutOfRangeException(nameof(step)) };
        // Local +Z is towards the right of travel, so lateral (metres left of centre) falls as z grows.
        var yaw = Math.PI / 2 - (step switch { 2 => 1, 3 => -1, _ => 0 }) * CarAngleDegrees * Math.PI / 180;
        return new PodiumPark(SiteS + x, FrontLateral - CarZ, yaw);
    }
}
