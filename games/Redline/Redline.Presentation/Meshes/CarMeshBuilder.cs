using System.Numerics;
using Redline.Logic;

namespace Redline.Presentation.Meshes;

/// <summary>
/// The meshes of one car, split by material. <see cref="Body"/> carries the livery zones as vertex colours (top, side,
/// stripe, nose, wings, endplates, T-cam); <see cref="Halo"/> is painted in the livery halo colour; <see cref="Tyres"/>
/// carry the compound stripe; <see cref="Helmet"/> the white shell, team band and dark visor; <see cref="Rims"/> also holds
/// the mirror glass and exhaust (bright metal); <see cref="Lights"/> the rain lights (emissive red). Every mesh has vertex
/// colours, sRGB-encoded. <see cref="WheelCentres"/> are front-left, front-right, rear-left, rear-right (left = +X).
/// <see cref="NumberDecals"/> are where the car number is drawn (nose, then the -X and +X rear endplates): the decal
/// centre, its outward facing direction, and the glyph height in metres. The text's "up" is +Y on the endplates and
/// -Z (towards the cockpit) on the nose, so the nose number reads upright from in front of the car.
/// </summary>
public sealed record CarMeshes(
    MeshData Body,
    MeshData Carbon,
    MeshData Tyres,
    MeshData Rims,
    MeshData Halo,
    MeshData Lights,
    MeshData Helmet,
    IReadOnlyList<Vector3> WheelCentres,
    IReadOnlyList<(Vector3 Pos, Vector3 Normal, float Size)> NumberDecals);

/// <summary>
/// Builds a 2026-regulation F1 car procedurally (port of the mockups' f1car2026.js, densified for the TV).
/// <para>
/// Frame: right-handed, Y up, metres, <b>+Z forward</b>, +X to the car's left; the origin is on the ground midway between
/// the axles, so placement puts the origin on the track point. Wheelbase 3.4 m, overall width 1.9 m, tyres 280 mm front /
/// 375 mm rear wide, no beam wing, active-aero flaps front and rear.
/// </para>
/// <para>
/// Winding: Godot 4 convention — a triangle is front-facing when its vertices appear <b>clockwise</b> to the viewer. So for
/// every triangle (a, b, c) the right-handed cross product (b - a) x (c - a) points <b>against</b> its vertex normals (into
/// the surface): a triangle on top of the car, seen from above, has a cross product pointing down (-Y).
/// </para>
/// <para>
/// No team, sponsor or series logos; the helmet is plain with a team-colour band. <c>Finish</c> (matte/gloss) is applied
/// by the Godot material, not here.
/// </para>
/// </summary>
public static class CarMeshBuilder
{
    public const float Wheelbase = 3.4f;
    public const float Width = 1.9f;
    public const float FrontTyreWidth = 0.28f;
    public const float RearTyreWidth = 0.375f;
    public const float FrontTyreRadius = 0.3525f;
    public const float RearTyreRadius = 0.355f;
    public const float NumberSize = 0.24f;

    public static readonly Vector4 TcamBlack = LiveryColours.Parse("#101012");
    public static readonly Vector4 TcamYellow = LiveryColours.Parse("#f6e600");

    private const float FrontAxle = Wheelbase / 2;
    private const float RearAxle = -Wheelbase / 2;
    private const float HalfWidth = Width / 2;
    private const float RimRadius = 0.232f;
    private const int ChassisPer = 6, ChassisSegs = 64;

    private static readonly Vector4 Underside = LiveryColours.Parse("#141417");
    private static readonly Vector4 CarbonColour = LiveryColours.Parse("#151518");
    private static readonly Vector4 Rubber = LiveryColours.Parse("#141416");
    private static readonly Vector4 CompoundStripe = LiveryColours.Parse("#f2c200");
    private static readonly Vector4 RimColour = LiveryColours.Parse("#202024");
    private static readonly Vector4 MirrorGlass = LiveryColours.Parse("#9fb3c8");
    private static readonly Vector4 Exhaust = LiveryColours.Parse("#2a2a2d");
    private static readonly Vector4 Led = LiveryColours.Parse("#ff1010");
    private static readonly Vector4 HelmetWhite = LiveryColours.Parse("#f2f2f2");
    private static readonly Vector4 Visor = LiveryColours.Parse("#0b0b10");

