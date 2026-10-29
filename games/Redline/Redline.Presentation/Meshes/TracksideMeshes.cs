using System.Numerics;
using Redline.Presentation.Course;

namespace Redline.Presentation.Meshes;

/// <summary>
/// The track-following parts of the barriers and catch fence (spec §2.3), procedural like the walls; the posts, blocks and
/// tyre modules on them are instanced Blender props (<see cref="Trackside"/>).
/// <list type="bullet">
/// <item><see cref="Fence"/>: the catch fence's mesh panel along <see cref="BarrierLayout.Fence"/>, from
/// <see cref="FenceFooting"/> below the track up to <see cref="Knee"/>, then on to
/// <see cref="TrackMeshBuilder.FenceHeight"/> leaning <see cref="OverhangReach"/> × <see cref="BarrierLayout.Overhang"/>
/// towards the track. UV = (metres along, reading left to right from the track; metres up the panel from the track's
/// height, continuing up the overhang).</item>
/// <item><see cref="Cables"/>: the fence's horizontal cables at <see cref="CableHeights"/> and along its top edge, thin
/// ribbons just in front of the panel, one segment per <see cref="CableRows"/> rows.</item>
/// <item><see cref="Rails"/>: the armco's two W-beam rails, their crowns on the barrier line at <see cref="RailHeights"/>,
/// the profile <see cref="RailProfile"/> reaching back from it. UV = (metres along, metres across the profile).</item>
/// </list>
/// Every surface faces the track (the fence and rails are drawn double-sided by their materials).
/// </summary>
public static class TracksideMeshes
{
    public const double Knee = 3.4, OverhangReach = 1.0;
    /// <summary>The fence's mesh (and its posts) reach this far below the track: the ground behind it can fall away.</summary>
    public const double FenceFooting = 1.0;
    public static readonly double[] CableHeights = [0.3, 1.6, 2.9];
    public const int CableRows = 2;
    public const double CableWidth = 0.03, CableGap = 0.04;
    public static readonly double[] RailHeights = [0.50, 0.85];

    /// <summary>A W-beam's cross-section: (metres back from the barrier line, metres up from the rail's centre).</summary>
    public static readonly (double Back, double Up)[] RailProfile =
        [(0.075, -0.155), (0.01, -0.115), (0.0, -0.075), (0.055, -0.02), (0.055, 0.02), (0.0, 0.075), (0.01, 0.115), (0.075, 0.155)];

    private static readonly Vector4 FenceColour = MeshOps.Hex("#b8bdc4");
    private static readonly Vector4 Steel = MeshOps.Hex("#a9adb2");

    /// <summary>The overhang's top lateral: <paramref name="fence"/> less the lean at row weight <paramref name="w"/>.</summary>
    public static double TopLateral(double fence, double w) => fence - OverhangReach * w;

    public static MeshData Fence(BarrierLayout b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var q = b.Samples;
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var m = new MeshData();
        foreach (var side in new[] { +1, -1 })
        {
            var last = -1;
            int[] prev = [];
            for (var k = 0; k < n; k++)
            {
                if (!b.FenceKept(k, side)) continue;
                if (last != k) prev = Column(k);
                var next = Column(k + 1);
                for (var r = 0; r < 2; r++)
                {
                    // Seen from the track, a left fence runs with travel to the viewer's right, a right one to the left.
                    if (side > 0) m.AddQuad(prev[r + 1], next[r + 1], next[r], prev[r]);
                    else m.AddQuad(next[r + 1], prev[r + 1], prev[r], next[r]);
                }
                (last, prev) = (k + 1, next);
            }

            int[] Column(int k)
            {
                var i = k % n;
                var f = b.Fence(i, side);
                var w = b.Overhang(i, side);
                var u = (float)(side * k * spacing);
                var top = TopLateral(f, w);
                var lean = Math.Sqrt(Math.Pow(f - top, 2) + Math.Pow(TrackMeshBuilder.FenceHeight - Knee, 2));
                return
                [
                    m.AddVertex(TrackMeshBuilder.At(q[i], side * f, -FenceFooting), new Vector2(u, (float)-FenceFooting), FenceColour),
                    m.AddVertex(TrackMeshBuilder.At(q[i], side * f, Knee), new Vector2(u, (float)Knee), FenceColour),
                    m.AddVertex(TrackMeshBuilder.At(q[i], side * top, TrackMeshBuilder.FenceHeight), new Vector2(u, (float)(Knee + lean)), FenceColour),
                ];
            }
        }
        m.ComputeNormals();
        return m;
    }

