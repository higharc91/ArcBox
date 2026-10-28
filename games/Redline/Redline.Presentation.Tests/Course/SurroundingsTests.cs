using System.Numerics;
using Redline.Presentation.Course;
using Redline.Presentation.Meshes;

namespace Redline.Presentation.Tests.Course;

/// <summary>
/// The surroundings plan (spec §2.4) on the real COTA site: the landmark footprints as the TV computes them
/// (<see cref="SurroundSite"/>), the trees the TV scatters, the full terrain grid. Nothing may overlap the track and its
/// run-off, the pit lane, the landmarks, the trees or each other; everything sits on the ground; the plan is
/// deterministic; budgets hold.
/// </summary>
public class SurroundingsTests
{
    private static SurroundSite S => SurroundSite.Cota.Value;

    private static SurroundingsPlan P => S.Plan;

    /// <summary>Every footprint a placed thing covers, with what it is (for messages).</summary>
    private static IEnumerable<(string What, Obb Box)> Boxes(SurroundingsPlan p) =>
    [
        ("promenade", p.Plaza.Promenade),
        ("amphitheatre", p.Plaza.Amphitheatre),
        .. p.CarParks.Select((l, i) => ($"car park {i}", l.Area)),
        .. p.Compounds.Select((c, i) => ($"compound {i}", c.Area)),
        .. p.Tents.Select((t, i) => ($"tent {i}", t.Footprint)),
        .. p.ParkedCars.Select((c, i) => ($"car {i}", c.Footprint)),
        .. p.Shrubs.Select((s, i) => ($"shrub {i}", s.Footprint)),
        .. p.PlazaProps.Select((s, i) => ($"plaza prop {i}", s.Footprint)),
        .. p.Groves.Select((s, i) => ($"grove {i}", s.Footprint)),
    ];

    private static IEnumerable<(int Road, Vector2 A, Vector2 B, float Half)> Segments(SurroundingsPlan p) =>
        p.Roads.SelectMany((r, k) => r.Points.Zip(r.Points.Skip(1), (a, b) => (k, a, b, r.Width / 2)));

    [Fact]
    public void Plan_has_every_feature()
    {
        Assert.InRange(P.CarParks.Count, 6, 14);
        Assert.Contains(P.CarParks, l => l.Infield);
        Assert.Contains(P.CarParks, l => !l.Infield);
        Assert.Contains(P.CarParks, l => l.Surface == LotSurface.Paved);
        Assert.Contains(P.CarParks, l => l.Surface == LotSurface.Grass);
        Assert.InRange(P.ParkedCars.Count, 800, Surroundings.MaxParkedCars);
        Assert.InRange(P.Compounds.Count, 3, 6);
        Assert.InRange(P.Tents.Count, 15, 80);
        Assert.Contains(P.Tents, t => t.Variant == TentModelsIndex.Marquee);
        Assert.InRange(P.Shrubs.Count, Surroundings.ShrubCount * 9 / 10, Surroundings.ShrubCount);
        Assert.InRange(P.Roads.Count, 8, 60);
        // Service roads in the infield.
        Assert.True(Segments(P).Count(s => S.Infield((s.A + s.B) / 2)) > 50, "infield service roads");
        Assert.InRange(P.Plaza.Promenade.HalfLength * 2, 40, 150);
        Assert.InRange(P.PlazaProps.Count, 12, 120);
        foreach (var v in new[] { PlazaModelsIndex.LightPole, PlazaModelsIndex.Kiosk, PlazaModelsIndex.FlagPole })
            Assert.Contains(P.PlazaProps, p => p.Variant == v);
        Assert.InRange(P.Groves.Count, Surroundings.GroveCount * 3 / 4, Surroundings.GroveCount);
        Assert.Contains(P.Groves, g => g.Variant == 1);
        Assert.InRange(P.FarClumps.Count, Surroundings.FarClumpCount * 9 / 10, Surroundings.FarClumpCount);
    }