    // Chassis: nose tip -> survival cell -> cockpit -> airbox -> engine cover -> gearbox. Bottom drops the tub to the floor.
    private static readonly Station[] Chassis =
    [
        new(2.98f, 0.205f, 0.05f, 0.045f, 2.2f, 0, 0.55f),
        new(2.75f, 0.235f, 0.11f, 0.08f, 2.4f, 0, 0.55f),
        new(2.3f, 0.30f, 0.17f, 0.12f, 2.6f, 0, 0.6f),
        new(1.75f, 0.38f, 0.23f, 0.17f, 3.0f, 0, 0.9f),
        new(1.2f, 0.45f, 0.29f, 0.215f, 3.4f, 0, 1.6f),
        new(0.7f, 0.5f, 0.34f, 0.255f, 3.8f, 0, 1.6f),
        new(0.3f, 0.48f, 0.37f, 0.235f, 4.0f, 0, 1.65f),
        new(-0.15f, 0.48f, 0.38f, 0.25f, 4.0f, 0, 1.55f),
        new(-0.42f, 0.62f, 0.27f, 0.4f, 3.0f, 0, 1.33f),
        new(-0.75f, 0.6f, 0.22f, 0.35f, 2.8f, 0, 1.43f),
        new(-1.2f, 0.5f, 0.17f, 0.25f, 2.6f, 0, 1.5f),
        new(-1.65f, 0.4f, 0.13f, 0.17f, 2.4f, 0, 1.45f),
        new(-2.0f, 0.345f, 0.1f, 0.11f, 2.4f, 0, 1.3f),
        new(-2.2f, 0.34f, 0.06f, 0.06f, 2.2f, 0, 1.0f),
    ];

    /// <summary>Builds the car for a grid seat (team livery, driver number and T-cam from grid2026.json).</summary>
    public static CarMeshes Build(Seat seat)
    {
        ArgumentNullException.ThrowIfNull(seat);
        return Build(seat.Team.Livery, seat.Driver.Number, LiveryColours.ParseTcam(seat.Driver.Tcam));
    }

    /// <summary>Builds one car. <paramref name="number"/> (0..99) is drawn by the host at <see cref="CarMeshes.NumberDecals"/>.</summary>
    public static CarMeshes Build(Livery livery, int number, TcamColour tcam)
    {
        ArgumentNullException.ThrowIfNull(livery);
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(number, 99);

        var c = new Colours(livery);
        var car = new CarMeshes(new MeshData(), new MeshData(), new MeshData(), new MeshData(), new MeshData(), new MeshData(), new MeshData(),
            WheelCentres(), NumberDecals());

        AddChassis(car, c);
        AddSidepods(car, c);
        AddCockpit(car, c, tcam);
        AddFloor(car);
        AddFrontWing(car, c);
        AddRearWing(car, c);
        AddWheels(car);
        AddSuspension(car);
        return car;
    }

    private sealed class Colours(Livery l)
    {
        public readonly Vector4 Top = LiveryColours.Parse(l.Top);
        public readonly Vector4 Side = LiveryColours.Parse(l.Side);
        public readonly Vector4 Stripe = LiveryColours.Parse(l.Stripe);
        public readonly Vector4 Nose = LiveryColours.Parse(l.Nose);
        public readonly Vector4 Wing = LiveryColours.Parse(l.Wing);
        public readonly Vector4 Endplate = LiveryColours.Parse(l.Endplate);
        public readonly Vector4 Halo = LiveryColours.Parse(l.Halo);
    }

    private static List<Vector3> WheelCentres() =>
    [
        new(HalfWidth - FrontTyreWidth / 2, FrontTyreRadius, FrontAxle),
        new(-(HalfWidth - FrontTyreWidth / 2), FrontTyreRadius, FrontAxle),
        new(HalfWidth - RearTyreWidth / 2, RearTyreRadius, RearAxle),
        new(-(HalfWidth - RearTyreWidth / 2), RearTyreRadius, RearAxle),
    ];

