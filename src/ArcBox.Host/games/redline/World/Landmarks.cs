using Godot;
using Redline.Presentation;
using Redline.Presentation.Meshes;
using Redline.Presentation.Models;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// COTA's landmarks: the main grandstand (right of the start straight) and the pit building with its pit lane (left),
/// the start gantry with five red lights over s = 0, the Turn 1 grandstand on the hill outside the hairpin with its red
/// roof, corner grandstands, and the Observation Tower with its red canopy inside the Turns 16–18 complex. Each is the
/// Blender model (<see cref="LandmarkModels"/>, through <see cref="SetPieces"/>) when it loads, else the procedural
/// primitives below, in the same place either way; the instanced crowd sits on the stands' tiers
/// (<see cref="GrandstandModel"/>) and keeps out of the Blender stands' aisles.
/// </summary>
public static class Landmarks
{
    /// <summary>A circle where trees must not grow (x, z, radius).</summary>
    public readonly record struct Footprint(float X, float Z, float Radius);

    private const float MainStandLateral = 21;
    private const float PitLaneInner = 20;
    private const float PitLaneOuter = 31;
    private const float PitLength = 330;
    private const float TurnOneS = 335;
    private const float TurnOneOffset = 32;

    private static readonly string[] CrowdPalette =
        ["d9d9d6", "cfc9bd", "3b4a63", "5a6170", "e2ddd2", "8f2a2a", "2d2f36", "a9b4c2", "c96a3a", "6d7f5a", "f2c14e", "1f5f9e", "e8e8e8", "b5332e"];

    private static float MainStandS(CircuitAssets a) => (float)a.Circuit.LapLength - 205;

    private static float PitS(CircuitAssets a) => (float)a.Circuit.LapLength - 175;

    public static IReadOnlyList<Footprint> Footprints(CircuitAssets a)
    {
        var list = new List<Footprint>();
        var main = Frame(a, MainStandS(a), -MainStandLateral, -1);
        list.Add(new Footprint(main.Origin.X, main.Origin.Z, 175));
        var pit = Frame(a, PitS(a), PitLaneInner, +1);
        list.Add(new Footprint(pit.Origin.X, pit.Origin.Z, 190));
        var t1 = TurnOneFrame(a);
        list.Add(new Footprint(t1.Origin.X, t1.Origin.Z, 95));
        var tower = TowerSite(a);
        list.Add(new Footprint(tower.X, tower.Z, 45));
        foreach (var xf in CornerStandFrames(a))
            list.Add(new Footprint(xf.Origin.X, xf.Origin.Z, 70));
        return list;
    }

    /// <summary>Builds every landmark under one node; <paramref name="gantry"/> is the start gantry, for the lights.</summary>
    public static Node3D Build(CircuitAssets a, out StartGantry gantry)
    {
        var root = new Node3D { Name = "Landmarks" };

        var main = Grandstand(LandmarkModels.MainStand, new Color("eeeff1"), seed: 5);
        main.Transform = Frame(a, MainStandS(a), -MainStandLateral, -1);
        main.Name = "MainGrandstand";
        root.AddChild(main);

        root.AddChild(PitLane(a));
        root.AddChild(PitLaneLines(a));
        root.AddChild(PitWall(a));
        if (PitStations(a) is { } stations)
        {
            root.AddChild(stations);
        }
        var pit = SetPieces.Instantiate(LandmarkModels.PitBuilding) ?? PitBuilding();
        pit.Name = "PitBuilding";
        pit.Transform = Frame(a, PitS(a), PitLaneOuter + 1, +1);
        root.AddChild(pit);

        var t1 = Grandstand(LandmarkModels.TurnOneStand, new Color("d0101c"), seed: 9);
        t1.Transform = TurnOneFrame(a);
        t1.Name = "TurnOneGrandstand";
        root.AddChild(t1);

        root.AddChild(Tower(a));

        var n = 0;
        foreach (var xf in CornerStandFrames(a))
        {
            var stand = Grandstand(LandmarkModels.CornerStand, n % 2 == 0 ? new Color("1b4f9c") : new Color("eeeff1"), seed: 20 + n);
            stand.Transform = xf;
            stand.Name = $"CornerGrandstand{++n}";
            root.AddChild(stand);
        }

        gantry = new StartGantry(a);
        root.AddChild(gantry);
        return root;
    }

