"""Pure-Python mesh building, authored in the Godot frame (metres, +Y up, +Z forward, +X left; see frame.py).

A `Mesh` is a list of vertices and polygon faces (quads/ngons allowed), each face carrying a material *slot name*,
optional per-corner UVs and a smooth flag. Builders return Meshes; `blend.make()` turns one into a Blender object.
Faces are wound counter-clockwise seen from outside (Blender's convention: normals by the right-hand rule); closed
builders fix their orientation from the signed volume, open ones from an explicit outward direction.

Builders: Section + loft() (superellipse lofts with livery zones and panel-line grooves), airfoil() + wing() (wing
elements), plate() (extruded outline), tube() (round or elliptic tubes along a path), lathe() (surfaces of revolution
about X, with circumferential UVs), box(), ellipsoid(), cylinder(). Transforms: T(), Rx(), Ry(), Rz(), S().
"""
import math
from dataclasses import dataclass, replace

from mathutils import Matrix, Vector


# ---- transforms (Godot frame) ------------------------------------------------------------------------------

def T(x, y=None, z=None):
    if y is None:
        x, y, z = x
    return Matrix.Translation((x, y, z))


def Rx(a):
    return Matrix.Rotation(a, 4, "X")


def Ry(a):
    return Matrix.Rotation(a, 4, "Y")


def Rz(a):
    return Matrix.Rotation(a, 4, "Z")


def S(x, y=None, z=None):
    y = x if y is None else y
    z = x if z is None else z
    m = Matrix.Identity(4)
    m[0][0], m[1][1], m[2][2] = x, y, z
    return m


MIRROR_X = S(-1, 1, 1)


class Mesh:
    def __init__(self):
        self.verts = []
        self.faces = []
        self.mats = []
        self.uvs = []
        self.smooth = []

    def vert(self, p):
        self.verts.append(Vector(p))
        return len(self.verts) - 1

    def face(self, idx, mat, uv=None, smooth=True):
        idx = tuple(idx)
        if len(set(idx)) < 3:
            return
        self.faces.append(idx)
        self.mats.append(mat)
        self.uvs.append(tuple(uv) if uv else None)
        self.smooth.append(smooth)

    def extend(self, other, matrix=None):
        base = len(self.verts)
        mirrored = matrix is not None and matrix.to_3x3().determinant() < 0
        for v in other.verts:
            self.verts.append(matrix @ v if matrix is not None else v.copy())
        for f, m, uv, s in zip(other.faces, other.mats, other.uvs, other.smooth):
            idx = tuple(base + i for i in f)
            if mirrored:
                idx = idx[::-1]
                uv = uv[::-1] if uv else None
            self.faces.append(idx)
            self.mats.append(m)
            self.uvs.append(uv)
            self.smooth.append(s)
        return self

    def transformed(self, matrix):
        return Mesh().extend(self, matrix)

    def mirrored_x(self):
        return self.transformed(MIRROR_X)

    def paint(self, mat):
        self.mats = [mat] * len(self.faces)
        return self

    def flip(self):
        self.faces = [f[::-1] for f in self.faces]
        self.uvs = [uv[::-1] if uv else None for uv in self.uvs]
        return self

    def face_normal(self, f):
        """Newell normal (unnormalised, length = 2 * area)."""
        n = Vector((0, 0, 0))
        for i in range(len(f)):
            a, b = self.verts[f[i]], self.verts[f[(i + 1) % len(f)]]
            n.x += (a.y - b.y) * (a.z + b.z)
            n.y += (a.z - b.z) * (a.x + b.x)
            n.z += (a.x - b.x) * (a.y + b.y)
        return n

    def centroid(self, f):
        c = Vector((0, 0, 0))
        for i in f:
            c += self.verts[i]
        return c / len(f)

    def signed_volume(self):
        vol = 0.0
        for f in self.faces:
            a = self.verts[f[0]]
            for i in range(1, len(f) - 1):
                vol += a.dot(self.verts[f[i]].cross(self.verts[f[i + 1]])) / 6.0
        return vol

    def orient_closed(self):
        """Flip every face if the (closed) mesh is inside out."""
        if self.signed_volume() < 0:
            self.flip()
        return self

    def orient_outward(self, outward):
        """Flip faces one by one so each normal agrees with outward(centroid) -> direction vector."""
        for k, f in enumerate(self.faces):
            if self.face_normal(f).dot(Vector(outward(self.centroid(f)))) < 0:
                self.faces[k] = f[::-1]
                uv = self.uvs[k]
                self.uvs[k] = uv[::-1] if uv else None
        return self

    def bounds(self):
        xs = [v.x for v in self.verts]
        ys = [v.y for v in self.verts]
        zs = [v.z for v in self.verts]
        return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))