    private static List<(Vector3, Vector3, float)> NumberDecals()
    {
        const float noseZ = 2.05f;
        var (top, slope) = Loft.TopAt(Chassis, ChassisPer, noseZ);
        var noseNormal = Vector3.Normalize(new Vector3(0, 1, -slope));
        return
        [
            (new Vector3(0, top, noseZ) + noseNormal * 0.004f, noseNormal, NumberSize),
            (new Vector3(-0.513f, 0.80f, -2.32f), -Vector3.UnitX, NumberSize),
            (new Vector3(0.513f, 0.80f, -2.32f), Vector3.UnitX, NumberSize),
        ];
    }

    private static void AddChassis(CarMeshes car, Colours c)
    {
        var body = Loft.Superellipse(Chassis, ChassisPer, ChassisSegs, (z, rel) =>
            rel < -0.55f ? Underside
            : z > 2.5f ? c.Nose
            : rel > 0.02f && rel < 0.16f ? c.Stripe
            : rel <= 0.02f ? c.Side
            : c.Top,
            relBreaks: [-0.55f, 0.02f, 0.16f], zBreaks: [2.5f]);
        CarMeshOps.Append(car.Body, body);

        // Engine-cover fin and the gearbox/crash-structure exhaust and rain light.
        CarMeshOps.Append(car.Body, Loft.Prism([new(-0.55f, 0.98f), new(-1.55f, 0.62f), new(-1.55f, 0.70f), new(-0.60f, 1.03f)],
            0.012f, Vector3.UnitZ, Vector3.UnitY, c.Top));
        CarMeshOps.Append(car.Rims, Loft.Cylinder(0.048f, 0.12f, 24, Exhaust), Matrix4x4.CreateRotationY(MathF.PI / 2) * CarMeshOps.At(0, 0.42f, -2.22f));
        CarMeshOps.Append(car.Carbon, Loft.Disc(0.036f, 0.036f, 20, Underside), Matrix4x4.CreateRotationY(MathF.PI) * CarMeshOps.At(0, 0.42f, -2.2805f));
        CarMeshOps.Append(car.Lights, Loft.Box(new Vector3(0.12f, 0.06f, 0.03f), Led), CarMeshOps.At(0, 0.30f, -2.225f));
    }

    private static void AddSidepods(CarMeshes car, Colours c)
    {
        foreach (var s in new[] { -1f, 1f })
        {
            Station[] pod =
            [
                new(0.92f, 0.43f, 0.15f, 0.15f, 3.2f, s * 0.56f, 0.87f),
                new(0.7f, 0.44f, 0.19f, 0.17f, 3.4f, s * 0.6f, 1.06f),
                new(0.2f, 0.42f, 0.19f, 0.16f, 3.4f, s * 0.59f, 1.6f),
                new(-0.4f, 0.36f, 0.17f, 0.13f, 3.0f, s * 0.47f, 2.0f),
                new(-0.95f, 0.29f, 0.12f, 0.09f, 2.8f, s * 0.3f, 2.2f),
                new(-1.35f, 0.24f, 0.07f, 0.05f, 2.4f, s * 0.2f, 3.0f),
            ];
            var part = Loft.Superellipse(pod, 10, 48, (_, rel) =>
                rel < -0.55f ? Underside : rel > 0.3f ? c.Top : rel > 0.12f ? c.Stripe : c.Side,
                relBreaks: [-0.55f, 0.12f, 0.3f]);
            CarMeshOps.Append(car.Body, part);
            CarMeshOps.Append(car.Carbon, Loft.Disc(0.125f, 0.11f, 32, CarbonColour), CarMeshOps.At(s * 0.56f, 0.445f, 0.9215f));
        }
    }