    /// <summary>
    /// A level frame beside the track: origin on the track surface at (s, lateral); +Z points away from the track on
    /// <paramref name="side"/> (+1 left of travel, -1 right), +Y up, +X along the track as seen facing it.
    /// </summary>
    public static Transform3D Frame(CircuitAssets a, float s, float lateral, int side)
    {
        var pose = TrackPose.At(a.Samples, s, lateral);
        var fwd = new Vector3(pose.Forward.X, 0, pose.Forward.Z).Normalized();
        var left = new Vector3(fwd.Z, 0, -fwd.X);
        var basis = new Basis(-side * fwd, Vector3.Up, side * left);
        return new Transform3D(basis, MeshConvert.V(pose.Position));
    }

    private static readonly float CornerStandLength = LandmarkModels.CornerStand.Length;
    private const float CornerStandOffset = 30;

    /// <summary>
    /// Smaller grandstands on the outside of the three sharpest corners after Turn 1 (and away from the start straight),
    /// each placed only where its whole footprint stays clear of every part of the lap.
    /// </summary>
    private static IReadOnlyList<Transform3D> CornerStandFrames(CircuitAssets a)
    {
        var q = a.Samples;
        var lap = a.Circuit.LapLength;
        var picked = new List<double>();
        var result = new List<Transform3D>();
        foreach (var i in Enumerable.Range(0, q.Count).OrderByDescending(i => q[i].Curvature))
        {
            var s = q[i].S;
            if (s < 700 || s > lap - 800 || picked.Any(p => Math.Abs(p - s) < 600))
                continue;
            var outside = q[i].Turn > 0 ? 1 : -1;
            var half = (float)TrackGeometry.Width(s) / 2;
            var xf = Frame(a, (float)s, outside * (half + CornerStandOffset), outside);
            // Footprint corners (local X along, Z away from the track) must keep 22 m from the centre line.
            var clear = new[] { -1f, 1f }.SelectMany(x => new[] { 0f, 22f }.Select(z => xf * new Vector3(x * CornerStandLength / 2, 0, z)))
                .All(c => q.Min(t => new Vector2(t.Position.X - c.X, t.Position.Y - c.Z).Length()) > 22);
            if (!clear)
                continue;
            var ground = a.Ground.Height(xf.Origin.X, xf.Origin.Z);
            result.Add(xf with { Origin = xf.Origin with { Y = MathF.Min(xf.Origin.Y, ground) } });
            picked.Add(s);
            if (result.Count == 3)
                break;
        }
        return result;
    }

    private static Transform3D TurnOneFrame(CircuitAssets a)
    {
        var i = (int)Math.Round(TurnOneS / TrackGeometry.Step) % a.Samples.Count;
        var outside = a.Samples[i].Turn > 0 ? 1 : -1;
        var half = (float)TrackGeometry.Width(TurnOneS) / 2;
        var xf = Frame(a, TurnOneS, outside * (half + TurnOneOffset), outside);
        var ground = a.Ground.Height(xf.Origin.X, xf.Origin.Z);
        return xf with { Origin = xf.Origin with { Y = MathF.Min(xf.Origin.Y, ground) } };
    }

    private static Vector3 TowerSite(CircuitAssets a)
    {
        var pts = a.Circuit.Points.Skip(128).Take(24).ToList();
        var x = pts.Average(p => p.X);
        var z = pts.Average(p => p.Y);
        return new Vector3(x, a.Ground.Height(x, z), z);
    }

    /// <summary>
    /// A grandstand along local X, rows climbing away (+Z), with its crowd: the Blender stand (roof, trim and lettering
    /// in <paramref name="roof"/>'s colour) or the procedural one.
    /// </summary>
    private static Node3D Grandstand(GrandstandSpec spec, Color roof, int seed)
    {
        var node = new Node3D();
        var model = SetPieces.Instantiate(spec.Contract, roof);
        if (model is not null)
        {
            model.Name = "Stand";
            node.AddChild(model);
        }
        else
        {
            node.AddChild(ProceduralStand(spec.Length, spec.Rows, roof));
        }
        node.AddChild(Crowd(spec.Length, spec.Rows, seed, seated: model is not null));
        return node;
    }