def merge(*meshes):
    out = Mesh()
    for m in meshes:
        out.extend(m)
    return out


# ---- interpolation --------------------------------------------------------------------------------------

def _cr(a, b, c, d, t):
    t2, t3 = t * t, t * t * t
    return 0.5 * (2 * b + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3)


def spline(points, per):
    """Catmull-Rom through `points` (ends clamped), `per` steps per span; returns Vectors."""
    pts = [Vector(p) for p in points]
    if len(pts) < 3 or per <= 1:
        return pts
    out = []
    for i in range(len(pts) - 1):
        p0, p1, p2, p3 = pts[max(0, i - 1)], pts[i], pts[i + 1], pts[min(len(pts) - 1, i + 2)]
        for j in range(per):
            t = j / per
            out.append(Vector(tuple(_cr(p0[k], p1[k], p2[k], p3[k], t) for k in range(3))))
    out.append(pts[-1])
    return out


# ---- superellipse lofts ---------------------------------------------------------------------------------

@dataclass(frozen=True)
class Section:
    """A cross-section in the plane Z = z, centred on (x, y). The upper half is |dx/w|^n + |dy/top|^n = 1; the lower
    half uses `bottom` and exponent `nb`, and narrows towards its base by `pinch` (0 = none, 0.6 = a deep undercut).
    `rel` runs -1 (bottom centre) .. 0 (widest) .. 1 (top centre)."""
    z: float
    y: float
    w: float
    top: float
    bottom: float
    n: float = 2.6
    nb: float = 2.6
    x: float = 0.0
    pinch: float = 0.0

    def point(self, side, rel):
        a = abs(rel)
        if rel >= 0:
            dx = self.w * max(0.0, 1 - a ** self.n) ** (1 / self.n)
            dy = self.top * a
        else:
            dx = self.w * max(0.0, 1 - a ** self.nb) ** (1 / self.nb) * (1 - self.pinch * a)
            dy = -self.bottom * a
        return Vector((self.x + side * dx, self.y + dy, self.z))

    def centre(self):
        return Vector((self.x, self.y + 0.5 * (self.top - self.bottom) * 0.3, self.z))


_FIELDS = ("z", "y", "w", "top", "bottom", "n", "nb", "x", "pinch")


def _lerp_section(a, b, t):
    return Section(*(getattr(a, f) + (getattr(b, f) - getattr(a, f)) * t for f in _FIELDS))


def densify(stations, per):
    """Catmull-Rom densification of every Section field, `per` steps per span."""
    out = []
    st = list(stations)
    for i in range(len(st) - 1):
        p0, p1, p2, p3 = st[max(0, i - 1)], st[i], st[i + 1], st[min(len(st) - 1, i + 2)]
        for j in range(per):
            t = j / per
            vals = [_cr(getattr(p0, f), getattr(p1, f), getattr(p2, f), getattr(p3, f), t) for f in _FIELDS]
            vals[2] = max(0.003, vals[2])
            vals[3] = max(0.003, vals[3])
            vals[4] = max(0.003, vals[4])
            vals[5] = max(1.6, vals[5])
            vals[6] = max(1.2, vals[6])
            out.append(Section(*vals))
    out.append(st[-1])
    return out


def section_at(stations, per, z):
    """The densified section at length position z (linear between dense rows)."""
    dense = densify(stations, per)
    for a, b in zip(dense, dense[1:]):
        if (a.z - z) * (b.z - z) <= 0 and a.z != b.z:
            return _lerp_section(a, b, (z - a.z) / (b.z - a.z))
    raise ValueError(f"z={z} outside the loft")