    [Fact]
    public void Groves_line_the_low_ground_and_far_clumps_reach_the_horizon()
    {
        // Many groves stand on hollow ground (the creek lines).
        Assert.True(P.Groves.Count(g => Surroundings.Hollow(S.Ground, g.Footprint.Centre) > 1) > P.Groves.Count / 5);
        var horizon = new SurroundHorizon(S.Ground, S.Samples);
        var beyond = P.FarClumps.Select(c => horizon.Beyond(new Vector2(c.Position.X, c.Position.Z))).ToList();
        Assert.True(beyond.Count(b => b > 1000) > P.FarClumps.Count / 5, "clumps far out");
        Assert.True(beyond.Max() > 4000, "clumps out to the horizon");
        foreach (var c in P.FarClumps)
        {
            var at = new Vector2(c.Position.X, c.Position.Z);
            Assert.True(S.Samples.Min(t => Vector2.Distance(t.Position, at)) > 440, $"far clump at {at} is near the track");
            var y = horizon.Beyond(at) > 0 ? horizon.Height(at) : S.Ground.Height(at.X, at.Y);
            Assert.Equal(y, c.Position.Y, 2);
        }
    }

    [Fact]
    public void Plan_is_deterministic()
    {
        var again = Surroundings.Plan(S.Site);
        Assert.Equal(P.ParkedCars, again.ParkedCars);
        Assert.Equal(P.Shrubs, again.Shrubs);
        Assert.Equal(P.Tents, again.Tents);
        Assert.Equal(P.PlazaProps, again.PlazaProps);
        Assert.Equal(P.CarParks, again.CarParks);
        Assert.Equal(P.Plaza, again.Plaza);
        Assert.Equal(P.Roads.SelectMany(r => r.Points), again.Roads.SelectMany(r => r.Points));
        Assert.Equal(P.Clearings, again.Clearings);
        Assert.Equal(P.Groves, again.Groves);
        Assert.Equal(P.FarClumps, again.FarClumps);
    }

    [Fact]
    public void Nothing_stands_on_the_track_or_its_run_off()
    {
        var q = S.Samples;
        foreach (var (what, box) in Boxes(P))
            foreach (var t in q)
            {
                if (Vector2.Distance(t.Position, box.Centre) > box.Reach + 60)
                    continue;
                Assert.True(box.Distance(t.Position) >= Surroundings.TrackClearance(t.S),
                    $"{what} is {box.Distance(t.Position):0.0} m from the centre line at s = {t.S:0}");
            }
        foreach (var (k, a, b, half) in Segments(P))
            foreach (var t in q)
                Assert.True(Shapes.SegmentDistance(t.Position, a, b) - half >= Surroundings.TrackClearance(t.S),
                    $"road {k} reaches {Shapes.SegmentDistance(t.Position, a, b) - half:0.0} m from the centre line at s = {t.S:0}");
    }

    [Fact]
    public void Nothing_stands_in_the_pit_lane_or_on_the_pit_building()
    {
        var q = S.Samples;
        var pitS = S.Site.PitS;
        var points = new List<Vector2>();
        for (var s = pitS - 195; s <= pitS + 190; s += 3)
            for (var lat = 19.0; lat <= 51; lat += 2)
            {
                var p = TrackMeshBuilder.TrackPoint(q, s, lat);
                points.Add(new Vector2(p.X, p.Z));
            }
        foreach (var (what, box) in Boxes(P))
            Assert.DoesNotContain(points, p => box.Contains(p));
        foreach (var (k, a, b, half) in Segments(P))
            Assert.DoesNotContain(points, p => Shapes.SegmentDistance(p, a, b) < half);
    }

    [Fact]
    public void Nothing_stands_on_a_landmark()
    {
        foreach (var f in S.Site.Landmarks)
        {
            var tower = Vector2.Distance(f.Centre, S.Site.Tower) < 1;
            foreach (var (what, box) in Boxes(P))
            {
                // The plaza complex surrounds the tower: only the tower's own structure counts for it.
                var plazaPart = what is "promenade" or "amphitheatre" || what.StartsWith("plaza prop", StringComparison.Ordinal);
                var radius = tower && plazaPart ? Surroundings.TowerStructure : f.Radius;
                Assert.True(box.Distance(f.Centre) >= radius, $"{what} overlaps the landmark at {f.Centre} (r {f.Radius})");
            }
            foreach (var (k, a, b, half) in Segments(P))
                Assert.True(Shapes.SegmentDistance(f.Centre, a, b) - half >= f.Radius, $"road {k} crosses the landmark at {f.Centre}");
        }
    }