    private static void AddCockpit(CarMeshes car, Colours c, TcamColour tcam)
    {
        // Cockpit opening, airbox inlet, T-cam.
        CarMeshOps.Append(car.Carbon, Loft.Ellipsoid(new Vector3(0.21f, 0.03f, 0.34f), 32, 12, CarbonColour), CarMeshOps.At(0, 0.72f, 0.32f));
        CarMeshOps.Append(car.Carbon, Loft.Disc(0.09f, 0.075f, 32, CarbonColour), CarMeshOps.At(0, 0.91f, -0.27f));
        CarMeshOps.Append(car.Body, Loft.Box(new Vector3(0.28f, 0.035f, 0.07f), tcam == TcamColour.Yellow ? TcamYellow : TcamBlack),
            CarMeshOps.At(0, 1.04f, -0.42f));

        // Helmet: plain white shell, team band, dark visor.
        var band = Vector4.Distance(c.Top, HelmetWhite) < 0.15f ? c.Side : c.Top;
        var helmetAt = CarMeshOps.At(0, 0.83f, 0.15f);
        CarMeshOps.Append(car.Helmet, Loft.Ellipsoid(new Vector3(0.142f), 48, 32, HelmetWhite), helmetAt);
        CarMeshOps.Append(car.Helmet, Loft.Ellipsoid(new Vector3(0.1435f), 48, 3, band, 0.49f, 0.67f), helmetAt);
        CarMeshOps.Append(car.Helmet, Loft.Ellipsoid(new Vector3(0.1442f), 24, 6, Visor, -0.1f, 0.32f, -1.2f, 1.2f), helmetAt);

        // Halo: hoop plus central pillar.
        Vector3[] hoop =
        [
            new(0.34f, 0.74f, -0.2f), new(0.33f, 0.9f, 0.05f), new(0.17f, 0.94f, 0.42f), new(0f, 0.945f, 0.54f),
            new(-0.17f, 0.94f, 0.42f), new(-0.33f, 0.9f, 0.05f), new(-0.34f, 0.74f, -0.2f),
        ];
        CarMeshOps.Append(car.Halo, Loft.Tube(hoop, 96, 16, 0.034f, 0.034f, c.Halo));
        CarMeshOps.Append(car.Halo, Loft.Tube([new(0f, 0.945f, 0.54f), new(0f, 0.86f, 0.66f), new(0f, 0.76f, 0.72f)], 16, 12, 0.03f, 0.03f, c.Halo));

        // Mirrors: body-coloured housings on carbon stalks, glass facing back.
        foreach (var s in new[] { -1f, 1f })
        {
            CarMeshOps.Append(car.Body, Loft.Ellipsoid(new Vector3(0.08f, 0.035f, 0.03f), 24, 12, c.Top), CarMeshOps.At(s * 0.52f, 0.72f, 0.6f));
            CarMeshOps.Append(car.Rims, Loft.Disc(0.07f, 0.028f, 24, MirrorGlass), Matrix4x4.CreateRotationY(MathF.PI) * CarMeshOps.At(s * 0.52f, 0.72f, 0.5695f));
            CarMeshOps.Append(car.Carbon, Loft.Tube([new(s * 0.30f, 0.66f, 0.62f), new(s * 0.47f, 0.72f, 0.6f)], 1, 8, 0.012f, 0.012f, CarbonColour));
        }
    }

