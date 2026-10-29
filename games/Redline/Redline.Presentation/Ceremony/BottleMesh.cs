using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Ceremony;

/// <summary>A lathed podium champagne bottle: dark green glass with a cream label, and the team-colour foil over the neck.</summary>
public sealed record BottleMeshes(MeshData Glass, MeshData Foil);

/// <summary>
/// Podium spec §5: the champagne bottle on each step, lathed from a profile (a 3-litre Jeroboam's proportions). Bottle
/// frame: base centred on the origin, +Y up, the mouth at (0, <see cref="Height"/>, 0).
/// </summary>
public static class BottleMesh
{
    public const float Height = 0.48f;
    public const float BodyRadius = 0.064f;
    /// <summary>Where the foil starts, on the shoulder.</summary>
    public const float FoilFrom = 0.335f;
    private const int Segments = 28;

    private static readonly Vector4 GlassGreen = MeshOps.Hex("#163a24");
    private static readonly Vector4 Label = MeshOps.Hex("#efe6cf");
    private static readonly Vector4 LabelBand = MeshOps.Hex("#b8141c");

    /// <param name="foilHex">The foil's colour (the team accent), <c>#rrggbb</c>.</param>
    public static BottleMeshes Build(string foilHex)
    {
        ArgumentNullException.ThrowIfNull(foilHex);
        var foil = MeshOps.Hex(foilHex);
        var glass = new MeshData();
        CarMeshOps.Append(glass, Loft.Lathe(GlassProfile(), Segments), Upright);
        var cap = new MeshData();
        CarMeshOps.Append(cap, Loft.Lathe(FoilProfile(foil), Segments), Upright);
        return new BottleMeshes(glass, cap);
    }

    /// <summary>The lathe's X axis becomes +Y.</summary>
    private static Matrix4x4 Upright => Matrix4x4.CreateRotationZ(MathF.PI / 2);

    /// <summary>Radius of the glass at height <paramref name="y"/> (0 outside the bottle).</summary>
    public static float RadiusAt(float y)
    {
        var p = Shape;
        if (y < 0 || y > p[^1].Y) return 0;
        for (var i = 1; i < p.Length; i++)
        {
            if (y <= p[i].Y)
            {
                var t = (y - p[i - 1].Y) / MathF.Max(1e-6f, p[i].Y - p[i - 1].Y);
                return float.Lerp(p[i - 1].X, p[i].X, t);
            }
        }
        return p[^1].X;
    }

    // (radius, height) up the outside of the glass, base to lip.
    private static readonly Vector2[] Shape =
    [
        new(0.058f, 0), new(0.0635f, 0.006f), new(BodyRadius, 0.03f), new(BodyRadius, 0.265f), new(0.061f, 0.292f),
        new(0.052f, 0.315f), new(0.040f, 0.338f), new(0.029f, 0.36f), new(0.0215f, 0.385f), new(0.0200f, 0.41f),
        new(0.0200f, 0.458f), new(0.0232f, 0.462f), new(0.0232f, Height),
    ];

    private static List<ProfilePoint> GlassProfile()
    {
        // The label: a cream band round the body with a thin red stripe, each edge a hard colour change.
        (float Y, Vector4 Below, Vector4 Above)[] edges =
        [
            (0.12f, GlassGreen, Label), (0.19f, Label, LabelBand), (0.2f, LabelBand, Label), (0.215f, Label, GlassGreen),
        ];
        var points = new List<ProfilePoint> { new(0, 0, GlassGreen), new(Shape[0].X, Shape[0].Y, GlassGreen, Hard: true) };
        foreach (var v in Shape.Skip(1))
        {
            foreach (var (y, below, above) in edges.Where(e => e.Y > points[^1].A && e.Y < v.Y))
            {
                points.Add(new ProfilePoint(RadiusAt(y), y, below));
                points.Add(new ProfilePoint(RadiusAt(y), y, above));
            }
            points.Add(new ProfilePoint(v.X, v.Y, GlassGreen));
        }
        points.Add(new ProfilePoint(0, Height, GlassGreen));
        return points;
    }

    private static List<ProfilePoint> FoilProfile(Vector4 foil)
    {
        const float lift = 0.0018f;
        var points = new List<ProfilePoint> { new(RadiusAt(FoilFrom - 0.004f) + lift * 0.2f, FoilFrom - 0.004f, foil) };
        foreach (var v in Shape.Where(v => v.Y > FoilFrom))
        {
            points.Add(new ProfilePoint(v.X + lift, v.Y, foil));
        }
        points.Add(new ProfilePoint(Shape[^1].X + lift, Height + lift, foil, Hard: true));
        points.Add(new ProfilePoint(Shape[^1].X + lift, Height + lift, foil, Hard: true));
        points.Add(new ProfilePoint(0, Height + lift, foil));
        return points;
    }
}