    /// <summary>Stepped grandstand along local X, rows climbing away (+Z), with a roof and pillars.</summary>
    private static MeshInstance3D ProceduralStand(float length, int rows, Color roof)
    {
        var p = new Prims();
        for (var r = 0; r < rows; r++)
        {
            var top = 1 + r * 0.9f;
            var shade = r % 2 == 0 ? new Color("c5c8ce") : new Color("b9bcc3");
            p.Box(new Vector3(0, (top - 4) / 2, r * 1.2f + 0.6f), new Vector3(length, top + 4, 1.2f), shade);
        }
        var roofY = 4.5f + rows * 0.9f;
        var depth = rows * 1.2f;
        // Back wall in panels, open stepped ends, roof with a front fascia, pillars at the back.
        for (var x = -length / 2; x < length / 2 - 0.1f; x += 12.5f)
        {
            var w = MathF.Min(12.5f, length / 2 - x);
            p.Box(new Vector3(x + w / 2, roofY / 2 - 2, depth + 0.3f), new Vector3(w - 0.3f, roofY + 4, 0.6f), new Color("aeb3ba"));
            p.Box(new Vector3(x, roofY / 2 - 2, depth + 0.35f), new Vector3(0.35f, roofY + 4, 0.7f), new Color("7d828a"));
        }
        p.Box(new Vector3(0, roofY, depth / 2 - 1.5f), new Vector3(length + 4, 0.5f, depth + 6), roof);
        p.Box(new Vector3(0, roofY - 0.6f, -4.3f), new Vector3(length + 4, 1.4f, 0.4f), roof.Darkened(0.12f));
        for (var x = -length / 2; x <= length / 2 + 0.1f; x += 25)
            p.Box(new Vector3(x, roofY / 2, depth - 0.2f), new Vector3(0.6f, roofY, 0.6f), new Color("d0d2d6"));
        return new MeshInstance3D { Mesh = p.Commit(), MaterialOverride = Materials.VertexColour(0.75f, 0, "stand") };
    }