def ring_rels(segs_half, n_ref=3.0, breaks=(), grooves=(), groove_width=0.012):
    """rel values for one side, -1..1: `segs_half` steps uniform in angle (mapped through the superellipse), plus
    each value in `breaks` (livery boundaries) and a (r - w, r, r + w) triplet for each panel-line groove at r."""
    rels = set()
    for k in range(segs_half + 1):
        th = -math.pi / 2 + math.pi * k / segs_half
        s = math.sin(th)
        rels.add(round(math.copysign(abs(s) ** (2 / n_ref), s), 6))
    for b in breaks:
        rels.add(round(b, 6))
    for g in grooves:
        for d in (-groove_width, 0, groove_width):
            rels.add(round(g + d, 6))
    out = sorted(rels)
    # Drop values that crowd a break or groove (keeps quads well shaped).
    keep = set(round(b, 6) for b in breaks)
    for g in grooves:
        keep.update(round(g + d, 6) for d in (-groove_width, 0, groove_width))
    pruned = []
    for r in out:
        if r in keep or r in (-1.0, 1.0) or all(abs(r - k) > groove_width * 1.5 for k in keep):
            pruned.append(r)
    return pruned


def loft(stations, per, rels, mat, *, z_breaks=(), z_grooves=(), rel_grooves=(), groove_depth=0.003,
         groove_width=0.006, cap_start=True, cap_end=True, sides=(1, -1)):
    """Lofts a closed superellipse tube through `stations` (ordered along Z either way).

    rels: the ring's rel values for one side (from ring_rels); mat(z, rel, side) -> slot name per face.
    z_breaks: lengths where a ring is forced (livery boundaries across the body); z_grooves / rel_grooves: panel lines,
    a narrow inset ring or column `groove_depth` deep. sides=(1,) builds only the +X half (for parts that meet a
    mirror plane or another body).
    """
    dense = densify(stations, per)
    zs = [s.z for s in dense]
    extra = list(z_breaks)
    for g in z_grooves:
        extra += [g - groove_width, g, g + groove_width]
    rows = list(dense)
    for ze in extra:
        for i in range(len(rows) - 1):
            a, b = rows[i], rows[i + 1]
            if (a.z - ze) * (b.z - ze) < 0:
                rows.insert(i + 1, _lerp_section(a, b, (ze - a.z) / (b.z - a.z)))
                break
    groove_rows = {round(g, 6) for g in z_grooves}
    groove_cols = {round(g, 6) for g in rel_grooves}

    cols = []
    if 1 in sides:
        cols += [(1, r) for r in rels]
    if -1 in sides:
        tail = list(reversed(rels))
        if 1 in sides:
            tail = tail[1:-1]
        cols += [(-1, r) for r in tail]
    closed_ring = len(sides) == 2

    m = Mesh()
    grid = []
    for s in rows:
        inset_row = round(s.z, 6) in groove_rows
        c = Vector((s.x, s.y, s.z))
        ring = []
        for side, r in cols:
            p = s.point(side, r)
            if inset_row or round(r, 6) in groove_cols:
                d = p - c
                d.z = 0
                if d.length > 1e-6:
                    p = p - d.normalized() * groove_depth
            ring.append(m.vert(p))
        grid.append(ring)

    nc = len(cols)
    span = nc if closed_ring else nc - 1
    for i in range(len(rows) - 1):
        zm = 0.5 * (rows[i].z + rows[i + 1].z)
        for k in range(span):
            k2 = (k + 1) % nc
            (sa, ra), (sb, rb) = cols[k], cols[k2]
            rm = 0.5 * (ra + rb)
            side = sa if abs(ra) < 1 else sb
            m.face((grid[i][k], grid[i][k2], grid[i + 1][k2], grid[i + 1][k]), mat(zm, rm, side),
                   uv=((k / nc, i / len(rows)), (k2 / nc, i / len(rows)), (k2 / nc, (i + 1) / len(rows)),
                       (k / nc, (i + 1) / len(rows))))
    for cap, i in ((cap_start, 0), (cap_end, len(rows) - 1)):
        if not cap:
            continue
        s = rows[i]
        ring = grid[i]
        if closed_ring:
            ctr = m.vert(Vector((s.x, s.y, s.z)))
            for k in range(nc):
                m.face((ring[k], ring[(k + 1) % nc], ctr), mat(s.z, 0.5 * (cols[k][1] + cols[(k + 1) % nc][1]), cols[k][0]))
        else:
            m.face(ring, mat(s.z, 0.0, sides[0]))
    if closed_ring:
        m.orient_closed()
    else:
        z0, z1 = rows[0].z, rows[-1].z
        zc = 0.5 * (z0 + z1)

        def outward(c):
            if abs(c.z - z0) < 1e-6 or abs(c.z - z1) < 1e-6:  # an end cap
                return Vector((0, 0, c.z - zc))
            s = min(rows, key=lambda r: abs(r.z - c.z))
            return Vector((c.x - s.x, c.y - s.y, 0))
        m.orient_outward(outward)
    return m


