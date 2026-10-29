using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Redline.Presentation.Course;
using Redline.Presentation.Meshes;
using Redline.Presentation.Models;

namespace Redline.Presentation.Tests.Course;

/// <summary>
/// Barriers and trackside furniture (spec §2.3) on the real COTA site, with the landmarks' structures as the TV computes
/// them: nothing intrudes into the run-off or the track, the pit lane or a landmark; every prop stands on the ground or
/// the verge (review focus 1 and 2); barriers never overlap; brake boards at their distances; marshal posts every
/// ~300 m; the whole trackside within its triangle budget; the committed models match their contracts.
/// </summary>
public class TracksideTests
{
    private static TracksideFixture F => TracksideFixture.Cota.Value;

    private static TracksidePlan P => F.Plan;

    [Fact]
    public void Plan_has_every_feature()
    {
        Assert.InRange(P.TecPro.Count, 400, 1500);
        Assert.InRange(P.Tyres.Count, 400, 1500);
        Assert.InRange(P.ArmcoPosts.Count, 800, 3000);
        Assert.InRange(P.FencePosts.Count, 2000, 6000);
        Assert.Contains(P.FencePosts, p => p.Variant == 1);
        Assert.InRange(P.MarshalPosts.Count, 17, 19);
        Assert.Equal(Enumerable.Range(1, 20), P.CornerBoards.Select(b => b.Turn));
        Assert.InRange(P.BrakeBoards.Count, 15, 27);
        Assert.Equal(Trackside.CameraTurns, P.CameraPlatforms.Select(c => (int)c.Shade));
        Assert.Equal(3, P.TecPro.Select(t => t.Shade).Distinct().Count());
        Assert.NotEmpty(P.Rails.Indices);
        Assert.NotEmpty(P.Cables.Indices);

        // Barrier kinds follow the corner data: TecPro outside Turn 1, a tyre wall outside the esses, armco on the back straight.
        var b = P.Barriers;
        Assert.Contains(b.Runs, r => r.Kind == BarrierKind.TecPro && r.Turn == 1 && r.Side == -1);
        Assert.Contains(b.Runs, r => r.Kind == BarrierKind.Tyres && r.Turn == 3);
        foreach (var side in new[] { +1, -1 })
            Assert.Contains(b.Runs, r => r.Kind == BarrierKind.Armco && r.Side == side && r.Segments * TrackGeometry.Step > 800);
        Assert.Contains(b.Runs, r => r.Kind == BarrierKind.Wall);
    }

    [Fact]
    public void Barrier_kinds_follow_each_corner_outside()
    {
        var b = P.Barriers;
        var n = F.Samples.Count;
        foreach (var span in F.Track.Course.Corners.Where(s => s.Corner.Barrier is BarrierKind.TecPro or BarrierKind.Tyres))
        {
            var apex = (int)Math.Round(span.Apex / TrackGeometry.Step) % n;
            var side = span.Corner.OutsideSide;
            // At the apex the outside barrier is the corner's (where the line stands at all).
            var rows = Enumerable.Range(apex - 3, 7).Where(i => b.Kept(i, side)).ToList();
            Assert.NotEmpty(rows);
            Assert.All(rows, i => Assert.Equal(span.Corner.Barrier, b.Kind(i, side)));
        }
        // No run is a stub.
        Assert.All(b.Runs, r => Assert.True(r.Segments >= 5, $"{r} is a stub"));
    }