    private static void AddFloor(CarMeshes car)
    {
        // Floor (narrows ahead of the rear tyres), 50 mm ride height; polygon in (Z, X).
        Vector2[] floor =
        [
            new(1.3f, 0.3f), new(0.9f, 0.7f), new(-1.15f, 0.72f), new(-1.42f, 0.52f), new(-2.05f, 0.52f),
            new(-2.05f, -0.52f), new(-1.42f, -0.52f), new(-1.15f, -0.72f), new(0.9f, -0.7f), new(1.3f, -0.3f),
        ];
        CarMeshOps.Append(car.Carbon, Loft.Prism(floor, 0.025f, Vector3.UnitZ, Vector3.UnitX, CarbonColour), CarMeshOps.At(0, 0.0625f, 0));

        foreach (var s in new[] { -1f, 1f })
        {
            // Floor-edge fence and two fences under the floor's leading edge.
            CarMeshOps.Append(car.Carbon, Loft.Prism([new(0.9f, 0.05f), new(-1.1f, 0.05f), new(-1.1f, 0.12f), new(0.6f, 0.16f)],
                0.012f, Vector3.UnitZ, Vector3.UnitY, CarbonColour), CarMeshOps.At(s * 0.71f, 0, 0));
            foreach (var x in new[] { 0.36f, 0.5f })
                CarMeshOps.Append(car.Carbon, Loft.Prism([new(1.15f, 0.075f), new(0.8f, 0.075f), new(0.8f, 0.2f), new(1.0f, 0.2f)],
                    0.01f, Vector3.UnitZ, Vector3.UnitY, CarbonColour), CarMeshOps.At(s * x, 0, 0));
        }

        // Diffuser: a ramp rising to the rear between strakes.
        CarMeshOps.Append(car.Carbon, Loft.Box(new Vector3(1.02f, 0.012f, 0.63f), CarbonColour),
            Matrix4x4.CreateRotationX(0.3f) * CarMeshOps.At(0, 0.17f, -1.78f));
        foreach (var x in new[] { -0.51f, -0.34f, -0.17f, 0.17f, 0.34f, 0.51f })
            CarMeshOps.Append(car.Carbon, Loft.Prism([new(-1.48f, 0.05f), new(-2.08f, 0.05f), new(-2.08f, 0.27f)],
                0.01f, Vector3.UnitZ, Vector3.UnitY, CarbonColour), CarMeshOps.At(x, 0, 0));
    }

    private static Matrix4x4 Pitch(float aoa, float y, float z) => Matrix4x4.CreateRotationX(aoa) * CarMeshOps.At(0, y, z);

    private static void AddFrontWing(CarMeshes car, Colours c)
    {
        // Main plane + two active flaps (2026: simpler, no cascades), tips curving up slightly.
        static Vector3 Sweep(float u) => new(0, 0.02f * u * u, 0);
        CarMeshOps.Append(car.Body, Loft.Airfoil(0.44f, 1.72f, 0.09f, 0.05f, 24, 16, c.Wing, Sweep), Pitch(0f, 0.1f, 3.05f));
        CarMeshOps.Append(car.Body, Loft.Airfoil(0.27f, 1.62f, 0.1f, 0.06f, 20, 16, c.Top, Sweep), Pitch(0.26f, 0.155f, 2.72f));
        CarMeshOps.Append(car.Body, Loft.Airfoil(0.2f, 1.56f, 0.1f, 0.06f, 20, 16, c.Wing, Sweep), Pitch(0.5f, 0.215f, 2.56f));
        foreach (var s in new[] { -1f, 1f })
        {
            CarMeshOps.Append(car.Body, Loft.Prism([new(3.1f, 0.04f), new(2.4f, 0.04f), new(2.33f, 0.3f), new(2.55f, 0.33f), new(3.02f, 0.17f)],
                0.016f, Vector3.UnitZ, Vector3.UnitY, c.Endplate), CarMeshOps.At(s * 0.865f, 0, 0));
            CarMeshOps.Append(car.Carbon, Loft.Prism([new(2.85f, 0.12f), new(2.6f, 0.12f), new(2.65f, 0.24f), new(2.82f, 0.2f)],
                0.01f, Vector3.UnitZ, Vector3.UnitY, CarbonColour), CarMeshOps.At(s * 0.12f, 0, 0));
        }
    }