# ---- airfoils and wings ------------------------------------------------------------------------------------

def airfoil(n=16, thickness=0.12, camber=0.04, camber_pos=0.4, te=0.004):
    """NACA-4-style section with cosine spacing: closed loop of (s, t), s 0..1 along the chord (LE at 0), t up.
    Starts at the trailing edge upper side, runs to the leading edge and back along the lower side."""
    def thick(x):
        return 5 * thickness * (0.2969 * math.sqrt(x) - 0.126 * x - 0.3516 * x * x + 0.2843 * x ** 3 - 0.1036 * x ** 4)

    def camber_line(x):
        p, mx = camber_pos, camber
        if x < p:
            return mx / (p * p) * (2 * p * x - x * x)
        return mx / ((1 - p) ** 2) * ((1 - 2 * p) + 2 * p * x - x * x)

    xs = [0.5 * (1 - math.cos(math.pi * i / n)) for i in range(n + 1)]
    upper = [(x, camber_line(x) + max(thick(x), te * x)) for x in xs]
    lower = [(x, camber_line(x) - max(thick(x), te * x)) for x in xs]
    return list(reversed(upper)) + lower[1:-1]


@dataclass(frozen=True)
class Span:
    """One spanwise station of a wing element: leading edge at (x, y, z), chord, angle of attack (radians, trailing
    edge up), thickness/camber fractions of the chord. Chord runs towards -Z (backwards)."""
    x: float
    y: float
    z: float
    chord: float
    aoa: float
    thickness: float = 0.1
    camber: float = -0.05


def wing(spans, mat, n=14, per=1, cap=True):
    """Lofts airfoil sections along the span (stations ordered along X). Downforce camber is negative (bulges down)."""
    if per > 1:
        pts = spline([(s.x, s.y, s.z) for s in spans], per)
        aux = spline([(s.chord, s.aoa, s.thickness) for s in spans], per)
        cam = spline([(s.camber, 0, 0) for s in spans], per)
        spans = [Span(p.x, p.y, p.z, a[0], a[1], a[2], c[0]) for p, a, c in zip(pts, aux, cam)]
    m = Mesh()
    rings = []
    for sp in spans:
        prof = airfoil(n, sp.thickness, sp.camber)
        d = Vector((0, math.sin(sp.aoa), -math.cos(sp.aoa)))
        u = Vector((0, math.cos(sp.aoa), math.sin(sp.aoa)))
        le = Vector((sp.x, sp.y, sp.z))
        rings.append([m.vert(le + d * (s * sp.chord) + u * (t * sp.chord)) for s, t in prof])
    k = len(rings[0])
    for i in range(len(rings) - 1):
        for j in range(k):
            j2 = (j + 1) % k
            m.face((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]), mat,
                   uv=((j / k, i), ((j + 1) / k, i), ((j + 1) / k, i + 1), (j / k, i + 1)))
    if cap:
        m.face(rings[0], mat, smooth=False)
        m.face(rings[-1][::-1], mat, smooth=False)
    return m.orient_closed()


# ---- plates, tubes, lathes, primitives -------------------------------------------------------------------