    [Fact]
    public void Nothing_intrudes_into_the_run_off_or_the_track()
    {
        foreach (var (what, box) in P.BarrierFootprints())
            foreach (var c in box.Corners.Append(box.Centre))
                Assert.True(Trackside.Clearance(F.Site, c) >= 0.5, $"{what} reaches {Trackside.Clearance(F.Site, c):0.00} m into the run-off at {c}");
        foreach (var (what, box) in P.FurnitureFootprints())
            foreach (var c in box.Corners.Append(box.Centre))
                Assert.True(Trackside.Clearance(F.Site, c) >= Trackside.Margin - 1e-3, $"{what} reaches {Trackside.Clearance(F.Site, c):0.00} m into the run-off at {c}");
        foreach (var p in P.FencePosts)
            Assert.True(Trackside.Clearance(F.Site, p.Footprint.Centre) >= 1, $"fence post at {p.Position} is in the run-off");
        foreach (var (name, mesh) in new[] { ("rails", P.Rails), ("cables", P.Cables), ("fence", F.Track.Fence), ("walls", F.Track.Walls) })
            foreach (var v in mesh.Positions)
                Assert.True(Trackside.Clearance(F.Site, new Vector2(v.X, v.Z)) >= 0.5, $"{name} vertex at {v} is in the run-off");

        // The barrier line keeps behind every corner's run-off with a metre to spare, on both sides.
        var b = P.Barriers;
        for (var i = 0; i < F.Samples.Count; i++)
            foreach (var side in new[] { +1, -1 })
                if (b.Kept(i, side))
                    Assert.True(b.Line(i, side) >= F.Track.Course.Runoff.Outer(i, side) + 0.99, $"barrier line inside the run-off at row {i}");
    }

    [Fact]
    public void Nothing_stands_on_the_pit_lane_or_a_landmark()
    {
        foreach (var (what, box) in P.BarrierFootprints().Concat(P.FurnitureFootprints()))
        {
            Assert.False(box.Corners.Append(box.Centre).Any(c => Trackside.InPitZone(F.Site, c)), $"{what} is on the pit lane");
            Assert.False(F.Site.Structures.Any(s => s.Overlaps(box)), $"{what} at {box.Centre} stands on a landmark");
        }
        foreach (var p in P.FencePosts)
            Assert.DoesNotContain(F.Site.Structures, s => s.Contains(p.Footprint.Centre));
    }

    [Fact]
    public void Barriers_never_overlap_each_other_and_furniture_keeps_off_them()
    {
        // Neighbouring blocks share their face's ends; their bodies may not cross more than a few centimetres.
        var boxes = P.BarrierFootprints().Select(x => (x.What, Box: x.Box with { HalfLength = x.Box.HalfLength - 0.06f, HalfWidth = x.Box.HalfWidth - 0.04f })).ToList();
        var buckets = boxes.Select((x, i) => (x, i)).ToLookup(t => Key(t.x.Box.Centre));
        foreach (var (what, box) in boxes)
        {
            var (kx, kz) = Key(box.Centre);
            for (var dx = -1; dx <= 1; dx++)
                for (var dz = -1; dz <= 1; dz++)
                    foreach (var (other, _) in buckets[(kx + dx, kz + dz)])
                        if (other.What != what)
                            Assert.False(box.Overlaps(other.Box), $"{what} overlaps {other.What}");
        }
        var furniture = P.FurnitureFootprints().ToList();
        foreach (var (what, box) in furniture)
        {
            Assert.DoesNotContain(P.BarrierFootprints(), x => x.Box.Overlaps(box));
            Assert.DoesNotContain(furniture, x => x.What != what && x.Box.Overlaps(box));
        }

        static (int, int) Key(Vector2 p) => ((int)MathF.Floor(p.X / 8), (int)MathF.Floor(p.Y / 8));
    }

