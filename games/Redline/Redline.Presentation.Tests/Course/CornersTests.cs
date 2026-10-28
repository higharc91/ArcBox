using Redline.Presentation.Course;

namespace Redline.Presentation.Tests.Course;

public class CornersTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);
    private static double Lap => Samples[1].S * Samples.Count;

    [Fact]
    public void Cota_has_its_20_turns_in_order_with_every_field()
    {
        var corners = Cota.Corners;

        Assert.Equal(20, corners.Count);
        Assert.Equal(Enumerable.Range(1, 20), corners.Select(c => c.Turn));
        for (var i = 1; i < corners.Count; i++)
            Assert.True(corners[i].S > corners[i - 1].S);
        Assert.All(corners, c =>
        {
            Assert.InRange(c.S, 0, Lap);
            Assert.Contains(c.Direction, new[] { -1, 1 });
            // The apex is on the inside.
            Assert.Equal(-c.Direction, c.ApexSide);
            Assert.True(Enum.IsDefined(c.Kerb) && Enum.IsDefined(c.Runoff) && Enum.IsDefined(c.Barrier)
                && Enum.IsDefined(c.Braking) && Enum.IsDefined(c.Confidence));
        });

        // COTA: anticlockwise, 11 left-handers and 9 right-handers.
        Assert.Equal(11, corners.Count(c => c.Direction < 0));
        Assert.Equal(9, corners.Count(c => c.Direction > 0));
    }

    [Fact]
    public void Each_apex_sits_on_a_curvature_peak_of_its_direction()
    {
        var peaks = CourseCurvature.Peaks(Samples);
        foreach (var c in Cota.Corners)
        {
            var peak = peaks.MinBy(p => Math.Abs(p.S - c.S));
            Assert.True(Math.Abs(peak.S - c.S) <= 4, $"T{c.Turn} at {c.S} m is {Math.Abs(peak.S - c.S):F0} m from the nearest peak");
            Assert.Equal(c.Direction, Math.Sign(peak.Curvature));
        }
        // Every hairpin-tight peak (under 25 m) is one of the official turns. (Two tighter-than-40 m peaks are not: the left
        // kink at s 3924 on the way into Turn 15 and a polyline vertex at s 4520 in Turn 18's tail.)
        foreach (var p in peaks.Where(p => Math.Abs(p.Curvature) > 1 / 25.0))
            Assert.Contains(Cota.Corners, c => Math.Abs(c.S - p.S) <= 4);
    }

    [Fact]
    public void Cota_character_hairpins_and_hill()
    {
        var k = CourseCurvature.Signed(Samples);
        int Index(double s) => (int)Math.Round(s / Samples[1].S) % Samples.Count;
        Corner T(int turn) => Cota.Corners[turn - 1];

        // Turn 1, Turn 11, Turn 12 and Turn 15 are the slow, heavy-braking corners: tighter than 25 m.
        foreach (var turn in new[] { 1, 11, 12, 15 })
        {
            Assert.True(Math.Abs(k[Index(T(turn).S)]) > 1 / 25.0, $"T{turn} is not a hairpin");
            Assert.Equal(Braking.Heavy, T(turn).Braking);
            Assert.Equal(Confidence.High, T(turn).Confidence);
            Assert.Equal(RunoffKind.TarmacStriped, T(turn).Runoff);
        }
        // Turn 1 is a left at the top of the hill.
        Assert.Equal(-1, T(1).Direction);
        Assert.True(Samples[Index(T(1).S)].Height >= Samples.Max(q => q.Height) - 0.5);
        // The esses alternate and the T16-T18 multi-apex is one long right.
        Assert.Equal(new[] { -1, 1, -1, 1 }, Enumerable.Range(3, 4).Select(t => T(t).Direction));
        Assert.All(Enumerable.Range(16, 3), t => Assert.Equal(1, T(t).Direction));
        Assert.Equal(new[] { 4192.0, 4292, 4392 }, Enumerable.Range(16, 3).Select(t => T(t).S));
    }

    [Fact]
    public void Spans_contain_their_apex_and_stop_before_the_neighbours()
    {
        var spans = CourseCurvature.Spans(Samples, Cota.Corners, Lap);

        Assert.Equal(20, spans.Count);
        for (var i = 0; i < spans.Count; i++)
        {
            var s = spans[i];
            Assert.True(s.Entry < s.Apex && s.Apex < s.Exit, $"T{s.Corner.Turn}");
            Assert.InRange(s.Exit - s.Entry, 10, 220);
            var next = spans[(i + 1) % spans.Count];
            var nextApex = next.Apex + (i + 1 == spans.Count ? Lap : 0);
            Assert.True(s.Exit <= (s.Apex + nextApex) / 2 + 1e-6, $"T{s.Corner.Turn} runs past halfway to T{next.Corner.Turn}");
        }
    }

    [Fact]
    public void Straight_mode_zones_cover_the_two_long_straights()
    {
        var zones = Cota.StraightModeZones;

        Assert.Equal(2, zones.Count);
        foreach (var z in zones)
        {
            Assert.InRange(z.Length(Lap), 600, 1100);
            // No official turn inside a zone, and the zone ends before the next braking zone.
            Assert.DoesNotContain(Cota.Corners, c => z.Contains(c.S, Lap));
        }
        // The main straight's zone runs over the start line; the back straight's from Turn 11 to Turn 12.
        Assert.Contains(zones, z => z.Contains(0, Lap) && z.Contains(5300, Lap));
        Assert.Contains(zones, z => z.Activation > Cota.Corners[10].S && z.End < Cota.Corners[11].S);
    }
}
