using System.Numerics;

namespace Redline.Presentation.Meshes;

/// <summary>
/// One cross-section of a lofted body: a superellipse |x/W|^N + |y/H|^N = 1 centred on (X, Y) at length position Z.
/// The lower half is scaled by <paramref name="Bottom"/> (below 1 flattens it, above 1 drops it towards the floor).
/// </summary>
internal readonly record struct Station(float Z, float Y, float W, float H, float N = 2.6f, float X = 0f, float Bottom = 0.55f)
{
    public static Station Lerp(Station a, Station b, float t) => new(
        float.Lerp(a.Z, b.Z, t), float.Lerp(a.Y, b.Y, t), float.Lerp(a.W, b.W, t), float.Lerp(a.H, b.H, t),
        float.Lerp(a.N, b.N, t), float.Lerp(a.X, b.X, t), float.Lerp(a.Bottom, b.Bottom, t));
}

/// <summary>One point of a lathe profile: radius from the X axis, position along it, colour; <c>Hard</c> creases the normal.</summary>
internal readonly record struct ProfilePoint(float R, float A, Vector4 Colour, bool Hard = false);

/// <summary>Procedural primitives for the car (lofts, lathes, tubes, airfoils, prisms, ellipsoids), each as a <see cref="Part"/> with outward normals.</summary>
internal static class Loft
{
    private const float PaintEpsilon = 1e-4f;

    /// <summary>Catmull-Rom densification of every station field (ends clamped), <paramref name="per"/> steps per span.</summary>
    public static List<Station> Densify(IReadOnlyList<Station> st, int per)
    {
        var result = new List<Station>();
        for (var i = 0; i < st.Count - 1; i++)
        {
            Station p0 = st[Math.Max(0, i - 1)], p1 = st[i], p2 = st[i + 1], p3 = st[Math.Min(st.Count - 1, i + 2)];
            for (var j = 0; j < per; j++)
            {
                var t = (float)j / per;
                result.Add(new Station(
                    Cr(p0.Z, p1.Z, p2.Z, p3.Z, t), Cr(p0.Y, p1.Y, p2.Y, p3.Y, t),
                    MathF.Max(0.004f, Cr(p0.W, p1.W, p2.W, p3.W, t)), MathF.Max(0.004f, Cr(p0.H, p1.H, p2.H, p3.H, t)),
                    MathF.Max(2f, Cr(p0.N, p1.N, p2.N, p3.N, t)), Cr(p0.X, p1.X, p2.X, p3.X, t),
                    MathF.Max(0.05f, Cr(p0.Bottom, p1.Bottom, p2.Bottom, p3.Bottom, t))));
            }
        }
        result.Add(st[^1]);
        return result;
    }