    /// <summary>Review focus 1: nothing floats or sinks on the slopes.</summary>
    [Fact]
    public void Every_prop_stands_on_the_verge_or_the_ground()
    {
        var q = F.Samples;
        foreach (var (what, p) in P.TecPro.Select(p => ("tecpro", p)).Concat(P.Tyres.Select(p => ("tyres", p)))
                     .Concat(P.ArmcoPosts.Select(p => ("armco post", p))))
        {
            // On the verge as rendered where its face is ...
            var face = new Vector2(p.Position.X, p.Position.Z);
            var verge = F.Site.Verge.Height(face);
            Assert.NotNull(verge);
            Assert.Equal(Trackside.Surface(F.Site, face), p.Position.Y, 3);
            Assert.True(MathF.Abs(p.Position.Y - verge!.Value) < 0.2f, $"{what} at {p.Position} is {p.Position.Y - verge:0.000} m off the verge");
            // ... its footing reaches the ground under every corner, and no corner is buried by more than 0.3 m.
            foreach (var c in p.Footprint.Corners)
            {
                var g = Trackside.Surface(F.Site, c);
                Assert.True(g >= p.Position.Y - Trackside.BarrierFooting && g <= p.Position.Y + 0.3f,
                    $"{what} at {p.Position}: ground {g - p.Position.Y:0.000} m at a corner {c}");
            }
        }
        // Fence posts stand with the fence panel at the track's height (their footing reaches the terrain behind the verge).
        foreach (var p in P.FencePosts)
        {
            var at = new Vector2(p.Position.X, p.Position.Z);
            var g = Trackside.Surface(F.Site, at);
            Assert.True(g >= p.Position.Y - Trackside.FenceFooting && g <= p.Position.Y + 0.3f, $"fence post at {p.Position}: ground {g - p.Position.Y:0.000} m");
        }
        foreach (var (what, box) in P.FurnitureFootprints().ToList())
        {
            var y = Furniture(what).Position.Y;
            foreach (var c in box.Corners.Append(box.Centre))
            {
                var g = Trackside.Surface(F.Site, c);
                Assert.True(y <= g + 0.01f, $"{what}: a corner floats {y - g:0.000} m over the ground");
                Assert.True(y >= g - 1.2f, $"{what}: a corner is buried {g - y:0.000} m");
            }
        }
    }

    private static Prop Furniture(string what) => what switch
    {
        _ when what.StartsWith("marshal post ", StringComparison.Ordinal) => P.MarshalPosts.Single(m => $"marshal post {m.Text}" == what).Prop,
        _ when what.StartsWith("corner board ", StringComparison.Ordinal) => P.CornerBoards.Single(m => $"corner board {m.Text}" == what).Prop,
        _ when what.StartsWith("brake board ", StringComparison.Ordinal) => P.BrakeBoards.Single(m => $"brake board T{m.Turn} {m.Text}" == what).Prop,
        _ when what.StartsWith("camera platform ", StringComparison.Ordinal) => P.CameraPlatforms[int.Parse(what[16..], System.Globalization.CultureInfo.InvariantCulture)],
        _ => P.BigScreen,
    };

