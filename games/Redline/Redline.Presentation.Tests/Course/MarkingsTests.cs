using System.Numerics;
using ArcBox.Sdk;
using Redline.Logic;
using Redline.Presentation.Course;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests.Course;

public class MarkingsTests
{
    private static readonly CircuitData Cota = CircuitData.Load("cota");
    private static readonly IReadOnlyList<TrackSample> Samples = TrackGeometry.Resample(Cota);
    private static readonly Lazy<TrackMeshes> Track = new(() => TrackMeshBuilder.Build(Cota));

    /// <summary>Review focus 5: each box is exactly where <see cref="Formation"/> puts that grid position's car.</summary>
    [Fact]
    public void Grid_boxes_are_the_formation_grid_slots()
    {
        var boxes = Markings.Grid();
        Assert.Equal(22, boxes.Count);

        // A view with every car at distance 0 lines them up on the grid, in position order.
        var codes = Enumerable.Range(0, 22).Select(i => $"C{i:00}").ToArray();
        var slots = Formation.Current(GridView(codes), Cota.LapLength);
        for (var g = 0; g < 22; g++)
        {
            var slot = slots[codes[g]];
            Assert.Equal(g + 1, boxes[g].Position);
            Assert.Equal(slot.RaceMeters, boxes[g].S, 9);
            Assert.Equal(slot.Lateral, boxes[g].Lateral, 9);
            Assert.Equal(LapMath.Grid(g).Lateral, boxes[g].Lateral, 9);
        }
    }

    [Fact]
    public void Grid_box_paint_is_on_the_track_under_each_car()
    {
        var lines = Track.Value.Lines;
        foreach (var box in Markings.Grid())
        {
            // The front bar's four corners are painted vertices, draped on the surface at the car's own track point.
            var front = box.S + Markings.BoxFront;
            foreach (var (s, lat) in new[]
                     {
                         (front, box.Lateral - Markings.BoxHalfWidth), (front, box.Lateral + Markings.BoxHalfWidth),
                         (front - Markings.LineWidth, box.Lateral - Markings.BoxHalfWidth), (front - Markings.LineWidth, box.Lateral + Markings.BoxHalfWidth),
                     })
            {
                var expected = TrackMeshBuilder.TrackPoint(Samples, s, lat);
                Assert.Contains(lines.Positions, v => Vector2.Distance(new Vector2(v.X, v.Z), new Vector2(expected.X, expected.Z)) < 0.01f
                    && v.Y - expected.Y is > 0.015f and < 0.03f);
            }

            // The car standing in it (origin midway between the axles) fits between the brackets, and the box is on the asphalt.
            var half = TrackGeometry.Width(box.S) / 2;
            Assert.True(Math.Abs(box.Lateral) + Markings.BoxHalfWidth < half - 0.3);
            Assert.True(CarMeshBuilder.Width / 2 < Markings.BoxHalfWidth - Markings.LineWidth);
            Assert.True(Markings.BoxFront > CarMeshBuilder.Wheelbase / 2 + CarMeshBuilder.FrontTyreRadius);
        }
        // Boxes never overlap: the stagger keeps each column's boxes 16 m apart.
        var grid = Markings.Grid();
        for (var i = 2; i < grid.Count; i++)
            Assert.True(grid[i - 2].S - grid[i].S >= 2 * Markings.BoxBracket + Markings.NumberHeight);
    }

    [Fact]
    public void Numbers_and_marks_stay_on_the_asphalt()
    {
        foreach (var mark in Track.Value.Course.Marks)
            foreach (var p in new[] { mark.A, mark.B, mark.C, mark.D })
                Assert.True(Math.Abs(p.Y) <= TrackGeometry.Width(p.X) / 2 + 0.01, $"mark corner {p} is off the asphalt");

        // Every digit is drawable, and each grid position number is painted (strokes beside its box).
        foreach (var d in "0123456789")
            Assert.NotEmpty(Glyphs.Strokes(d));
        var marks = Track.Value.Course.Marks;
        foreach (var box in Markings.Grid())
        {
            var outward = box.Lateral >= 0 ? 1 : -1;
            var nearLat = box.Lateral + outward * (Markings.BoxHalfWidth + Markings.NumberGap);
            Assert.Contains(marks, m => m.A.X > box.S - 3 && m.A.X < box.S + Markings.BoxFront
                && (m.A.Y - nearLat) * outward > -0.2 && (m.A.Y - nearLat) * outward < 2.2);
        }
    }

    [Fact]
    public void Pit_and_straight_mode_lines_are_painted()
    {
        var marks = Track.Value.Course.Marks;
        var half = TrackGeometry.Width(0) / 2;
        // Pit entry and exit lines on the pit (left) side of the main straight.
        Assert.Contains(marks, m => m.A.X >= Markings.PitEntryFrom - 1 && m.A.X <= Markings.PitEntryTo && m.A.Y > half - Markings.PitLane - 0.5);
        Assert.Contains(marks, m => m.A.X >= Markings.PitExitFrom - 1 && m.A.X <= Markings.PitExitTo && m.A.Y > half - Markings.PitLane - 0.5);
        Assert.DoesNotContain(marks, m => m.A.X > Markings.PitEntryFrom && m.A.X < Markings.PitEntryTo && m.A.Y < 0 && m.Paint == Paint.White
            && Math.Abs(m.B.X - m.A.X) > 5);
        // One full-width activation line per straight-mode zone.
        foreach (var zone in Cota.StraightModeZones)
            Assert.Contains(marks, m => Math.Abs((m.A.X + m.C.X) / 2 - zone.Activation) < 0.5 && Math.Abs(m.A.Y - m.C.Y) > 12);
        // Repair seams at both ends of every patch.
        foreach (var (from, to) in Markings.Patches)
            foreach (var s in new[] { from, to })
                Assert.Contains(marks, m => m.Paint == Paint.Sealant && Math.Abs((m.A.X + m.C.X) / 2 - s) < 0.1);
    }

    private static RedlineHostView GridView(IReadOnlyList<string> codes) =>
        new(RacePhase.Grid, 0, 15, 28, "cota", null, null,
            [.. codes.Select((c, i) => new DriverRow(c, 1, "team", null, null, i + 1, 0, 0, 0, DriverStatus.Ready, null, null, null, false, true))],
            null, null, "attribution", Payouts.From(new RedlineRules()));
}