    [Fact]
    public void Nothing_stands_in_a_tree_and_only_the_plaza_clears_trees()
    {
        var standing = P.Trees;
        foreach (var (what, box) in Boxes(P))
            foreach (var t in standing)
                if (Vector2.Distance(t.Centre, box.Centre) < box.Reach + t.Radius)
                    Assert.True(box.Distance(t.Centre) >= t.Radius, $"{what} is inside the crown of the tree at {t.Centre}");
        foreach (var (k, a, b, half) in Segments(P))
            foreach (var t in standing)
                Assert.True(Shapes.SegmentDistance(t.Centre, a, b) - half >= t.Radius, $"road {k} runs through the tree at {t.Centre}");

        // The trees that went are exactly those in the plaza's clearings, and there are few of them.
        var cleared = S.Site.Trees.Where(t => P.Clearings.Any(c => Vector2.Distance(c.Centre, t.Centre) < c.Radius)).ToList();
        Assert.Equal(S.Site.Trees.Count - cleared.Count, standing.Count);
        Assert.InRange(cleared.Count, 0, S.Site.Trees.Count / 20);
        var complex = new[] { P.Plaza.Promenade, P.Plaza.Amphitheatre };
        foreach (var c in P.Clearings)
            Assert.Contains(complex, b => b.Distance(c.Centre) <= (Surroundings.MaxCrown * MathF.Sqrt(2)) + 0.01f);
    }

    [Fact]
    public void Placed_things_do_not_overlap_each_other()
    {
        // Sites: plaza parts, car parks, compounds, with each other.
        var sites = new List<(string, Obb)> { ("promenade", P.Plaza.Promenade), ("amphitheatre", P.Plaza.Amphitheatre) };
        sites.AddRange(P.CarParks.Select((l, i) => ($"car park {i}", l.Area)));
        sites.AddRange(P.Compounds.Select((c, i) => ($"compound {i}", c.Area)));
        for (var i = 0; i < sites.Count; i++)
            for (var j = i + 1; j < sites.Count; j++)
                if (!(i < 2 && j < 2))
                    Assert.False(sites[i].Item2.Overlaps(sites[j].Item2), $"{sites[i].Item1} overlaps {sites[j].Item1}");

        // Cars park inside their car parks, tents stand in their compounds, plaza props on the plaza.
        Assert.All(P.ParkedCars, c => Assert.Contains(P.CarParks, l => c.Footprint.Corners.All(k => l.Area.Grown(0.3f).Contains(k))));
        Assert.All(P.Tents, t => Assert.Contains(P.Compounds, c => t.Footprint.Corners.All(k => c.Area.Grown(0.3f).Contains(k))));
        var plaza = new[] { P.Plaza.Promenade, new Obb(P.Plaza.Tower, Vector2.UnitX, P.Plaza.ApronRadius, P.Plaza.ApronRadius) };
        Assert.All(P.PlazaProps, p => Assert.Contains(plaza, b => b.Contains(p.Footprint.Centre)));

        // Shrubs keep off every site and road; cars and tents keep off each other's sites.
        foreach (var s in P.Shrubs)
        {
            Assert.DoesNotContain(sites, x => x.Item2.Overlaps(s.Footprint));
            Assert.DoesNotContain(Segments(P), r => s.Footprint.Distance(r.A, r.B) < r.Half);
        }
        foreach (var c in P.ParkedCars)
            Assert.DoesNotContain(P.Compounds, x => x.Area.Overlaps(c.Footprint));

        // Roads keep off the sites, except where they run into their own entrance (the last 8 m).
        foreach (var (k, a, b, half) in Segments(P))
        {
            var road = P.Roads[k];
            var mid = (a + b) / 2;
            if (Vector2.Distance(mid, road.Points[^1]) < 14 || Vector2.Distance(mid, road.Points[0]) < 14)
                continue;
            foreach (var (what, box) in sites)
                Assert.True(box.Distance(a, b) >= half, $"road {k} runs over {what}");
        }
    }