    [Fact]
    public void Brake_boards_stand_300_200_100_metres_before_the_entry_on_the_outside()
    {
        var spans = F.Track.Course.Corners;
        var lap = F.Site.LapLength;
        foreach (var board in P.BrakeBoards)
        {
            var span = spans.Single(s => s.Corner.Turn == board.Turn);
            Assert.Contains(span.Corner.Braking, new[] { Braking.Medium, Braking.Heavy });
            var metres = int.Parse(board.Text, System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains(metres, new[] { 100, 200, 300 });
            Assert.Equal(Wrap(span.Entry - metres, lap), board.S, 6);
            Assert.Equal(span.Corner.OutsideSide, board.Side);
            Assert.Equal(TracksideAtlas.Brake(metres), board.Cell);
            // It really stands there: its centre projects onto the lap within a couple of metres of S, on its side.
            var at = new Vector2(board.Prop.Position.X, board.Prop.Position.Z);
            var (i, _) = Nearest(at);
            Assert.True(Math.Abs(Wrap(F.Samples[i].S - board.S + lap / 2, lap) - lap / 2) < 2.5, $"brake board {board} stands at s={F.Samples[i].S:0}");
            Assert.Equal(board.Side, Math.Sign(Vector2.Dot(at - F.Samples[i].Position, F.Samples[i].Left)));
        }
        // Every medium or heavy corner has the boards its straight has room for (300 m before Turn 15 is in Turn 14).
        for (var k = 0; k < spans.Count; k++)
        {
            var span = spans[k];
            if (span.Corner.Braking is not (Braking.Medium or Braking.Heavy)) continue;
            var room = Wrap(span.Entry - spans[(k + spans.Count - 1) % spans.Count].Exit, lap);
            var expected = new[] { 300, 200, 100 }.Where(m => m <= room - 20).ToList();
            Assert.Equal(expected, P.BrakeBoards.Where(b => b.Turn == span.Corner.Turn).Select(b => int.Parse(b.Text, System.Globalization.CultureInfo.InvariantCulture)));
        }
        Assert.Equal(3, P.BrakeBoards.Count(b => b.Turn == 1));
        Assert.Equal(3, P.BrakeBoards.Count(b => b.Turn == 11));
        Assert.Equal(3, P.BrakeBoards.Count(b => b.Turn == 12));
        foreach (var span in spans.Where(s => s.Corner.Braking is Braking.None or Braking.Light))
            Assert.DoesNotContain(P.BrakeBoards, b => b.Turn == span.Corner.Turn);
    }

    [Fact]
    public void Marshal_posts_every_300_metres_on_alternating_sides()
    {
        var posts = P.MarshalPosts;
        var lap = F.Site.LapLength;
        Assert.Equal(Enumerable.Range(1, posts.Count).Select(n => n.ToString(System.Globalization.CultureInfo.InvariantCulture)), posts.Select(p => p.Text));
        Assert.Equal(posts.Select(p => p.S).Order(), posts.Select(p => p.S));
        for (var k = 0; k < posts.Count; k++)
        {
            var gap = Wrap(posts[(k + 1) % posts.Count].S - posts[k].S, lap);
            var spacing = Trackside.Spacing(lap);
            Assert.InRange(spacing, 280, 320);
            Assert.InRange(gap, spacing - 2 * Trackside.MarshalShift - 1, spacing + 2 * Trackside.MarshalShift + 1);
            Assert.Equal(TracksideAtlas.Post(k + 1), posts[k].Cell);
        }
        var alternating = Enumerable.Range(0, posts.Count - 1).Count(k => posts[k].Side != posts[k + 1].Side);
        Assert.True(alternating >= (posts.Count - 1) * 2 / 3, $"only {alternating} of {posts.Count - 1} neighbours alternate");
        // Behind the barrier: further from the centre line than its barrier's back.
        foreach (var post in posts)
        {
            var c = post.Prop.Footprint.Centre;
            var (i, d) = Nearest(c);
            var b = P.Barriers;
            Assert.True(d > b.Line(i, post.Side) + BarrierLayout.Depth(b.Kind(i, post.Side)), $"marshal post {post.Text} is not behind the barrier");
        }
    }

    [Fact]
    public void Corner_boards_stand_before_each_corner_on_its_outside()
    {
        var lap = F.Site.LapLength;
        foreach (var board in P.CornerBoards)
        {
            var span = F.Track.Course.Corners.Single(s => s.Corner.Turn == board.Turn);
            Assert.Equal(span.Corner.OutsideSide, board.Side);
            Assert.Equal($"T{board.Turn}", board.Text);
            Assert.Equal(TracksideAtlas.Corner(board.Turn), board.Cell);
            var before = Wrap(span.Entry - board.S + lap / 2, lap) - lap / 2;
            Assert.InRange(before, -15, 55);
        }
        Assert.Equal("T1", TracksideAtlas.Cells[TracksideAtlas.Corner(1)]);
        Assert.Equal("300", TracksideAtlas.Cells[TracksideAtlas.Brake(300)]);
        Assert.Equal("7", TracksideAtlas.Cells[TracksideAtlas.Post(7)]);
        Assert.Equal(TracksideAtlas.Columns * TracksideAtlas.Rows, TracksideAtlas.Cells.Count);
        Assert.True(P.MarshalPosts.Count <= TracksideAtlas.Cells.Count - TracksideAtlas.PostFirst);
    }

    [Fact]
    public void Big_screen_stands_at_the_main_grandstand_facing_the_straight()
    {
        var screen = P.BigScreen;
        var at = new Vector2(screen.Position.X, screen.Position.Z);
        var (i, d) = Nearest(at);
        var s = F.Samples[i].S;
        var lap = F.Site.LapLength;
        Assert.True(Wrap(lap - s, lap) is > 0 and < 90 || s < 20, $"screen at s={s:0}");
        Assert.True(Vector2.Dot(at - F.Samples[i].Position, F.Samples[i].Left) < 0, "on the grandstand's (right) side");
        Assert.InRange(d, 18, 45);
        // It looks back up the straight: its face (-Z) towards the start of the straight.
        var face = -new Vector2(MathF.Sin(screen.Yaw), MathF.Cos(screen.Yaw));
        var back = TrackMeshBuilder.TrackPoint(F.Samples, lap - 200, 0);
        Assert.True(Vector2.Dot(face, Vector2.Normalize(new Vector2(back.X, back.Z) - at)) > 0.8f);
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        var again = Trackside.Plan(F.Site);
        Assert.Equal(P.TecPro, again.TecPro);
        Assert.Equal(P.Tyres, again.Tyres);
        Assert.Equal(P.ArmcoPosts, again.ArmcoPosts);
        Assert.Equal(P.FencePosts, again.FencePosts);
        Assert.Equal(P.MarshalPosts, again.MarshalPosts);
        Assert.Equal(P.CornerBoards, again.CornerBoards);
        Assert.Equal(P.BrakeBoards, again.BrakeBoards);
        Assert.Equal(P.CameraPlatforms, again.CameraPlatforms);
        Assert.Equal(P.BigScreen, again.BigScreen);
        Assert.Equal(P.Rails.Positions, again.Rails.Positions);
    }

    [Fact]
    public void Low_preset_prefix_is_every_other_post()
    {
        // Interleaved: the first half of a list is every second item, so Low's half density is even.
        var posts = P.ArmcoPosts;
        var half = posts.Take((posts.Count + 1) / 2).ToList();
        var rest = posts.Skip(half.Count).ToList();
        Assert.Equal(posts.Count, half.Count + rest.Count);
        var mean = (Vector3 a, Vector3 b) => Vector3.Distance(a, b);
        Assert.True(half.Zip(half.Skip(1)).Average(p => mean(p.First.Position, p.Second.Position)) < 20);
    }

    // ---- models and budget ----------------------------------------------------------------------------------

    private static readonly string ModelsDir = Path.Combine(RepoRoot(), "src", "ArcBox.Host", "games", "redline", "models");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ArcBox.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("ArcBox.slnx not found above the test output.");
    }

