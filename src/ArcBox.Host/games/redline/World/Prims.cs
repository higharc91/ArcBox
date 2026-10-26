using Godot;

namespace ArcBox.Host.Games.Redline.World;

/// <summary>
/// Accumulates simple vertex-coloured primitives (boxes, tapered cylinders, ellipsoids) into one surface, so a whole
/// landmark is one mesh and one draw call. Faces are wound clockwise from the front (Godot's convention).
/// </summary>
public sealed class Prims
{
    private readonly SurfaceTool _st = new();
    private bool _any;

    public Prims()
    {
        _st.Begin(Mesh.PrimitiveType.Triangles);
    }

    public ArrayMesh Commit(ArrayMesh? addTo = null)
    {
        var mesh = addTo ?? new ArrayMesh();
        if (_any)
        {
            _st.Commit(mesh);
        }
        return mesh;
    }

    /// <summary>An axis-aligned box of <paramref name="size"/> centred at <paramref name="xf"/>'s origin.</summary>
    public Prims Box(Transform3D xf, Vector3 size, Color colour)
    {
        var h = size / 2;
        // +X, -X, +Y, -Y, +Z, -Z faces: (normal, u axis, v axis) with u × v = -normal so the quad is clockwise from outside.
        Face(xf, new Vector3(1, 0, 0), new Vector3(0, 0, 1), new Vector3(0, 1, 0), h, colour);
        Face(xf, new Vector3(-1, 0, 0), new Vector3(0, 0, -1), new Vector3(0, 1, 0), h, colour);
        Face(xf, new Vector3(0, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, 1), h, colour);
        Face(xf, new Vector3(0, -1, 0), new Vector3(1, 0, 0), new Vector3(0, 0, -1), h, colour);
        Face(xf, new Vector3(0, 0, 1), new Vector3(-1, 0, 0), new Vector3(0, 1, 0), h, colour);
        Face(xf, new Vector3(0, 0, -1), new Vector3(1, 0, 0), new Vector3(0, 1, 0), h, colour);
        return this;
    }

    public Prims Box(Vector3 centre, Vector3 size, Color colour) => Box(new Transform3D(Basis.Identity, centre), size, colour);

    /// <summary>A capped cylinder from <paramref name="a"/> to <paramref name="b"/>, radius tapering ra → rb.</summary>
    public Prims Cylinder(Vector3 a, Vector3 b, float ra, float rb, int sides, Color colour, bool caps = true)
    {
        var axis = (b - a).Normalized();
        var helper = Mathf.Abs(axis.Y) < 0.95f ? Vector3.Up : Vector3.Right;
        var u = axis.Cross(helper).Normalized();
        var v = axis.Cross(u);
        _any = true;
        for (var i = 0; i < sides; i++)
        {
            var a0 = Mathf.Tau * i / sides;
            var a1 = Mathf.Tau * (i + 1) / sides;
            var d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0);
            var d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
            var p0 = a + d0 * ra;
            var p1 = a + d1 * ra;
            var q0 = b + d0 * rb;
            var q1 = b + d1 * rb;
            // Side quad, wound clockwise seen from outside.
            Tri(p0, q0, p1, d0, d0, d1, colour);
            Tri(p1, q0, q1, d1, d0, d1, colour);
            if (caps)
            {
                Tri(a, p0, p1, -axis, -axis, -axis, colour);
                Tri(b, q1, q0, axis, axis, axis, colour);
            }
        }
        return this;
    }

    /// <summary>An ellipsoid with <paramref name="radii"/>, optionally lumpy (radius × (1 + lump × noise)).</summary>
    public Prims Ellipsoid(Vector3 centre, Vector3 radii, int segments, int rings, Color colour, FastNoiseLite? lumps = null, float lump = 0)
    {
        _any = true;
        Vector3 Dir(int ring, int seg)
        {
            var theta = Mathf.Pi * ring / rings;
            var phi = Mathf.Tau * (seg % segments) / segments;
            return new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
        }
        Vector3 Pos(Vector3 d)
        {
            var k = lumps is null ? 1 : 1 + lump * lumps.GetNoise3Dv(d * 2.3f + centre * 7);
            return centre + d * radii * k;
        }
        for (var r = 0; r < rings; r++)
        {
            for (var s = 0; s < segments; s++)
            {
                var d00 = Dir(r, s);
                var d01 = Dir(r, s + 1);
                var d10 = Dir(r + 1, s);
                var d11 = Dir(r + 1, s + 1);
                Vector3 N(Vector3 d) => (d / radii).Normalized();
                // Seen from outside, ring r is above ring r + 1 and seg grows clockwise from above: wind accordingly.
                Tri(Pos(d00), Pos(d10), Pos(d01), N(d00), N(d10), N(d01), colour);
                Tri(Pos(d01), Pos(d10), Pos(d11), N(d01), N(d10), N(d11), colour);
            }
        }
        return this;
    }

    private void Face(Transform3D xf, Vector3 n, Vector3 u, Vector3 v, Vector3 h, Color colour)
    {
        var c = n * h;
        var du = u * h;
        var dv = v * h;
        var p00 = xf * (c - du - dv);
        var p10 = xf * (c + du - dv);
        var p11 = xf * (c + du + dv);
        var p01 = xf * (c - du + dv);
        var normal = (xf.Basis * n).Normalized();
        // u × v = -n: (p00, p10, p11) runs clockwise seen from the +n side.
        Tri(p00, p10, p11, normal, normal, normal, colour);
        Tri(p00, p11, p01, normal, normal, normal, colour);
    }

    private void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Color colour)
    {
        _any = true;
        _st.SetColor(colour);
        _st.SetNormal(na);
        _st.AddVertex(a);
        _st.SetNormal(nb);
        _st.AddVertex(b);
        _st.SetNormal(nc);
        _st.AddVertex(c);
    }
}