    /// <summary>
    /// The crowd: one capsule per seat (<see cref="GrandstandModel.SeatXs"/>) on every tread, some seats empty. On the
    /// Blender stands (<paramref name="seated"/>) the figures are slimmer and sit on the seat pans, about a seat-back
    /// high, and none sit in the aisles (stairs there); on the procedural stand they stand on the tread as before.
    /// </summary>
    private static MultiMeshInstance3D Crowd(float length, int rows, int seed, bool seated)
    {
        var rng = new Random(seed);
        var seats = new List<(Vector3 Pos, Color Colour)>();
        IReadOnlyList<float> aisles = seated ? GrandstandModel.Aisles(length) : [];
        var (radius, height, lift, depth) = seated ? (0.16f, 0.72f, 0.45f + 0.36f, 0.5f) : (0.21f, 0.86f, 0.42f, 0.45f);
        var xs = GrandstandModel.SeatXs(length);
        for (var r = 0; r < rows; r++)
            foreach (var x in xs)
            {
                if (aisles.Any(a => MathF.Abs(x - a) < GrandstandModel.AisleHalfWidth))
                    continue;
                if (rng.NextDouble() < 0.12)
                    continue;
                var c = new Color(CrowdPalette[rng.Next(CrowdPalette.Length)]).Darkened((float)rng.NextDouble() * 0.15f);
                var y = GrandstandModel.TreadTop(r) + lift;
                seats.Add((new Vector3(x + (float)(rng.NextDouble() - 0.5) * (seated ? 0.08f : 0.18f), y, (r * GrandstandModel.RowDepth) + depth), c));
            }
        var crowd = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new CapsuleMesh { Radius = radius, Height = height, RadialSegments = 6, Rings = 1 },
            InstanceCount = seats.Count,
        };
        for (var i = 0; i < seats.Count; i++)
        {
            crowd.SetInstanceTransform(i, new Transform3D(Basis.Identity, seats[i].Pos));
            crowd.SetInstanceColor(i, seats[i].Colour);
        }
        return new MultiMeshInstance3D
        {
            Name = "Crowd",
            Multimesh = crowd,
            MaterialOverride = Materials.VertexColour(0.9f, 0, "crowd"),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private static MeshInstance3D PitBuilding()
    {
        var p = new Prims();
        const float depth = 18;
        // Garages at ground level, glass hospitality band above, roof with a slim overhang.
        p.Box(new Vector3(0, 1.5f, depth / 2), new Vector3(PitLength, 9, depth), new Color("e7e8ea"));
        p.Box(new Vector3(0, 7.4f, depth / 2 - 0.6f), new Vector3(PitLength + 0.2f, 2.8f, depth), new Color("1c2738"));
        p.Box(new Vector3(0, 9.4f, depth / 2 - 1.5f), new Vector3(PitLength + 3, 1.2f, depth + 3), new Color("cfd3d8"));
        p.Box(new Vector3(0, 9.75f, -2.6f), new Vector3(PitLength + 3, 0.5f, 0.6f), new Color("e10600"));
        for (var x = -PitLength / 2 + 20; x < PitLength / 2 - 10; x += 37)
            p.Box(new Vector3(x, 10.6f, depth / 2 + 2), new Vector3(7, 1.6f, 4), new Color("9ea3aa"));
        for (var x = -PitLength / 2 + 6; x < PitLength / 2; x += 12)
            p.Box(new Vector3(x, 2.2f, -0.05f), new Vector3(10.6f, 4.4f, 0.2f), new Color("2a2c31"));
        return new MeshInstance3D { Mesh = p.Commit(), MaterialOverride = Materials.VertexColour(0.5f, 0.1f, "pit"), Name = "PitBuilding" };
    }

    private static MeshInstance3D PitLane(CircuitAssets a)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var start = PitS(a) - PitLength / 2 - 30;
        var end = PitS(a) + PitLength / 2 + 25;
        var colour = new Color("4a4b50");
        for (var s = start; s < end; s += 2)
        {
            Vector3 P(float ss, float lat) => MeshConvert.V(TrackMeshBuilder.TrackPoint(a.Samples, ss, lat)) + new Vector3(0, 0.04f, 0);
            var a0 = P(s, PitLaneInner);
            var b0 = P(s, PitLaneOuter);
            var a1 = P(s + 2, PitLaneInner);
            var b1 = P(s + 2, PitLaneOuter);
            st.SetColor(colour);
            st.SetNormal(Vector3.Up);
            // Outer edge is left (+lateral): seen from above, b0 → b1 → a1 → a0 is clockwise.
            foreach (var (v, lat) in new[] { (b0, PitLaneOuter), (b1, PitLaneOuter), (a1, PitLaneInner), (b0, PitLaneOuter), (a1, PitLaneInner), (a0, PitLaneInner) })
            {
                st.SetUV(new Vector2(s, lat));
                st.AddVertex(v);
            }
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Materials.PitLane, Name = "PitLane" };
    }