    public static TheoryData<string> Files()
    {
        var data = new TheoryData<string>();
        foreach (var c in TracksideModels.All)
            data.Add(c.File);
        return data;
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Glb_has_every_node_and_slot_within_budget(string file)
    {
        var contract = TracksideModels.All.Single(c => c.File == file);
        var glb = GlbFile.Load(Path.Combine(ModelsDir, file));
        Assert.Empty(contract.Missing(glb.Nodes.Keys, glb.UsedMaterials));
        Assert.InRange(glb.Triangles, 1, contract.Budget);
        Assert.Empty(glb.ExtensionsRequired);
        foreach (var node in contract.Nodes)
            Assert.NotNull(glb.Nodes[node].Mesh);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Godot_import_file_is_committed_next_to_the_glb(string file)
    {
        var import = File.ReadAllText(Path.Combine(ModelsDir, file) + ".import");
        Assert.Contains($"source_file=\"res://games/redline/models/{file}\"", import, StringComparison.Ordinal);
        Assert.StartsWith($"res://.godot/imported/{Path.GetFileName(file)}-", ModelChoice.ImportedPath(import), StringComparison.Ordinal);
    }

    [Fact]
    public void Trackside_fits_its_share_of_the_course_budget()
    {
        var barriers = NodeTriangles(Path.Combine(ModelsDir, TracksideModels.Barriers.File));
        var furniture = NodeTriangles(Path.Combine(ModelsDir, TracksideModels.Furniture.File));
        var screen = GlbFile.Load(Path.Combine(ModelsDir, TracksideModels.BigScreen.File)).Triangles;
        long props = (long)P.TecPro.Count * barriers["tecpro_block"] + (long)P.Tyres.Count * barriers["tyre_module"]
            + (long)P.ArmcoPosts.Count * barriers["armco_post"]
            + P.FencePosts.Sum(p => (long)barriers[p.Variant == 1 ? "fence_post_overhang" : "fence_post"])
            + (long)P.MarshalPosts.Count * furniture["marshal_post"] + (long)P.CornerBoards.Count * furniture["corner_board"]
            + (long)P.BrakeBoards.Count * furniture["brake_board"] + (long)P.CameraPlatforms.Count * furniture["camera_platform"]
            + screen;
        var meshes = (P.Rails.Indices.Count + P.Cables.Indices.Count + F.Track.Fence.Indices.Count) / 3;
        var total = props + meshes;
        TestContext.Current.SendDiagnosticMessage($"trackside draws {total} triangles ({props} instanced, {meshes} rails, cables and fence)");
        Assert.InRange(total, 1, TracksideModels.DrawnBudget);

        var models = Directory.EnumerateFiles(ModelsDir, "*.glb", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
        Assert.InRange(models, 1, 40L * 1024 * 1024);
    }

    private static Dictionary<string, int> NodeTriangles(string path)
    {
        var data = File.ReadAllBytes(path);
        var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(12));
        using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(data, 20, length));
        var root = doc.RootElement;
        var accessors = root.GetProperty("accessors");
        var meshes = root.GetProperty("meshes").EnumerateArray().Select(m => m.GetProperty("primitives").EnumerateArray()
            .Sum(p => accessors[p.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32() / 3)).ToList();
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in root.GetProperty("nodes").EnumerateArray())
            if (n.TryGetProperty("mesh", out var mesh) && n.TryGetProperty("name", out var name))
                result[name.GetString()!] = meshes[mesh.GetInt32()];
        return result;
    }

    private static double Wrap(double v, double lap)
    {
        var r = v % lap;
        return r < 0 ? r + lap : r;
    }

    private static (int Index, float Distance) Nearest(Vector2 p)
    {
        var q = F.Samples;
        var best = 0;
        var d = float.MaxValue;
        for (var i = 0; i < q.Count; i++)
        {
            var e = Vector2.DistanceSquared(q[i].Position, p);
            if (e < d) (d, best) = (e, i);
        }
        return (best, MathF.Sqrt(d));
    }
}

/// <summary>COTA as the TV builds it for the trackside: samples, track meshes, full terrain and the landmarks' structures.</summary>
internal sealed class TracksideFixture
{
    public static readonly Lazy<TracksideFixture> Cota = new(() => new TracksideFixture());

    private TracksideFixture()
    {
        var c = CircuitData.Load("cota");
        Samples = TrackGeometry.Resample(c);
        Track = TrackMeshBuilder.Build(c);
        var terrain = TerrainBuilder.Build(c, Samples);
        var ground = TerrainSampler.FromGrid(terrain);
        Site = new TracksideSite(Samples, Track, ground, c.LapLength, Trackside.Structures(c, Samples));
        Plan = Trackside.Plan(Site);
    }

    public IReadOnlyList<TrackSample> Samples { get; }
    public TrackMeshes Track { get; }
    public TracksideSite Site { get; }
    public TracksidePlan Plan { get; }
}