    public static MeshData Cables(BarrierLayout b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var q = b.Samples;
        var n = q.Count;
        var m = new MeshData();
        foreach (var side in new[] { +1, -1 })
        {
            for (var k = 0; k < n; k += CableRows)
            {
                var ok = true;
                for (var d = 0; d < CableRows; d++)
                    ok &= b.FenceKept(k + d, side);
                if (!ok) continue;
                var j = k + CableRows;
                foreach (var h in CableHeights.Append(TrackMeshBuilder.FenceHeight))
                {
                    double Lat(int r) => h > Knee ? TopLateral(b.Fence(r, side), b.Overhang(r, side)) : b.Fence(r, side);
                    var la = Lat(k) - CableGap;
                    var lb = Lat(j) - CableGap;
                    var a0 = TrackMeshBuilder.At(q[k % n], side * la, h - CableWidth / 2);
                    var a1 = TrackMeshBuilder.At(q[k % n], side * la, h + CableWidth / 2);
                    var b0 = TrackMeshBuilder.At(q[j % n], side * lb, h - CableWidth / 2);
                    var b1 = TrackMeshBuilder.At(q[j % n], side * lb, h + CableWidth / 2);
                    FacingQuad(m, a1, b1, b0, a0, TowardsTrack(q[k % n], side), Steel);
                }
            }
        }
        m.ComputeNormals();
        return m;
    }

    public static MeshData Rails(BarrierLayout b)
    {
        ArgumentNullException.ThrowIfNull(b);
        var q = b.Samples;
        var n = q.Count;
        var spacing = q[1].S - q[0].S;
        var m = new MeshData();
        foreach (var run in b.Runs.Where(r => r.Kind == BarrierKind.Armco))
        {
            foreach (var centre in RailHeights)
            {
                var first = m.Positions.Count;
                var cols = RailProfile.Length;
                for (var k = run.From; k <= run.To; k++)
                {
                    var i = k % n;
                    var line = b.Line(i, run.Side);
                    double across = 0;
                    for (var p = 0; p < cols; p++)
                    {
                        if (p > 0)
                            across += Math.Sqrt(Math.Pow(RailProfile[p].Back - RailProfile[p - 1].Back, 2) + Math.Pow(RailProfile[p].Up - RailProfile[p - 1].Up, 2));
                        var pos = TrackMeshBuilder.At(q[i], run.Side * (line + RailProfile[p].Back), centre + RailProfile[p].Up);
                        m.AddVertex(pos, new Vector2((float)(run.Side * k * spacing), (float)across), Steel);
                    }
                }
                var rows = run.To - run.From + 1;
                for (var r = 0; r + 1 < rows; r++)
                    for (var p = 0; p + 1 < cols; p++)
                    {
                        var a = first + r * cols + p;
                        var c = first + (r + 1) * cols + p;
                        var want = TowardsTrack(q[(run.From + r) % n], run.Side);
                        FacingQuad(m, a, c, c + 1, a + 1, want);
                    }
            }
        }
        m.ComputeNormals();
        return m;
    }

    /// <summary>The level direction from a barrier on <paramref name="side"/> back towards the centre line.</summary>
    private static Vector3 TowardsTrack(TrackSample t, int side) => new(-side * t.Left.X, 0, -side * t.Left.Y);

    /// <summary>Adds quad a-b-c-d (a loop) so that its front faces <paramref name="want"/>.</summary>
    private static void FacingQuad(MeshData m, int a, int b, int c, int d, Vector3 want)
    {
        var p = m.Positions;
        var away = Vector3.Cross(p[b] - p[a], p[d] - p[a]);
        if (Vector3.Dot(away, want) > 0)
            m.AddQuad(a, d, c, b);
        else
            m.AddQuad(a, b, c, d);
    }

    private static void FacingQuad(MeshData m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 want, Vector4 colour)
    {
        var len = Vector3.Distance(a, b);
        var ia = m.AddVertex(a, new Vector2(0, 0), colour);
        var ib = m.AddVertex(b, new Vector2(len, 0), colour);
        var ic = m.AddVertex(c, new Vector2(len, 1), colour);
        var id = m.AddVertex(d, new Vector2(0, 1), colour);
        FacingQuad(m, ia, ib, ic, id, want);
    }
}
