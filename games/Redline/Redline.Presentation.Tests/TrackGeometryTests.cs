using System.Numerics;

namespace Redline.Presentation.Tests;

public class TrackGeometryTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);

    private static double LoopLength()
    {
        double total = 0;
        for (var i = 0; i < Samples.Count; i++)
            total += Vector2.Distance(Samples[i].Position, Samples[(i + 1) % Samples.Count].Position);
        return total;
    }

    private static TrackSample Nearest(double s) =>
        Samples.MinBy(t => Math.Abs(t.S - s));

    [Fact]
    public void Resample_is_closed_and_evenly_spaced()
    {
        var length = LoopLength();
        Assert.InRange(length, 5505, 5525);
        Assert.InRange(Samples.Count, Math.Round(length / TrackGeometry.Step) - 1, Math.Round(length / TrackGeometry.Step) + 1);

        for (var i = 0; i < Samples.Count; i++)
        {
            var gap = Vector2.Distance(Samples[i].Position, Samples[(i + 1) % Samples.Count].Position);
            Assert.InRange(gap, 2.0 - 0.05, 2.0 + 0.05);
        }

        // The last sample wraps round to the first.
        Assert.InRange(Vector2.Distance(Samples[^1].Position, Samples[0].Position), 1.95, 2.05);
        Assert.Equal(0, Samples[0].S);
    }

    [Fact]
    public void Samples_carry_unit_tangents_left_normals_and_height()
    {
        foreach (var t in Samples)
        {
            Assert.Equal(1.0, t.Tangent.Length(), 4);
            Assert.Equal(t.Tangent.Y, t.Left.X, 6);
            Assert.Equal(-t.Tangent.X, t.Left.Y, 6);
            Assert.Equal(LapMath.Elevation(Cota, t.S), t.Height, 9);
        }
    }

    [Fact]
    public void Turn1_is_a_left_hairpin_corner()
    {
        var t = Nearest(327);

        Assert.True(TrackGeometry.IsCorner(t));
        Assert.Equal(-1, t.Turn);
    }

    [Fact]
    public void Main_straight_is_not_a_corner()
    {
        var t = Nearest(5300);

        Assert.False(TrackGeometry.IsCorner(t));
    }

    [Fact]
    public void Width_is_24_at_turn1_and_15_elsewhere()
    {
        Assert.Equal(24, TrackGeometry.Width(300), 9);
        Assert.Equal(15, TrackGeometry.Width(1000), 9);
        Assert.Equal(15, TrackGeometry.Width(120), 9);
        Assert.Equal(24, TrackGeometry.Width(200), 9);
        Assert.Equal(15, TrackGeometry.Width(470), 9);
        Assert.InRange(TrackGeometry.Width(160), 15, 24);
    }

    [Fact]
    public void Fold_guard_drops_inner_wall_at_turn1_only()
    {
        var t = Nearest(327);
        var offset = TrackGeometry.Width(t.S) / 2 + 19;

        // Turn 1 is a left turn, so the left wall is the inner one.
        Assert.False(TrackGeometry.WallAllowed(t, +1, offset));
        Assert.True(TrackGeometry.WallAllowed(t, -1, offset));

        var straight = Nearest(5300);
        var straightOffset = TrackGeometry.Width(straight.S) / 2 + 19;
        Assert.True(TrackGeometry.WallAllowed(straight, +1, straightOffset));
        Assert.True(TrackGeometry.WallAllowed(straight, -1, straightOffset));
    }
}
