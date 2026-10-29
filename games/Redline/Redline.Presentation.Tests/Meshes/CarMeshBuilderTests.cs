using System.Numerics;
using Redline.Logic;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests.Meshes;

public class CarMeshBuilderTests
{
    private static readonly Grid Grid = Grid2026.Load();

    private static CarMeshes BuildFor(string code) => CarMeshBuilder.Build(Grid.ByCode(code));

    private static IEnumerable<MeshData> All(CarMeshes c) =>
        [c.Body, c.Carbon, c.Tyres, c.Rims, c.Halo, c.Lights, c.Helmet];

    private static int Triangles(CarMeshes c) => All(c).Sum(m => m.Indices.Count / 3);

    [Fact]
    public void Dimensions_match_2026_regs()
    {
        var car = BuildFor("NOR");

        Assert.Equal(4, car.WheelCentres.Count);
        var front = car.WheelCentres.Where(w => w.Z > 0).ToList();
        var rear = car.WheelCentres.Where(w => w.Z < 0).ToList();
        Assert.Equal(2, front.Count);
        Assert.Equal(2, rear.Count);
        Assert.InRange(front[0].Z - rear[0].Z, 3.35f, 3.45f);
        Assert.InRange(front[0].Z + rear[0].Z, -0.01f, 0.01f); // origin midway between the axles

        var all = All(car).SelectMany(m => m.Positions).ToList();
        Assert.InRange(all.Max(p => p.X) - all.Min(p => p.X), 1.80f, 1.91f);
        Assert.InRange(all.Min(p => p.Y), -0.001f, 0.001f); // tyres touch the ground at y = 0

        // Tyre widths, measured on the rubber of the left-hand (+X) tyres.
        var rearLeft = car.Tyres.Positions.Where(p => p.Z < 0 && p.X > 0).ToList();
        var frontLeft = car.Tyres.Positions.Where(p => p.Z > 0 && p.X > 0).ToList();
        Assert.InRange(rearLeft.Max(p => p.X) - rearLeft.Min(p => p.X), 0.355f, 0.395f);
        Assert.InRange(frontLeft.Max(p => p.X) - frontLeft.Min(p => p.X), 0.26f, 0.30f);
        Assert.InRange(rearLeft.Max(p => p.X), 0.94f, 0.951f);
    }

    [Fact]
    public void Triangle_budget_between_40k_and_70k()
    {
        Assert.InRange(Triangles(BuildFor("NOR")), 40_000, 70_000);
    }

    [Fact]
    public void Lofted_bodywork_is_dense()
    {
        // The chassis alone carries >= 48 sections x 32 around (>= 3,000 triangles); body is the densest mesh.
        Assert.True(BuildFor("NOR").Body.Indices.Count / 3 >= 48 * 32 * 2 * 2);
    }