    [Fact]
    public void Props_stand_on_the_ground()
    {
        foreach (var p in P.ParkedCars.Concat(P.Tents).Concat(P.Shrubs).Concat(P.PlazaProps).Concat(P.Groves))
            Assert.Equal(S.Ground.Height(p.Position.X, p.Position.Z), p.Position.Y, 3);
        // The amphitheatre stands on the highest ground under it (its skirt reaches down past the lowest).
        var a = P.Plaza.Amphitheatre;
        var lowest = float.MaxValue;
        for (var u = -a.HalfLength; u <= a.HalfLength; u += 4)
            for (var v = -a.HalfWidth; v <= a.HalfWidth; v += 4)
            {
                var q = a.At(u, v);
                var h = S.Ground.Height(q.X, q.Y);
                Assert.True(h <= P.Plaza.AmphitheatreOrigin.Y + 0.01f);
                lowest = MathF.Min(lowest, h);
            }
        Assert.True(P.Plaza.AmphitheatreOrigin.Y + Redline.Presentation.Models.SurroundingsModels.AmphiSkirt < lowest, "the skirt reaches the lowest ground");
    }

    [Fact]
    public void Draped_surfaces_follow_the_ground_and_face_up()
    {
        var surfaces = SurroundMeshes.Build(P, S.Ground);
        foreach (var (m, lo, hi) in new[]
                 {
                     (surfaces.Roads, SurroundMeshes.RoadLift, SurroundMeshes.RoadLift),
                     (surfaces.Lots, SurroundMeshes.LotLift, SurroundMeshes.LotLift),
                     (surfaces.Plaza, SurroundMeshes.PromenadeLift, SurroundMeshes.ApronLift),
                 })
        {
            Assert.NotEmpty(m.Indices);
            for (var i = 0; i < m.Positions.Count; i++)
            {
                var p = m.Positions[i];
                var lift = p.Y - S.Ground.Height(p.X, p.Z);
                Assert.InRange(lift, lo - 0.001f, hi + 0.001f);
                Assert.True(m.Normals[i].Y > 0.5f, $"normal {m.Normals[i]} at {p}");
            }
            // The middle of every triangle stays above the ground (the terrain never pokes through).
            for (var t = 0; t < m.Indices.Count; t += 3)
            {
                var c = (m.Positions[m.Indices[t]] + m.Positions[m.Indices[t + 1]] + m.Positions[m.Indices[t + 2]]) / 3;
                Assert.True(c.Y - S.Ground.Height(c.X, c.Z) > 0.02f, $"triangle at {c} is {c.Y - S.Ground.Height(c.X, c.Z):0.000} m over the ground");
                var n = Vector3.Cross(m.Positions[m.Indices[t + 2]] - m.Positions[m.Indices[t]], m.Positions[m.Indices[t + 1]] - m.Positions[m.Indices[t]]);
                Assert.True(n.Y > 0, $"clockwise seen from above: {n} at {c} in mesh {m.Indices.Count}");
            }
        }
    }

    [Fact]
    public void Terrain_attributes_measure_the_track_the_wear_the_infield_and_the_hollows()
    {
        var attrs = SurroundMeshes.TerrainAttributes(S.Terrain, S.Samples, P);
        Assert.Equal(S.Terrain.Positions.Count, attrs.Length);
        var grid = S.Terrain.Positions;
        for (var i = 0; i < grid.Count; i += 997)
        {
            var p = new Vector2(grid[i].X, grid[i].Z);
            var d = S.Samples.Min(t => Vector2.Distance(t.Position, p));
            // Exact where the mown bands are; a chamfer estimate (a few per cent long) further out, capped.
            if (d < SurroundMeshes.ExactReach)
                Assert.Equal(d, attrs[i].Distance, 1);
            else
                Assert.InRange(attrs[i].Distance, MathF.Min(d, SurroundMeshes.DistanceCap) - 0.5f, MathF.Min((d * 1.09f) + 1, SurroundMeshes.DistanceCap));
            Assert.InRange(attrs[i].Wear, 0, 1);
            Assert.InRange(attrs[i].Hollow, 0, 1);
            if (d > 2)
                Assert.Equal(S.Infield(p), attrs[i].Infield);
        }
        // Worn where a car park is, untouched far from everything; some hollows, some infield.
        var lot = P.CarParks[0].Area;
        var near = Enumerable.Range(0, grid.Count).Where(i => lot.Contains(new Vector2(grid[i].X, grid[i].Z))).ToList();
        Assert.NotEmpty(near);
        Assert.All(near, i => Assert.Equal(1, attrs[i].Wear, 3));
        Assert.Contains(attrs, a => a.Wear == 0);
        Assert.Contains(attrs, a => a.Hollow > 0.5f);
        Assert.InRange(attrs.Count(a => a.Infield), attrs.Length / 50, attrs.Length / 2);
    }

