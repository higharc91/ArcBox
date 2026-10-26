using System.Numerics;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests;

public class ShotFramingTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);

    private static float Horizontal(Vector3 a, Vector3 b) => new Vector2(a.X - b.X, a.Z - b.Z).Length();

    /// <summary>Angle in degrees between the view direction and the direction to <paramref name="p"/>.</summary>
    private static float OffAxis(CameraPose cam, Vector3 p)
    {
        var view = Vector3.Normalize(cam.Target - cam.Eye);
        var to = Vector3.Normalize(p - cam.Eye);
        return MathF.Acos(Math.Clamp(Vector3.Dot(view, to), -1, 1)) * 180 / MathF.PI;
    }

    [Fact]
    public void Heli_on_one_car_is_60m_out_and_25m_up()
    {
        var car = TrackPose.At(Samples, 1500, 0);

        var cam = ShotFraming.Heli([car.Position], car.Forward, 1, 0);

        Assert.Equal(60, Horizontal(cam.Eye, car.Position), 1);
        Assert.Equal(25, cam.Eye.Y - car.Position.Y, 1);
        Assert.True(OffAxis(cam, car.Position) < 2);
    }

    [Fact]
    public void Heli_backs_off_to_frame_two_distant_cars()
    {
        var a = TrackPose.At(Samples, 1500, 0).Position;
        var b = TrackPose.At(Samples, 1650, 0).Position;

        var cam = ShotFraming.Heli([a, b], Vector3.UnitX, -1, 3);

        Assert.True(Horizontal(cam.Eye, (a + b) / 2) > 60);
        // Inside the horizontal half-angle of a 16:9 frame.
        var halfWidth = MathF.Atan(MathF.Tan(cam.Fov / 2 * MathF.PI / 180) * 16 / 9) * 180 / MathF.PI;
        Assert.True(OffAxis(cam, a) < halfWidth);
        Assert.True(OffAxis(cam, b) < halfWidth);
    }

    [Fact]
    public void Heli_sits_on_the_requested_side_and_orbits()
    {
        var car = TrackPose.At(Samples, 1500, 0);

        var left = ShotFraming.Heli([car.Position], car.Forward, 1, 0);
        var right = ShotFraming.Heli([car.Position], car.Forward, -1, 0);
        var later = ShotFraming.Heli([car.Position], car.Forward, 1, 6);

        Assert.True(Vector3.Dot(left.Eye - car.Position, car.Left) > 0);
        Assert.True(Vector3.Dot(right.Eye - car.Position, car.Left) < 0);
        Assert.True(Vector3.Dot(left.Eye - car.Position, car.Forward) > 0, "front quarter");
        Assert.NotEqual(left.Eye, later.Eye);
    }

    [Fact]
    public void Heli_stays_above_the_ground()
    {
        var car = TrackPose.At(Samples, 1500, 0);

        var cam = ShotFraming.Heli([car.Position], car.Forward, 1, 0, (_, _) => 500);

        Assert.True(cam.Eye.Y >= 500 + ShotFraming.Clearance - 0.01f);
    }

    [Fact]
    public void Trackside_is_9m_ahead_5m_out_and_low()
    {
        var car = TrackPose.At(Samples, 2000, 3.4);

        var cam = ShotFraming.Trackside(Samples, 2000, 3.4, car.Position, 1);

        var offset = cam.Eye - car.Position;
        Assert.Equal(9, Vector3.Dot(offset, car.Forward), 0);
        Assert.Equal(5, Vector3.Dot(offset, car.Left), 0);
        Assert.Equal(ShotFraming.TracksideHeight, cam.Eye.Y - TrackMeshBuilder.TrackPoint(Samples, 2009, 8.4).Y, 2);
        Assert.True(OffAxis(cam, car.Position) < cam.Fov / 2);
    }

    [Fact]
    public void Grid_looks_down_the_straight_over_the_whole_grid()
    {
        var cam = ShotFraming.Grid(Samples);
        var pole = TrackPose.At(Samples, -Formation.PoleSetback, -3.6).Position;
        var last = TrackPose.At(Samples, -Formation.PoleSetback - 168, 3.6).Position;

        Assert.True(OffAxis(cam, pole) < cam.Fov / 2);
        Assert.True(OffAxis(cam, last) < cam.Fov / 2);
        Assert.True(cam.Eye.Y > pole.Y + 10);
    }

    [Fact]
    public void Lineup_hold_frames_the_car_closely()
    {
        var car = TrackPose.At(Samples, -40, 3.6);
        var alongside = TrackPose.At(Samples, -32, -3.6);   // the car ahead in the other column

        var cam = ShotFraming.LineupHold(car, 1);

        Assert.InRange(Vector3.Distance(cam.Eye, car.Position), 6, 12);
        Assert.True(OffAxis(cam, car.Position) < cam.Fov / 2);
        Assert.True(Vector3.Dot(cam.Eye - car.Position, car.Left) > 4, "outside the column");
        Assert.True(Vector3.Distance(cam.Eye, alongside.Position) > 6, "clear of the neighbouring car");
    }

    [Fact]
    public void Lineup_orbit_stays_on_the_pit_side_of_the_grid()
    {
        var centre = TrackPose.At(Samples, -Formation.PoleSetback - 84, 0);
        for (var t = 0.0; t < 90; t += 1.5)
        {
            var cam = ShotFraming.LineupOrbitPose(Samples, t);
            Assert.True(Vector3.Dot(cam.Eye - centre.Position, centre.Left) > 20, $"t = {t}");
            Assert.InRange(Vector3.Distance(cam.Target, centre.Position), 0, 2);
        }
    }

    [Fact]
    public void Lerp_blends_every_part()
    {
        var a = new CameraPose(Vector3.Zero, Vector3.UnitX, 30);
        var b = new CameraPose(Vector3.One, Vector3.UnitY, 50);

        var mid = CameraPose.Lerp(a, b, 0.5f);

        Assert.Equal(new Vector3(0.5f), mid.Eye);
        Assert.Equal(new Vector3(0.5f, 0.5f, 0), mid.Target);
        Assert.Equal(40, mid.Fov);
    }
}