    private static float Cr(float a, float b, float c, float d, float t)
    {
        float t2 = t * t, t3 = t2 * t;
        return 0.5f * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3);
    }

    /// <summary>Height of the top centre line (Y + H) of the densified loft at length position <paramref name="z"/>, with its slope dY/dZ.</summary>
    public static (float Top, float Slope) TopAt(IReadOnlyList<Station> stations, int per, float z)
    {
        var st = Densify(stations, per);
        for (var i = 0; i < st.Count - 1; i++)
        {
            Station a = st[i], b = st[i + 1];
            if ((z <= a.Z && z >= b.Z) || (z >= a.Z && z <= b.Z))
            {
                var dz = b.Z - a.Z;
                var slope = MathF.Abs(dz) > 1e-6f ? (b.Y + b.H - a.Y - a.H) / dz : 0f;
                var t = MathF.Abs(dz) > 1e-6f ? (z - a.Z) / dz : 0f;
                return (float.Lerp(a.Y + a.H, b.Y + b.H, t), slope);
            }
        }
        throw new ArgumentOutOfRangeException(nameof(z), z, "Outside the loft.");
    }

    private static Vector3 SectionPoint(in Station s, int side, float rel)
    {
        var x = side * s.W * MathF.Pow(MathF.Max(0f, 1f - MathF.Pow(MathF.Abs(rel), s.N)), 1f / s.N);
        var y = s.H * rel * (rel < 0 ? s.Bottom : 1f);
        return new Vector3(s.X + x, s.Y + y, s.Z);
    }

    /// <summary>
    /// Lofts a closed superellipse tube through <paramref name="stations"/> (densified <paramref name="per"/> times).
    /// Columns are parametrised by <c>rel</c> (-1 bottom .. +1 top, the fraction of the half-height) so livery zones are
    /// horizontal bands. <paramref name="paint"/>(z, rel) colours each vertex; every value in <paramref name="relBreaks"/> and
    /// <paramref name="zBreaks"/> gets a doubled seam of vertices so colour changes there are crisp.
    /// </summary>
    public static Part Superellipse(IReadOnlyList<Station> stations, int per, int segs, Func<float, float, Vector4> paint,
        IReadOnlyList<float>? relBreaks = null, IReadOnlyList<float>? zBreaks = null, float nRef = 3f, bool caps = true)
    {
        if (segs % 4 != 0)
            throw new ArgumentException("segs must be a multiple of 4.", nameof(segs));
        var dense = Densify(stations, per);

        // Rows (sections along the length), with doubled seams at z breaks.
        var rows = new List<(Station S, float PaintZ, bool Connect)>();
        for (var i = 0; i < dense.Count; i++)
        {
            rows.Add((dense[i], dense[i].Z, true));
            if (i == dense.Count - 1 || zBreaks is null)
                continue;
            Station a = dense[i], b = dense[i + 1];
            var dir = MathF.Sign(b.Z - a.Z);
            foreach (var t in zBreaks.Select(z => (z - a.Z) / (b.Z - a.Z)).Where(t => t > 0 && t < 1).Order())
            {
                var s = Station.Lerp(a, b, t);
                rows.Add((s, s.Z - dir * PaintEpsilon, false));
                rows.Add((s, s.Z + dir * PaintEpsilon, true));
            }
        }

        // Columns around the ring: bottom, up the +X side, top, down the -X side; doubled seams at rel breaks.
        var cols = new List<(int Side, float Rel, float PaintRel, bool Connect)>();
        for (var k = 0; k < segs; k++)
        {
            var (side, rel) = RingColumn(k, segs, nRef);
            cols.Add((side, rel, rel, true));
            if (relBreaks is null)
                continue;
            var (nextSide, nextRel) = RingColumn(k + 1, segs, nRef);
            var dir = MathF.Sign(nextRel - rel);
            var seamSide = MathF.Abs(rel) == 1f ? nextSide : side; // top and bottom columns sit on the centre line
            var crossing = relBreaks.Where(r => (r - rel) * (r - nextRel) < 0).OrderBy(r => dir * r);
            foreach (var r in crossing)
            {
                cols.Add((seamSide, r, r - dir * PaintEpsilon, false));
                cols.Add((seamSide, r, r + dir * PaintEpsilon, true));
            }
        }

        var part = new Part();
        int nr = rows.Count, nc = cols.Count;
        var pos = new Vector3[nr, nc];
        for (var r = 0; r < nr; r++)
            for (var c = 0; c < nc; c++)
                pos[r, c] = SectionPoint(rows[r].S, cols[c].Side, cols[c].Rel);

        var ids = new int[nr, nc];
        for (var r = 0; r < nr; r++)
        {
            int rp = r, rn = r;
            while (rp > 0 && rows[rp].S.Z == rows[r].S.Z) rp--;
            while (rn < nr - 1 && rows[rn].S.Z == rows[r].S.Z) rn++;
            var centre = new Vector3(rows[r].S.X, rows[r].S.Y, rows[r].S.Z);
            for (var c = 0; c < nc; c++)
            {
                int cp = c, cn = c;
                do cp = (cp - 1 + nc) % nc; while (cp != c && pos[r, cp] == pos[r, c]);
                do cn = (cn + 1) % nc; while (cn != c && pos[r, cn] == pos[r, c]);
                var along = pos[rn, c] - pos[rp, c];
                var around = pos[r, cn] - pos[r, cp];
                var n = Vector3.Cross(along, around);
                if (Vector3.Dot(n, pos[r, c] - centre) < 0)
                    n = -n;
                ids[r, c] = part.Vertex(pos[r, c], n, new Vector2((float)c / nc, (float)r / (nr - 1)), paint(rows[r].PaintZ, cols[c].PaintRel));
            }
        }
        for (var r = 0; r < nr - 1; r++)
        {
            if (!rows[r].Connect)
                continue;
            for (var c = 0; c < nc; c++)
            {
                if (!cols[c].Connect)
                    continue;
                var c2 = (c + 1) % nc;
                part.Quad(ids[r, c], ids[r, c2], ids[r + 1, c2], ids[r + 1, c]);
            }
        }

        if (caps)
        {
            foreach (var (r, inner) in new[] { (0, 1), (nr - 1, nr - 2) })
            {
                var s = rows[r].S;
                var centre = new Vector3(s.X, s.Y, s.Z);
                var normal = CarMeshOps.SafeNormalize(centre - new Vector3(rows[inner].S.X, rows[inner].S.Y, rows[inner].S.Z));
                var mid = part.Vertex(centre, normal, new Vector2(0.5f, 0.5f), paint(rows[r].PaintZ, 0f));
                var ring = new int[nc];
                for (var c = 0; c < nc; c++)
                    ring[c] = part.Vertex(pos[r, c], normal, new Vector2((float)c / nc, 0f), part.Colors[ids[r, c]]);
                for (var c = 0; c < nc; c++)
                    if (cols[c].Connect)
                        part.Triangle(ring[c], ring[(c + 1) % nc], mid);
            }
        }
        return part;
    }

    /// <summary>Column k of a ring of <paramref name="segs"/>: k = 0 bottom, segs/4 the +X side, segs/2 top, 3 segs/4 the -X side.</summary>
    private static (int Side, float Rel) RingColumn(int k, int segs, float nRef)
    {
        var q = segs / 4;
        k %= segs;
        if (k == 0) return (1, -1f);
        if (k == 2 * q) return (1, 1f);
        if (k == q) return (1, 0f);
        if (k == 3 * q) return (-1, 0f);
        var theta = -MathF.PI / 2 + 2 * MathF.PI * k / segs;
        var sn = MathF.Sin(theta);
        return (k < 2 * q ? 1 : -1, MathF.Sign(sn) * MathF.Pow(MathF.Abs(sn), 2f / nRef));
    }

    /// <summary>
    /// Revolves <paramref name="profile"/> about the X axis (point = (A, R cos φ, R sin φ)). The profile must run so that
    /// (dA, -dR) points outward, e.g. a tyre goes up its -X sidewall, across the tread, down its +X sidewall.
    /// </summary>
    public static Part Lathe(IReadOnlyList<ProfilePoint> profile, int segs)
    {
        var part = new Part();
        var n = profile.Count;
        var normals = new Vector2[n]; // (r, a)
        for (var i = 0; i < n; i++)
        {
            var p = new Vector2(profile[i].R, profile[i].A);
            int prev = i, next = i;
            while (prev > 0 && Same(profile[prev], profile[i])) prev--;
            while (next < n - 1 && Same(profile[next], profile[i])) next++;
            Vector2 tangent;
            var dupNext = i < n - 1 && Same(profile[i + 1], profile[i]);
            var dupPrev = i > 0 && Same(profile[i - 1], profile[i]);
            if (profile[i].Hard && dupNext && prev != i)
                tangent = p - new Vector2(profile[prev].R, profile[prev].A);
            else if (profile[i].Hard && dupPrev && next != i)
                tangent = new Vector2(profile[next].R, profile[next].A) - p;
            else
                tangent = new Vector2(profile[next].R - profile[prev].R, profile[next].A - profile[prev].A);
            normals[i] = new Vector2(tangent.Y, -tangent.X); // (dA, -dR)
        }

        var ids = new int[n, segs + 1];
        for (var i = 0; i < n; i++)
        {
            for (var k = 0; k <= segs; k++)
            {
                var phi = 2 * MathF.PI * k / segs;
                float cs = MathF.Cos(phi), sn = MathF.Sin(phi);
                var pr = profile[i];
                ids[i, k] = part.Vertex(new Vector3(pr.A, pr.R * cs, pr.R * sn),
                    new Vector3(normals[i].Y, normals[i].X * cs, normals[i].X * sn),
                    new Vector2((float)k / segs, (float)i / (n - 1)), pr.Colour);
            }
        }
        for (var i = 0; i < n - 1; i++)
            for (var k = 0; k < segs; k++)
                part.Quad(ids[i, k], ids[i, k + 1], ids[i + 1, k + 1], ids[i + 1, k]);
        return part;

        static bool Same(ProfilePoint a, ProfilePoint b) => a.R == b.R && a.A == b.A;
    }

    /// <summary>A capped solid cylinder of radius <paramref name="r"/> and length <paramref name="length"/> along X, centred on the origin.</summary>
    public static Part Cylinder(float r, float length, int segs, Vector4 colour)
    {
        float h = length / 2;
        return Lathe([
            new(0, -h, colour), new(r, -h, colour, true), new(r, -h, colour, true),
            new(r, h, colour, true), new(r, h, colour, true), new(0, h, colour),
        ], segs);
    }

    /// <summary>
    /// A tube of elliptical section (<paramref name="rMajor"/> along the frame axis nearest <paramref name="majorHint"/>) along a
    /// Catmull-Rom curve through <paramref name="points"/>; a straight strut when given two points.
    /// </summary>
    public static Part Tube(IReadOnlyList<Vector3> points, int lengthSegs, int sides, float rMajor, float rMinor, Vector4 colour,
        Vector3? majorHint = null, bool caps = true)
    {
        var path = new List<Vector3>();
        if (points.Count == 2)
        {
            for (var i = 0; i <= lengthSegs; i++)
                path.Add(Vector3.Lerp(points[0], points[1], (float)i / lengthSegs));
        }
        else
        {
            var spans = points.Count - 1;
            for (var i = 0; i <= lengthSegs; i++)
            {
                var u = (float)i / lengthSegs * spans;
                var seg = Math.Min((int)u, spans - 1);
                var t = u - seg;
                Vector3 p0 = points[Math.Max(0, seg - 1)], p1 = points[seg], p2 = points[seg + 1], p3 = points[Math.Min(spans, seg + 2)];
                if (seg == 0) p0 = 2 * p1 - p2;
                if (seg + 2 > spans) p3 = 2 * p2 - p1;
                path.Add(new Vector3(Cr(p0.X, p1.X, p2.X, p3.X, t), Cr(p0.Y, p1.Y, p2.Y, p3.Y, t), Cr(p0.Z, p1.Z, p2.Z, p3.Z, t)));
            }
        }

        var part = new Part();
        var m = path.Count;
        var tangents = new Vector3[m];
        for (var i = 0; i < m; i++)
            tangents[i] = CarMeshOps.SafeNormalize(path[Math.Min(m - 1, i + 1)] - path[Math.Max(0, i - 1)]);
        var hint = majorHint ?? Vector3.UnitZ;
        if (MathF.Abs(Vector3.Dot(hint, tangents[0])) > 0.95f)
            hint = MathF.Abs(tangents[0].Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u0 = CarMeshOps.SafeNormalize(hint - tangents[0] * Vector3.Dot(hint, tangents[0]));
        var ids = new int[m, sides + 1];
        for (var i = 0; i < m; i++)
        {
            if (i > 0)
                u0 = CarMeshOps.SafeNormalize(u0 - tangents[i] * Vector3.Dot(u0, tangents[i]));
            var v0 = Vector3.Cross(tangents[i], u0);
            for (var k = 0; k <= sides; k++)
            {
                var phi = 2 * MathF.PI * k / sides;
                float cs = MathF.Cos(phi), sn = MathF.Sin(phi);
                ids[i, k] = part.Vertex(path[i] + cs * rMajor * u0 + sn * rMinor * v0, cs / rMajor * u0 + sn / rMinor * v0,
                    new Vector2((float)k / sides, (float)i / (m - 1)), colour);
            }
        }
        for (var i = 0; i < m - 1; i++)
            for (var k = 0; k < sides; k++)
                part.Quad(ids[i, k], ids[i, k + 1], ids[i + 1, k + 1], ids[i + 1, k]);

        if (caps)
        {
            foreach (var (i, sign) in new[] { (0, -1f), (m - 1, 1f) })
            {
                var normal = sign * tangents[i];
                var mid = part.Vertex(path[i], normal, new Vector2(0.5f, 0.5f), colour);
                var ring = new int[sides];
                for (var k = 0; k < sides; k++)
                    ring[k] = part.Vertex(part.Positions[ids[i, k]], normal, Vector2.Zero, colour);
                for (var k = 0; k < sides; k++)
                    part.Triangle(ring[k], ring[(k + 1) % sides], mid);
            }
        }
        return part;
    }

    /// <summary>
    /// A NACA 4-digit style wing with inverted camber (downforce): leading edge on the X axis at the origin, chord towards
    /// -Z, span along X centred on 0. <paramref name="spanOffset"/>(u), u = -1 .. 1 across the span, shifts each section.
    /// </summary>
    public static Part Airfoil(float chord, float span, float thick, float camber, int chordPts, int spanSegs, Vector4 colour,
        Func<float, Vector3>? spanOffset = null)
    {
        var n = chordPts;
        var upper = new Vector2[n + 1]; // (z, y)
        var lower = new Vector2[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var x = (1 - MathF.Cos((float)i / n * MathF.PI)) / 2;
            var yt = 5 * thick * (0.2969f * MathF.Sqrt(x) - 0.126f * x - 0.3516f * x * x + 0.2843f * x * x * x - 0.1036f * x * x * x * x);
            const float p = 0.4f;
            var yc = -(x < p ? camber / (p * p) * (2 * p * x - x * x) : camber / ((1 - p) * (1 - p)) * ((1 - 2 * p) + 2 * p * x - x * x));
            upper[i] = new Vector2(-x * chord, (yc + yt) * chord);
            lower[i] = new Vector2(-x * chord, (yc - yt) * chord);
        }
        // Closed loop: upper LE -> TE, then lower TE -> just behind the LE. Counter-clockwise in (z, y).
        var loop = new List<Vector2>(upper);
        for (var i = n; i >= 1; i--)
            loop.Add(lower[i]);
        var count = loop.Count;
        var normals2 = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            Vector2 t;
            if (i == n) t = loop[i] - loop[i - 1];                    // upper trailing edge: crease
            else if (i == n + 1) t = loop[i + 1] - loop[i];           // lower trailing edge: crease
            else t = loop[(i + 1) % count] - loop[(i - 1 + count) % count];
            normals2[i] = new Vector2(t.Y, -t.X);                     // outward for a counter-clockwise loop
        }

        var part = new Part();
        var ids = new int[spanSegs + 1, count];
        var sections = new Vector3[spanSegs + 1];
        for (var j = 0; j <= spanSegs; j++)
        {
            var u = 2f * j / spanSegs - 1f;
            sections[j] = new Vector3(u * span / 2, 0, 0) + (spanOffset?.Invoke(u) ?? Vector3.Zero);
            for (var i = 0; i < count; i++)
                ids[j, i] = part.Vertex(sections[j] + new Vector3(0, loop[i].Y, loop[i].X), new Vector3(0, normals2[i].Y, normals2[i].X),
                    new Vector2((float)i / count, (float)j / spanSegs), colour);
        }
        for (var j = 0; j < spanSegs; j++)
            for (var i = 0; i < count; i++)
                part.Quad(ids[j, i], ids[j, (i + 1) % count], ids[j + 1, (i + 1) % count], ids[j + 1, i]);

        foreach (var (j, sign) in new[] { (0, -1f), (spanSegs, 1f) })
        {
            var normal = new Vector3(sign, 0, 0);
            var up = new int[n + 1];
            var lo = new int[n + 1];
            for (var i = 0; i <= n; i++)
            {
                up[i] = part.Vertex(sections[j] + new Vector3(0, upper[i].Y, upper[i].X), normal, Vector2.Zero, colour);
                lo[i] = part.Vertex(sections[j] + new Vector3(0, lower[i].Y, lower[i].X), normal, Vector2.Zero, colour);
            }
            for (var i = 0; i < n; i++)
                part.Quad(up[i], up[i + 1], lo[i + 1], lo[i]);
        }
        return part;
    }

    /// <summary>
    /// Extrudes a simple polygon given in (u, v) along w = u x v, <paramref name="thickness"/> centred on the plane.
    /// </summary>
    public static Part Prism(IReadOnlyList<Vector2> polygon, float thickness, Vector3 uAxis, Vector3 vAxis, Vector4 colour)
    {
        var pts = polygon.ToList();
        if (SignedArea(pts) < 0)
            pts.Reverse();
        var w = Vector3.Normalize(Vector3.Cross(uAxis, vAxis));
        var half = thickness / 2;
        Vector3 At(Vector2 p, float s) => p.X * uAxis + p.Y * vAxis + s * w;

        var part = new Part();
        var tris = EarClip(pts);
        foreach (var s in new[] { -half, half })
        {
            var normal = MathF.Sign(s) * w;
            var ids = pts.Select(p => part.Vertex(At(p, s), normal, p, colour)).ToArray();
            foreach (var (a, b, c) in tris)
                part.Triangle(ids[a], ids[b], ids[c]);
        }
        for (var i = 0; i < pts.Count; i++)
        {
            Vector2 a = pts[i], b = pts[(i + 1) % pts.Count];
            var d = b - a;
            var normal = d.Y * uAxis - d.X * vAxis;
            part.Quad(part.Vertex(At(a, -half), normal, a, colour), part.Vertex(At(b, -half), normal, b, colour),
                part.Vertex(At(b, half), normal, b, colour), part.Vertex(At(a, half), normal, a, colour));
        }
        return part;
    }

    /// <summary>An axis-aligned box of the given full size centred on the origin.</summary>
    public static Part Box(Vector3 size, Vector4 colour) =>
        Prism([new(-size.Z / 2, -size.Y / 2), new(size.Z / 2, -size.Y / 2), new(size.Z / 2, size.Y / 2), new(-size.Z / 2, size.Y / 2)],
            size.X, Vector3.UnitZ, Vector3.UnitY, colour);

    /// <summary>
    /// An ellipsoid (or a latitude/longitude patch of one). Latitude is measured from the XZ plane, longitude about Y from +Z towards +X.
    /// </summary>
    public static Part Ellipsoid(Vector3 radii, int slices, int stacks, Vector4 colour,
        float latMin = -MathF.PI / 2, float latMax = MathF.PI / 2, float lonMin = 0, float lonMax = 2 * MathF.PI)
    {
        var part = new Part();
        var ids = new int[stacks + 1, slices + 1];
        for (var i = 0; i <= stacks; i++)
        {
            var lat = float.Lerp(latMin, latMax, (float)i / stacks);
            for (var k = 0; k <= slices; k++)
            {
                var lon = float.Lerp(lonMin, lonMax, (float)k / slices);
                var dir = new Vector3(MathF.Cos(lat) * MathF.Sin(lon), MathF.Sin(lat), MathF.Cos(lat) * MathF.Cos(lon));
                var p = dir * radii;
                ids[i, k] = part.Vertex(p, p / (radii * radii), new Vector2((float)k / slices, (float)i / stacks), colour);
            }
        }
        for (var i = 0; i < stacks; i++)
            for (var k = 0; k < slices; k++)
                part.Quad(ids[i, k], ids[i, k + 1], ids[i + 1, k + 1], ids[i + 1, k]);
        return part;
    }

    /// <summary>A flat elliptical disc in the XY plane facing +Z.</summary>
    public static Part Disc(float rx, float ry, int segs, Vector4 colour)
    {
        var part = new Part();
        var mid = part.Vertex(Vector3.Zero, Vector3.UnitZ, new Vector2(0.5f, 0.5f), colour);
        var ring = new int[segs];
        for (var k = 0; k < segs; k++)
        {
            var a = 2 * MathF.PI * k / segs;
            ring[k] = part.Vertex(new Vector3(rx * MathF.Cos(a), ry * MathF.Sin(a), 0), Vector3.UnitZ,
                new Vector2(0.5f + 0.5f * MathF.Cos(a), 0.5f + 0.5f * MathF.Sin(a)), colour);
        }
        for (var k = 0; k < segs; k++)
            part.Triangle(mid, ring[k], ring[(k + 1) % segs]);
        return part;
    }

    private static float SignedArea(IReadOnlyList<Vector2> p)
    {
        var a = 0f;
        for (var i = 0; i < p.Count; i++)
        {
            Vector2 c = p[i], d = p[(i + 1) % p.Count];
            a += c.X * d.Y - d.X * c.Y;
        }
        return a / 2;
    }

    /// <summary>Ear-clipping triangulation of a simple counter-clockwise polygon.</summary>
    private static List<(int, int, int)> EarClip(IReadOnlyList<Vector2> p)
    {
        var idx = Enumerable.Range(0, p.Count).ToList();
        var result = new List<(int, int, int)>();
        var guard = 0;
        while (idx.Count > 3 && guard++ < 10_000)
        {
            var clipped = false;
            for (var i = 0; i < idx.Count; i++)
            {
                int a = idx[(i - 1 + idx.Count) % idx.Count], b = idx[i], c = idx[(i + 1) % idx.Count];
                if (Cross(p[b] - p[a], p[c] - p[b]) <= 0)
                    continue;
                var inside = false;
                foreach (var o in idx)
                {
                    if (o == a || o == b || o == c)
                        continue;
                    if (Cross(p[b] - p[a], p[o] - p[a]) >= 0 && Cross(p[c] - p[b], p[o] - p[b]) >= 0 && Cross(p[a] - p[c], p[o] - p[c]) >= 0)
                    {
                        inside = true;
                        break;
                    }
                }
                if (inside)
                    continue;
                result.Add((a, b, c));
                idx.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
                break;
        }
        if (idx.Count == 3)
            result.Add((idx[0], idx[1], idx[2]));
        return result;

        static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;
    }
}