def plate(outline, thickness, origin=(0, 0, 0), u=(0, 0, 1), v=(0, 1, 0), mat="carbon"):
    """Extrudes a 2D outline (u, v coordinates; any simple polygon) `thickness` along u x v, centred on the plane."""
    o, U, V = Vector(origin), Vector(u).normalized(), Vector(v).normalized()
    N = U.cross(V).normalized()
    m = Mesh()
    front = [m.vert(o + U * a + V * b + N * (thickness / 2)) for a, b in outline]
    back = [m.vert(o + U * a + V * b - N * (thickness / 2)) for a, b in outline]
    n = len(outline)
    # UVs in metres (planar on the faces, perimeter x thickness round the edge): every face gets a proper tangent frame.
    m.face(front, mat, uv=[(a, b) for a, b in outline], smooth=False)
    m.face(back[::-1], mat, uv=[(a, b) for a, b in reversed(outline)], smooth=False)
    run = 0.0
    for i in range(n):
        j = (i + 1) % n
        step = math.hypot(outline[j][0] - outline[i][0], outline[j][1] - outline[i][1])
        m.face((front[i], back[i], back[j], front[j]), mat,
               uv=((run, thickness), (run, 0.0), (run + step, 0.0), (run + step, thickness)), smooth=False)
        run += step
    return m.orient_closed()


def rounded_outline(points, radius, segs=4):
    """Rounds each corner of a 2D polygon with an arc of `radius` (clamped to half the shorter edge)."""
    out = []
    n = len(points)
    for i in range(n):
        p0, p1, p2 = Vector(points[i - 1]), Vector(points[i]), Vector(points[(i + 1) % n])
        a, b = (p0 - p1), (p2 - p1)
        la, lb = a.length, b.length
        r = min(radius, 0.45 * la, 0.45 * lb)
        if r < 1e-5:
            out.append((p1.x, p1.y))
            continue
        a.normalize()
        b.normalize()
        s, e = p1 + a * r, p1 + b * r
        for k in range(segs + 1):
            t = k / segs
            q = (1 - t) ** 2 * s + 2 * (1 - t) * t * p1 + t * t * e
            out.append((q.x, q.y))
    return out


def tube(points, ra, rb=None, sides=10, major=(0, 0, 1), mat="carbon", caps=True, per=1, taper=None):
    """A tube along a path; section an ellipse with half-axes ra (along `major`, projected) and rb. `taper(t)` scales
    the section along the path (t 0..1)."""
    rb = ra if rb is None else rb
    raw = spline(points, per) if per > 1 else [Vector(p) for p in points]
    pts = [raw[0]]
    for p in raw[1:]:
        if (p - pts[-1]).length > 1e-5:
            pts.append(p)
    m = Mesh()
    rings = []
    hint = Vector(major)
    for i, p in enumerate(pts):
        if i == 0:
            t = pts[1] - pts[0]
        elif i == len(pts) - 1:
            t = pts[-1] - pts[-2]
        else:
            t = pts[i + 1] - pts[i - 1]
        t.normalize()
        a = hint - t * hint.dot(t)
        if a.length < 1e-4:
            a = Vector((0, 1, 0)) - t * t.y
        a.normalize()
        b = t.cross(a)
        k = taper(i / (len(pts) - 1)) if taper else 1.0
        ring = []
        for j in range(sides):
            ph = 2 * math.pi * j / sides
            ring.append(m.vert(p + a * (ra * k * math.cos(ph)) + b * (rb * k * math.sin(ph))))
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(sides):
            j2 = (j + 1) % sides
            m.face((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]), mat,
                   uv=((j / sides, i), ((j + 1) / sides, i), ((j + 1) / sides, i + 1), (j / sides, i + 1)))
    if caps:
        m.face(rings[0], mat, smooth=False)
        m.face(rings[-1][::-1], mat, smooth=False)
        return m.orient_closed()
    centres = pts
    return m.orient_outward(lambda c: c - min(centres, key=lambda q: (q - c).length))