    /// <summary>
    /// The pit lane's painted lines, following the track like the lane itself: the fast lane's inner line, the fast/
    /// working lane divider and a box line at every garage divider.
    /// </summary>
    private static MeshInstance3D PitLaneLines(CircuitAssets a)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var white = new Color("ececec");
        Vector3 P(float ss, float lat) => MeshConvert.V(TrackMeshBuilder.TrackPoint(a.Samples, ss, lat)) + new Vector3(0, 0.06f, 0);
        void Strip(float s0, float s1, float lat0, float lat1)
        {
            // Seen from above, (s0, lat1) → (s1, lat1) → (s1, lat0) → (s0, lat0) is clockwise: Godot's front.
            var q = new[] { (s0, lat1), (s1, lat1), (s1, lat0), (s0, lat1), (s1, lat0), (s0, lat0) };
            foreach (var (ss, lat) in q)
            {
                st.SetColor(white);
                st.SetNormal(Vector3.Up);
                st.SetUV(new Vector2(ss, lat));
                st.AddVertex(P(ss, lat));
            }
        }
        var start = PitS(a) - (PitLength / 2) - 20;
        var end = PitS(a) + (PitLength / 2) + 15;
        foreach (var (from, to) in PitLaneModel.LaneLines)
        {
            for (var s = start; s < end; s += 2)
            {
                Strip(s, s + 2, from, to);
            }
        }
        foreach (var x in PitLaneModel.GarageDividers)
        {
            var s = PitS(a) - x;
            Strip(s - 0.07f, s + 0.07f, PitLaneModel.LaneLines[^1].To, PitLaneOuter - 0.2f);
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Materials.Lines, Name = "PitLaneLines" };
    }

    /// <summary>The concrete pit wall under the catch fence, between the track and the pit lane, following the track.</summary>
    private static MeshInstance3D PitWall(CircuitAssets a)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var face = new Color("e6e7e9");
        var top = new Color("c9ccd1");
        Vector3 P(float ss, float lat, float dy) => MeshConvert.V(TrackMeshBuilder.TrackPoint(a.Samples, ss, lat)) + new Vector3(0, dy, 0);
        void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 outward, Color c, float u0, float u1)
        {
            // Emit p0 p1 p2 / p0 p2 p3 in whichever order is clockwise seen from `outward` (Godot's front face).
            var n = (p1 - p0).Cross(p2 - p0);
            var flip = n.Dot(outward) > 0;
            var tris = flip ? new[] { p0, p2, p1, p0, p3, p2 } : new[] { p0, p1, p2, p0, p2, p3 };
            var uvs = flip
                ? new[] { new Vector2(u0, 0), new Vector2(u1, 1), new Vector2(u1, 0), new Vector2(u0, 0), new Vector2(u0, 1), new Vector2(u1, 1) }
                : new[] { new Vector2(u0, 0), new Vector2(u1, 0), new Vector2(u1, 1), new Vector2(u0, 0), new Vector2(u1, 1), new Vector2(u0, 1) };
            for (var i = 0; i < 6; i++)
            {
                st.SetColor(c);
                st.SetNormal(outward.Normalized());
                st.SetUV(uvs[i]);
                st.AddVertex(tris[i]);
            }
        }
        const float lo = -1.0f, hi = PitLaneModel.WallHeight, inner = PitLaneModel.WallInner, outer = PitLaneModel.WallOuter;
        var start = PitS(a) - (PitLength / 2) - 30;
        var end = PitS(a) + (PitLength / 2) + 25;
        for (var s = start; s < end; s += 2)
        {
            var across = (P(s, outer, 0) - P(s, inner, 0)).Normalized();
            Quad(P(s, inner, lo), P(s + 2, inner, lo), P(s + 2, inner, hi), P(s, inner, hi), -across, face, s, s + 2);
            Quad(P(s, inner, hi), P(s + 2, inner, hi), P(s + 2, outer, hi), P(s, outer, hi), Vector3.Up, top, s, s + 2);
            Quad(P(s, outer, hi), P(s + 2, outer, hi), P(s + 2, outer, lo), P(s, outer, lo), across, face, s, s + 2);
        }
        foreach (var (s, dir) in new[] { (start, -1f), (end, 1f) })
        {
            var along = (P(s + 1, inner, 0) - P(s - 1, inner, 0)).Normalized() * dir;
            Quad(P(s, inner, lo), P(s, outer, lo), P(s, outer, hi), P(s, inner, hi), along, face, 0, 1);
        }
        return new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = Materials.VertexColour(0.85f, 0, "pitwall"), Name = "PitWall" };
    }

    /// <summary>
    /// The pit-wall stations (<see cref="LandmarkModels.PitStation"/>), each framed at its own point on the track so the
    /// row follows the straight's rise; null when the model can't be used (the procedural pit has none).
    /// </summary>
    private static Node3D? PitStations(CircuitAssets a)
    {
        var root = new Node3D { Name = "PitWallStations" };
        foreach (var x in PitLaneModel.StationOffsets)
        {
            if (SetPieces.Instantiate(LandmarkModels.PitStation) is not { } station)
            {
                root.Free();
                return null;
            }
            station.Transform = Frame(a, PitS(a) - x, PitLaneModel.WallOuter, +1);
            root.AddChild(station);
        }
        return root;
    }

    private static Node3D Tower(CircuitAssets a)
    {
        var site = TowerSite(a);
        if (SetPieces.Instantiate(LandmarkModels.Tower) is { } model)
        {
            model.Name = "ObservationTower";
            model.Position = site;
            return model;
        }
        var p = new Prims();
        p.Cylinder(new Vector3(0, -2, 0), new Vector3(0, 70, 0), 2.6f, 1.5f, 24, new Color("eef0f2"));
        p.Cylinder(new Vector3(0, 68, 0), new Vector3(0, 71.5f, 0), 7.5f, 8.5f, 32, new Color("c9d2dc"));
        p.Cylinder(new Vector3(0, 71.5f, 0), new Vector3(0, 72.2f, 0), 8.8f, 8.8f, 32, new Color("f2f3f5"));
        p.Cylinder(new Vector3(0, 72.2f, 0), new Vector3(0, 77, 0), 1.2f, 0.4f, 12, new Color("eef0f2"));
        var red = new Color("d0101c");
        for (var i = 0; i < 40; i++)
        {
            var ang = Mathf.Tau * i / 40;
            var from = new Vector3(Mathf.Cos(ang) * 1.6f, 76, Mathf.Sin(ang) * 1.6f);
            var to = new Vector3(Mathf.Cos(ang + 0.5f) * 27, -1, Mathf.Sin(ang + 0.5f) * 27);
            p.Cylinder(from, to, 0.16f, 0.2f, 6, red, caps: false);
        }
        return new MeshInstance3D
        {
            Mesh = p.Commit(),
            MaterialOverride = Materials.VertexColour(0.4f, 0.2f, "tower"),
            Position = site,
            Name = "ObservationTower",
        };
    }
}