    private static void AddRearWing(CarMeshes car, Colours c)
    {
        // Main plane + active flap; no beam wing in 2026.
        CarMeshOps.Append(car.Body, Loft.Airfoil(0.34f, 0.98f, 0.11f, 0.07f, 24, 12, c.Wing), Pitch(0.1f, 0.86f, -2.02f));
        CarMeshOps.Append(car.Body, Loft.Airfoil(0.24f, 0.98f, 0.1f, 0.06f, 20, 12, c.Top), Pitch(0.45f, 0.935f, -2.30f));
        foreach (var s in new[] { -1f, 1f })
        {
            CarMeshOps.Append(car.Body, Loft.Prism([new(-1.97f, 0.62f), new(-2.6f, 0.54f), new(-2.62f, 1.07f), new(-2.12f, 1.1f), new(-1.95f, 0.93f)],
                0.02f, Vector3.UnitZ, Vector3.UnitY, c.Endplate), CarMeshOps.At(s * 0.5f, 0, 0));
            CarMeshOps.Append(car.Lights, Loft.Box(new Vector3(0.025f, 0.28f, 0.015f), Led), CarMeshOps.At(s * 0.5f, 0.75f, -2.645f));
        }
        // Swan-neck pylon.
        CarMeshOps.Append(car.Carbon, Loft.Prism([new(-2.0f, 0.33f), new(-2.12f, 0.33f), new(-2.2f, 0.84f), new(-2.1f, 0.84f)],
            0.03f, Vector3.UnitZ, Vector3.UnitY, CarbonColour));
    }

    private static void AddWheels(CarMeshes car)
    {
        foreach (var centre in car.WheelCentres)
        {
            var front = centre.Z > 0;
            var w = front ? FrontTyreWidth : RearTyreWidth;
            var radius = front ? FrontTyreRadius : RearTyreRadius;
            // Built with the outer face towards +X; right-hand wheels are turned 180 degrees about Y.
            var place = (centre.X > 0 ? Matrix4x4.Identity : Matrix4x4.CreateRotationY(MathF.PI)) * CarMeshOps.At(centre);

            CarMeshOps.Append(car.Tyres, Loft.Lathe(TyreProfile(radius, w), 72), place);

            // Rim: lip and dished face, ten spokes, hub and nut; a closed inner face; brake duct inboard.
            CarMeshOps.Append(car.Rims, Loft.Lathe([
                new(RimRadius, w / 2 - 0.002f, RimColour), new(RimRadius - 0.012f, w / 2 - 0.006f, RimColour),
                new(RimRadius - 0.03f, w / 2 - 0.03f, RimColour), new(RimRadius - 0.035f, w / 2 - 0.06f, RimColour),
            ], 72), place);
            CarMeshOps.Append(car.Rims, Loft.Disc(RimRadius - 0.03f, RimRadius - 0.03f, 48, RimColour),
                Matrix4x4.CreateRotationY(MathF.PI / 2) * CarMeshOps.At(w / 2 - 0.06f, 0, 0) * place);
            CarMeshOps.Append(car.Rims, Loft.Disc(RimRadius, RimRadius, 48, RimColour),
                Matrix4x4.CreateRotationY(-MathF.PI / 2) * CarMeshOps.At(-w / 2 + 0.012f, 0, 0) * place);
            for (var k = 0; k < 10; k++)
            {
                var a = k * MathF.PI / 5;
                var radial = new Vector3(0, MathF.Cos(a), MathF.Sin(a));
                var tangential = new Vector3(0, -MathF.Sin(a), MathF.Cos(a));
                var spoke = Loft.Prism([new(0.045f, -0.02f), new(RimRadius - 0.032f, -0.011f), new(RimRadius - 0.032f, 0.011f), new(0.045f, 0.02f)],
                    0.014f, radial, tangential, RimColour);
                CarMeshOps.Append(car.Rims, spoke, CarMeshOps.At(w / 2 - 0.045f, 0, 0) * place);
            }
            CarMeshOps.Append(car.Rims, Loft.Cylinder(0.055f, 0.05f, 32, RimColour), CarMeshOps.At(w / 2 - 0.035f, 0, 0) * place);
            CarMeshOps.Append(car.Rims, Loft.Cylinder(0.03f, 0.03f, 12, MirrorGlass), CarMeshOps.At(w / 2 - 0.02f, 0, 0) * place);
            CarMeshOps.Append(car.Carbon, Loft.Cylinder(0.16f, 0.1f, 32, CarbonColour), CarMeshOps.At(-(w / 2 + 0.03f), 0, 0) * place);
        }
    }