def lathe(profile, segs, mat, outward=1, phase=0.0, uv_scale=1.0):
    """Revolves a profile of (r, a) points about the X axis: vertex (a, r cos phi, r sin phi). `mat` is a slot name or
    a function(i) -> slot for the span between profile points i and i+1. The surface faces the right-hand side of the
    profile walked in (a, r) (outward=1) or the left (outward=-1). UVs: u around (0..1), v along the profile (m)."""
    m = Mesh()
    rings = []
    for r, a in profile:
        ring = []
        for j in range(segs):
            ph = phase + 2 * math.pi * j / segs
            ring.append(m.vert((a, r * math.cos(ph), r * math.sin(ph))))
        rings.append(ring)
    vs = [0.0]
    for (r0, a0), (r1, a1) in zip(profile, profile[1:]):
        vs.append(vs[-1] + math.hypot(r1 - r0, a1 - a0) * uv_scale)
    for i in range(len(profile) - 1):
        (r0, a0), (r1, a1) = profile[i], profile[i + 1]
        if abs(r0) < 1e-9 and abs(r1) < 1e-9:
            continue
        da, dr = a1 - a0, r1 - r0
        n2 = (dr * outward, -da * outward)  # (a, r) components of the wanted normal
        slot = mat(i) if callable(mat) else mat
        for j in range(segs):
            j2 = (j + 1) % segs
            f = (rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j])
            uv = ((j / segs, vs[i]), ((j + 1) / segs, vs[i]), ((j + 1) / segs, vs[i + 1]), (j / segs, vs[i + 1]))
            if r0 < 1e-9:
                f, uv = f[1:], uv[1:]
            elif r1 < 1e-9:
                f, uv = f[:3], uv[:3]
            ph = phase + 2 * math.pi * (j + 0.5) / segs
            want = Vector((n2[0], n2[1] * math.cos(ph), n2[1] * math.sin(ph)))
            before = len(m.faces)
            m.face(f, slot, uv=uv)
            if len(m.faces) > before and m.face_normal(m.faces[-1]).dot(want) < 0:
                m.faces[-1] = m.faces[-1][::-1]
                m.uvs[-1] = m.uvs[-1][::-1]
    return m


def box(size, centre=(0, 0, 0), mat="carbon"):
    sx, sy, sz = (s / 2 for s in size)
    cx, cy, cz = centre
    m = Mesh()
    v = [m.vert((cx + x * sx, cy + y * sy, cz + z * sz)) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
    for f in ((0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)):
        m.face(tuple(v[i] for i in f), mat, uv=((0, 0), (1, 0), (1, 1), (0, 1)), smooth=False)
    return m.orient_closed()


def ellipsoid(radii, centre=(0, 0, 0), segs=24, rings=12, mat="carbon", lat=(-math.pi / 2, math.pi / 2)):
    rx, ry, rz = radii
    c = Vector(centre)
    m = Mesh()
    lat0, lat1 = lat
    grid = []
    for i in range(rings + 1):
        la = lat0 + (lat1 - lat0) * i / rings
        row = []
        for j in range(segs):
            lo = 2 * math.pi * j / segs
            row.append(m.vert(c + Vector((rx * math.cos(la) * math.cos(lo), ry * math.sin(la), rz * math.cos(la) * math.sin(lo)))))
        grid.append(row)
    for i in range(rings):
        for j in range(segs):
            j2 = (j + 1) % segs
            m.face((grid[i][j], grid[i][j2], grid[i + 1][j2], grid[i + 1][j]), mat,
                   uv=((j / segs, i / rings), ((j + 1) / segs, i / rings), ((j + 1) / segs, (i + 1) / rings),
                       (j / segs, (i + 1) / rings)))
    return m.orient_outward(lambda p: Vector(((p.x - c.x) / rx ** 2, (p.y - c.y) / ry ** 2, (p.z - c.z) / rz ** 2)))


def cylinder(radius, length, segs=16, mat="carbon", axis="x", centre=(0, 0, 0), caps=True):
    """A capped cylinder along an axis, centred on `centre`."""
    prof = [(0, -length / 2), (radius, -length / 2), (radius, length / 2), (0, length / 2)] if caps else \
        [(radius, -length / 2), (radius, length / 2)]
    m = lathe(prof, segs, mat, outward=-1)
    rot = {"x": Matrix.Identity(4), "y": Rz(math.pi / 2), "z": Ry(-math.pi / 2)}[axis]
    return m.transformed(T(centre) @ rot)


def with_mat(m, mat):
    return m.paint(mat)