    [Fact]
    public void Terrain_has_no_steep_banks_between_parts_of_the_lap()
    {
        // The blend zone is relaxed (TerrainBuilder.Relax): between the Turn 1 hill and the low stadium section the ground
        // slopes evenly instead of dropping in a short shadowed bank (136 vertices steeper than 21° before).
        var steep = S.Terrain.Normals.Count(n => n.Y < 0.93f);
        Assert.InRange(steep, 0, 20);
    }

    [Fact]
    public void Low_preset_prefix_is_spread_over_the_whole_circuit()
    {
        // The Low preset draws the first half: it must still reach every car park and every part of the map.
        var half = P.ParkedCars.Take(P.ParkedCars.Count / 2).ToList();
        foreach (var lot in P.CarParks)
        {
            var all = P.ParkedCars.Count(c => lot.Area.Grown(0.5f).Contains(c.Footprint.Centre));
            var some = half.Count(c => lot.Area.Grown(0.5f).Contains(c.Footprint.Centre));
            Assert.InRange(some, all / 4, all * 3 / 4 + 1);
        }
        var shrubs = P.Shrubs.Take(P.Shrubs.Count / 2).ToList();
        Assert.InRange(shrubs.Average(s => s.Position.X), P.Shrubs.Average(s => s.Position.X) - 60, P.Shrubs.Average(s => s.Position.X) + 60);
        Assert.InRange(shrubs.Average(s => s.Position.Z), P.Shrubs.Average(s => s.Position.Z) - 60, P.Shrubs.Average(s => s.Position.Z) + 60);
    }

    [Fact]
    public void Far_land_tucks_under_the_terrain_and_reaches_the_horizon()
    {
        var horizon = new SurroundHorizon(S.Ground, S.Samples);
        var far = horizon.Build();
        var g = S.Ground;
        // Wherever it lies over the terrain grid it is below it, also between its own vertices.
        for (var t = 0; t < far.Indices.Count; t += 3)
        {
            var a = far.Positions[far.Indices[t]];
            var b = far.Positions[far.Indices[t + 1]];
            var c = far.Positions[far.Indices[t + 2]];
            foreach (var p in new[] { a, b, c, (a + b) / 2, (b + c) / 2, (a + c) / 2, (a + b + c) / 3 })
                if (g.Contains(new Vector2(p.X, p.Z)))
                    Assert.True(p.Y <= g.Height(p.X, p.Z) - 0.5f, $"far land above the terrain at {p}");
        }
        var xs = far.Positions.Select(p => p.X).ToList();
        Assert.InRange(xs.Max() - xs.Min(), 2 * SurroundHorizon.Reach - 1, 2 * SurroundHorizon.Reach + 1);
        Assert.InRange(far.Indices.Count / 3, 1000, 40_000);
        Assert.All(far.Normals, n => Assert.True(n.Y > 0.5f));
    }