    [Fact]
    public void Normals_unit_no_NaN()
    {
        foreach (var mesh in All(BuildFor("VER")))
        {
            Assert.Equal(mesh.Positions.Count, mesh.Normals.Count);
            Assert.Equal(mesh.Positions.Count, mesh.Uvs.Count);
            Assert.Equal(mesh.Positions.Count, mesh.Colors.Count);
            Assert.Equal(0, mesh.Indices.Count % 3);
            Assert.All(mesh.Indices, i => Assert.InRange(i, 0, mesh.Positions.Count - 1));
            Assert.All(mesh.Positions, p => Assert.True(float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z)));
            Assert.All(mesh.Normals, n => Assert.InRange(n.Length(), 0.999f, 1.001f));
            Assert.All(mesh.Uvs, uv => Assert.True(float.IsFinite(uv.X) && float.IsFinite(uv.Y)));
        }
    }

    [Fact]
    public void Winding_is_clockwise_seen_from_the_normal_side()
    {
        // Godot 4 front faces are clockwise as seen by the viewer. For a triangle seen from the side its normal
        // points to, clockwise means the right-handed cross product (b - a) x (c - a) points AWAY from the viewer,
        // i.e. opposite to the vertex normals.
        var car = BuildFor("LEC");
        foreach (var mesh in All(car))
        {
            var agree = 0;
            var total = mesh.Indices.Count / 3;
            for (var t = 0; t < total; t++)
            {
                var (a, b, c) = (mesh.Indices[3 * t], mesh.Indices[3 * t + 1], mesh.Indices[3 * t + 2]);
                var cross = Vector3.Cross(mesh.Positions[b] - mesh.Positions[a], mesh.Positions[c] - mesh.Positions[a]);
                var n = mesh.Normals[a] + mesh.Normals[b] + mesh.Normals[c];
                if (Vector3.Dot(cross, n) < 0)
                    agree++;
            }
            Assert.True(agree >= total * 0.99, $"{agree} of {total} triangles are clockwise-front");
        }

        // Concrete case: the highest triangle of the body faces up, so its cross product points down.
        var body = car.Body;
        var top = Enumerable.Range(0, body.Indices.Count / 3)
            .MaxBy(t => body.Positions[body.Indices[3 * t]].Y + body.Positions[body.Indices[3 * t + 1]].Y + body.Positions[body.Indices[3 * t + 2]].Y);
        var (p0, p1, p2) = (body.Positions[body.Indices[3 * top]], body.Positions[body.Indices[3 * top + 1]], body.Positions[body.Indices[3 * top + 2]]);
        Assert.True(Vector3.Cross(p1 - p0, p2 - p0).Y < 0);
        Assert.True(body.Normals[body.Indices[3 * top]].Y > 0.5f);
    }

    [Fact]
    public void Normals_point_outward()
    {
        var car = BuildFor("NOR");
        // Tyre tread: the vertex furthest from its wheel's axle has a normal pointing away from the axle.
        foreach (var w in car.WheelCentres)
        {
            var i = Enumerable.Range(0, car.Tyres.Positions.Count)
                .Where(k => Vector3.Distance(car.Tyres.Positions[k], w) < 0.5f)
                .MaxBy(k => car.Tyres.Positions[k].Y);
            var radial = car.Tyres.Positions[i] - w;
            radial.X = 0;
            Assert.True(Vector3.Dot(Vector3.Normalize(radial), car.Tyres.Normals[i]) > 0.9f);
        }
        // Helmet: normals point away from the helmet centre.
        var centre = car.Helmet.Positions.Aggregate(Vector3.Zero, (s, p) => s + p) / car.Helmet.Positions.Count;
        var outward = car.Helmet.Positions.Zip(car.Helmet.Normals).Count(pn => Vector3.Dot(pn.First - centre, pn.Second) > 0);
        Assert.True(outward >= car.Helmet.Positions.Count * 0.95);
    }

    [Fact]
    public void Livery_zone_colours_present()
    {
        var seat = Grid.ByCode("NOR");
        var car = CarMeshBuilder.Build(seat);
        var top = LiveryColours.Parse(seat.Team.Livery.Top);
        var side = LiveryColours.Parse(seat.Team.Livery.Side);
        var stripe = LiveryColours.Parse(seat.Team.Livery.Stripe);
        var halo = LiveryColours.Parse(seat.Team.Livery.Halo);

        Assert.Equal(new Vector4(1f, 128 / 255f, 0f, 1f), top);
        Assert.Contains(top, car.Body.Colors);
        Assert.Contains(side, car.Body.Colors);
        Assert.Contains(stripe, car.Body.Colors);
        Assert.All(car.Halo.Colors, c => Assert.Equal(halo, c));
        // The orange is the dominant body colour by vertex count.
        Assert.True(car.Body.Colors.Count(c => c == top) > car.Body.Colors.Count / 4);
    }

    [Fact]
    public void Red_bull_nose_is_yellow_and_ferrari_has_no_orange()
    {
        var rb = BuildFor("VER");
        Assert.Contains(LiveryColours.Parse("#ffc906"), rb.Body.Colors);
        var ferrari = BuildFor("LEC");
        Assert.DoesNotContain(LiveryColours.Parse("#ff8000"), ferrari.Body.Colors);
        Assert.Contains(LiveryColours.Parse("#dc0000"), ferrari.Body.Colors);
    }

    [Fact]
    public void Tcam_is_black_on_first_car_and_yellow_on_second()
    {
        Assert.Contains(CarMeshBuilder.TcamYellow, BuildFor("PIA").Body.Colors);
        Assert.DoesNotContain(CarMeshBuilder.TcamYellow, BuildFor("NOR").Body.Colors);
        Assert.Contains(CarMeshBuilder.TcamBlack, BuildFor("NOR").Body.Colors);
        Assert.Equal(TcamColour.Black, LiveryColours.ParseTcam("black"));
        Assert.Equal(TcamColour.Yellow, LiveryColours.ParseTcam("Yellow"));
        Assert.Throws<ArgumentException>(() => LiveryColours.ParseTcam("green"));
    }

    [Fact]
    public void Two_number_decals_on_rear_endplates_and_one_on_nose()
    {
        var car = BuildFor("HAM");
        Assert.Equal(3, car.NumberDecals.Count);

        var endplates = car.NumberDecals.Where(d => MathF.Abs(d.Normal.X) > 0.99f).ToList();
        Assert.Equal(2, endplates.Count);
        Assert.All(endplates, d =>
        {
            Assert.InRange(d.Pos.Z, -2.6f, -2.0f);
            Assert.InRange(MathF.Abs(d.Pos.X), 0.5f, 0.53f);
            Assert.Equal(MathF.Sign(d.Pos.X), MathF.Sign(d.Normal.X)); // faces outward
            Assert.InRange(d.Size, 0.15f, 0.3f);
        });
        Assert.NotEqual(MathF.Sign(endplates[0].Pos.X), MathF.Sign(endplates[1].Pos.X));

        var nose = Assert.Single(car.NumberDecals, d => d.Normal.Y > 0.9f);
        Assert.InRange(nose.Pos.Z, 1.6f, 2.6f);
        Assert.InRange(nose.Pos.X, -0.001f, 0.001f);
        // Sits just above the nose surface: interpolate the body's top centre line (highest X = 0 vertex per section).
        var line = car.Body.Positions.Where(p => p.X == 0 && p.Z > 1.5f && p.Z < 2.6f)
            .GroupBy(p => p.Z).Select(g => (Z: g.Key, Y: g.Max(p => p.Y))).OrderBy(p => p.Z).ToList();
        var hi = line.First(p => p.Z >= nose.Pos.Z);
        var lo = line.Last(p => p.Z <= nose.Pos.Z);
        var surface = hi.Z == lo.Z ? hi.Y : float.Lerp(lo.Y, hi.Y, (nose.Pos.Z - lo.Z) / (hi.Z - lo.Z));
        Assert.InRange(nose.Pos.Y - surface, 0.001f, 0.01f);
        Assert.All(car.NumberDecals, d => Assert.InRange(d.Normal.Length(), 0.999f, 1.001f));
    }

    [Fact]
    public void Builds_for_all_22_drivers()
    {
        var seats = Grid.Seats;
        Assert.Equal(22, seats.Count);
        foreach (var seat in seats)
        {
            var car = CarMeshBuilder.Build(seat);
            Assert.InRange(Triangles(car), 40_000, 70_000);
            Assert.Contains(LiveryColours.Parse(seat.Team.Livery.Top), car.Body.Colors);
        }
    }

    [Fact]
    public void Rejects_bad_input()
    {
        var livery = Grid.ByCode("NOR").Team.Livery;
        Assert.Throws<ArgumentNullException>(() => CarMeshBuilder.Build(null!, 1, TcamColour.Black));
        Assert.Throws<ArgumentOutOfRangeException>(() => CarMeshBuilder.Build(livery, 100, TcamColour.Black));
        Assert.Throws<FormatException>(() => LiveryColours.Parse("orange"));
    }
}