    /// <summary>Shouldered tyre profile: up the -X sidewall, round the shoulder, across the tread and back down, with compound stripes.</summary>
    private static List<ProfilePoint> TyreProfile(float radius, float w)
    {
        const float shoulder = 0.05f;
        float stripeLo = radius - 0.078f, stripeHi = radius - 0.064f;
        var side = new List<ProfilePoint>();
        float[] radii = [RimRadius, RimRadius + 0.02f, RimRadius + 0.045f];
        foreach (var r in radii)
            side.Add(new(r, Bulge(r), Rubber));
        side.Add(new(stripeLo, Bulge(stripeLo), Rubber));
        side.Add(new(stripeLo, Bulge(stripeLo), CompoundStripe));
        side.Add(new(stripeHi, Bulge(stripeHi), CompoundStripe));
        side.Add(new(stripeHi, Bulge(stripeHi), Rubber));
        side.Add(new(radius - shoulder, -w / 2, Rubber));
        for (var k = 1; k <= 6; k++)
        {
            var a = k / 6f * MathF.PI / 2;
            side.Add(new(radius - shoulder + shoulder * MathF.Sin(a), -w / 2 + shoulder - shoulder * MathF.Cos(a), Rubber));
        }
        var profile = new List<ProfilePoint>(side);
        for (var k = 1; k < 4; k++)
            profile.Add(new(radius, float.Lerp(-w / 2 + shoulder, w / 2 - shoulder, k / 4f), Rubber));
        for (var i = side.Count - 1; i >= 0; i--)
            profile.Add(side[i] with { A = -side[i].A });
        return profile;

        // Sidewall tucks in towards the rim and reaches the full width at the shoulder.
        float Bulge(float r)
        {
            var t = (r - RimRadius) / (radius - shoulder - RimRadius);
            return -w / 2 + 0.012f * (1 - t) * (1 - t);
        }
    }

    private static void AddSuspension(CarMeshes car)
    {
        void Arm(Vector3 a, Vector3 b, float r = 0.016f) =>
            CarMeshOps.Append(car.Carbon, Loft.Tube([a, b], 1, 10, r * 1.8f, r * 0.6f, CarbonColour, Vector3.UnitZ));

        foreach (var s in new[] { -1f, 1f })
        {
            float fx = s * (HalfWidth - FrontTyreWidth / 2 - 0.12f), rx = s * (HalfWidth - RearTyreWidth / 2 - 0.13f);
            const float f = FrontAxle, r = RearAxle;
            // Front: lower and upper wishbones, push rod, track rod.
            Arm(new(s * 0.2f, 0.3f, f - 0.25f), new(fx, 0.27f, f));
            Arm(new(s * 0.18f, 0.3f, f + 0.2f), new(fx, 0.27f, f));
            Arm(new(s * 0.22f, 0.5f, f - 0.2f), new(fx, 0.47f, f));
            Arm(new(s * 0.16f, 0.48f, f + 0.22f), new(fx, 0.47f, f));
            Arm(new(s * 0.2f, 0.6f, f - 0.05f), new(fx * 0.9f, 0.3f, f), 0.012f);
            Arm(new(s * 0.15f, 0.37f, f + 0.06f), new(fx, 0.37f, f + 0.09f), 0.01f);
            // Rear: lower and upper wishbones, pull rod, toe link.
            Arm(new(s * 0.3f, 0.3f, r + 0.25f), new(rx, 0.28f, r));
            Arm(new(s * 0.25f, 0.3f, r - 0.25f), new(rx, 0.28f, r));
            Arm(new(s * 0.3f, 0.5f, r + 0.2f), new(rx, 0.5f, r));
            Arm(new(s * 0.2f, 0.48f, r - 0.2f), new(rx, 0.5f, r));
            Arm(new(s * 0.22f, 0.3f, r + 0.1f), new(rx * 0.9f, 0.5f, r), 0.012f);
            Arm(new(s * 0.22f, 0.4f, r - 0.1f), new(rx, 0.4f, r - 0.1f), 0.01f);
        }
    }
}