/// <summary>The start gantry across s = 0 with five red lights facing the grid; <see cref="SetLit"/> switches them.</summary>
public partial class StartGantry : Node3D
{
    private readonly List<MeshInstance3D> _lights = [];

    public StartGantry()
    {
    }

    public StartGantry(CircuitAssets a)
    {
        Name = "StartGantry";
        var half = (float)TrackGeometry.Width(0) / 2 + 2.6f;
        var pose = TrackPose.At(a.Samples, 0, 0);
        var fwd = MeshConvert.V(pose.Forward) with { Y = 0 };
        fwd = fwd.Normalized();
        var left = new Vector3(fwd.Z, 0, -fwd.X);
        // Local: X across the track (left), Y up, Z along travel.
        Transform = new Transform3D(new Basis(left, Vector3.Up, fwd), MeshConvert.V(pose.Position));

        if (SetPieces.Instantiate(LandmarkModels.StartGantry) is { } model)
        {
            AddChild(model);
            foreach (var name in StartGantryModel.Lights)
            {
                var pod = SetPieces.Mesh(model, name)!;
                pod.MaterialOverride = Off;
                pod.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                _lights.Add(pod);
            }
            return;
        }

        var p = new Prims();
        var steel = new Color("5d626b");
        foreach (var x in new[] { -half, half })
            p.Box(new Vector3(x, 4.4f, 0), new Vector3(0.55f, 9.6f, 0.55f), steel);
        p.Box(new Vector3(0, 8.95f, 0), new Vector3(2 * half + 0.6f, 0.75f, 0.7f), steel);
        p.Box(new Vector3(0, 9.55f, 0), new Vector3(2 * half + 0.6f, 0.12f, 0.9f), new Color("e10600"));
        p.Box(new Vector3(0, 7.75f, -0.1f), new Vector3(6.4f, 1.3f, 0.45f), new Color("121216"));
        AddChild(new MeshInstance3D { Mesh = p.Commit(), MaterialOverride = Materials.VertexColour(0.4f, 0.5f, "gantry") });

        var lamp = new SphereMesh { Radius = 0.26f, Height = 0.52f, RadialSegments = 16, Rings = 8 };
        for (var i = 0; i < 5; i++)
        {
            var light = new MeshInstance3D
            {
                Mesh = lamp,
                Position = new Vector3((i - 2) * 1.15f, 7.75f, -0.38f),
                MaterialOverride = Off,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            _lights.Add(light);
            AddChild(light);
        }
    }

    private static StandardMaterial3D Off => Materials.Plain("2a0606", 0.25f);

    private static StandardMaterial3D On => Materials.Emissive("ff1a1a", 6);

    /// <summary>Lights the first <paramref name="count"/> lamps (0 = lights out).</summary>
    public void SetLit(int count)
    {
        for (var i = 0; i < _lights.Count; i++)
        {
            _lights[i].MaterialOverride = i < count ? On : Off;
        }
    }
}