    [Fact]
    public void Skyline_stands_on_the_horizon_towards_Austin()
    {
        var sky = P.Skyline;
        var centre = new Vector2(S.Samples.Average(t => t.Position.X), S.Samples.Average(t => t.Position.Y));
        var to = new Vector2(sky.Position.X, sky.Position.Z) - centre;
        Assert.Equal(Surroundings.SkylineDistance, to.Length(), 0);
        Assert.True(to.Y < 0 && to.X < 0, "north-north-west");
        Assert.True(Vector2.Dot(Vector2.Normalize(to), -sky.Facing) > 0.999f, "faces the circuit");
        Assert.Equal(Surroundings.SkylineDistance / Surroundings.AustinDistance, sky.Scale, 4);
        // Within the camera's far plane (9 km) and on the far land.
        Assert.True(to.Length() + 1000 < 9000);
        Assert.InRange(sky.Position.Y, new SurroundHorizon(S.Ground, S.Samples).Height(new Vector2(sky.Position.X, sky.Position.Z)) - 4, 60);
    }
}

/// <summary>COTA as the TV builds it: samples, full terrain, landmark footprints (<c>Landmarks.Footprints</c>), trees.</summary>
internal sealed class SurroundSite
{
    public static readonly Lazy<SurroundSite> Cota = new(() => new SurroundSite());

    private readonly Vector2[] _polygon;

    private SurroundSite()
    {
        var c = CircuitData.Load("cota");
        Samples = TrackGeometry.Resample(c);
        Terrain = TerrainBuilder.Build(c, Samples);
        Ground = TerrainSampler.FromGrid(Terrain);
        var q = Samples;
        var lap = (float)c.LapLength;
        (Vector3 O, Vector3 X, Vector3 Z) Frame(float s, float lateral, int side)
        {
            var pose = TrackPose.At(q, s, lateral);
            var fwd = Vector3.Normalize(new Vector3(pose.Forward.X, 0, pose.Forward.Z));
            var left = new Vector3(fwd.Z, 0, -fwd.X);
            return (pose.Position, -side * fwd, side * left);
        }
        KeepOut At(Vector3 o, float r) => new(new Vector2(o.X, o.Z), r);
        var fp = new List<KeepOut>
        {
            At(Frame(lap - 205, -21, -1).O, 175),
            At(Frame(lap - 175, 20, 1).O, 190),
        };
        var i1 = (int)Math.Round(335 / TrackGeometry.Step) % q.Count;
        var out1 = q[i1].Turn > 0 ? 1 : -1;
        fp.Add(At(Frame(335, out1 * ((float)TrackGeometry.Width(335) / 2 + 32), out1).O, 95));
        var pts = c.Points.Skip(128).Take(24).ToList();
        var tower = new Vector2(pts.Average(p => p.X), pts.Average(p => p.Y));
        fp.Add(new KeepOut(tower, 45));
        var picked = new List<double>();
        foreach (var i in Enumerable.Range(0, q.Count).OrderByDescending(i => q[i].Curvature))
        {
            var s = q[i].S;
            if (s < 700 || s > lap - 800 || picked.Any(p => Math.Abs(p - s) < 600))
                continue;
            var outside = q[i].Turn > 0 ? 1 : -1;
            var xf = Frame((float)s, outside * ((float)TrackGeometry.Width(s) / 2 + 30), outside);
            var clear = new[] { -1f, 1f }.SelectMany(x => new[] { 0f, 22f }.Select(z => xf.O + (xf.X * (x * 45)) + (xf.Z * z)))
                .All(k => q.Min(t => new Vector2(t.Position.X - k.X, t.Position.Y - k.Z).Length()) > 22);
            if (!clear)
                continue;
            fp.Add(At(xf.O, 70));
            picked.Add(s);
            if (picked.Count == 3)
                break;
        }
        var trees = TreeScatter.Place(q, Ground, 1500, seed: 99, minTrackDistance: 46,
            blocked: p => fp.Any(f => Vector2.DistanceSquared(p, f.Centre) < f.Radius * f.Radius));
        Site = new SurroundingsSite(q, Ground, lap, tower, fp, [.. trees.Select(t => new KeepOut(new Vector2(t.Position.X, t.Position.Z), t.Scale))]);
        Plan = Surroundings.Plan(Site);
        _polygon = [.. q.Select(t => t.Position)];
    }

    public IReadOnlyList<TrackSample> Samples { get; }
    public MeshData Terrain { get; }
    public TerrainSampler Ground { get; }
    public SurroundingsSite Site { get; }
    public SurroundingsPlan Plan { get; }

    public bool Infield(Vector2 p) => Shapes.Inside(_polygon, p);
}
