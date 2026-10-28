"""Shared helpers for the driver figures and helmets (Task 2; see docs/blender-models.md).

Adaptive parametric surfaces: a surface is a function pos(u, v) -> Vector (Godot frame) over a rectangle in (u, v).
`adaptive_points` samples the rectangle with a quadtree whose cell size follows a spacing function (fine round the
eyes, coarse on the back of the head), `surface` triangulates the samples with Blender's constrained Delaunay
triangulation, so paint regions (lips, helmet stripes) and creases (the eyelid margin) get exact edges instead of
jagged face-by-face colouring, maps the triangles onto the surface and carries analytic normals, which
`make_smooth` writes as custom split normals so the irregular triangles still shade smoothly.

Also: small maths (smoothstep, bump, monotone interpolation), deterministic noise and a polygon offset helper.
Everything is deterministic: no randomness without a fixed seed.
"""
import math

from mathutils import Vector, geometry, noise

from . import blend, frame
from .mesh import Mesh


# ---- small maths -------------------------------------------------------------------------------------------

def clamp(x, lo=0.0, hi=1.0):
    return lo if x < lo else hi if x > hi else x


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(e0, e1, x):
    if e0 == e1:
        return 0.0 if x < e0 else 1.0
    t = clamp((x - e0) / (e1 - e0))
    return t * t * (3 - 2 * t)


def bump(dx, dy, rx, ry, power=2.0):
    """A smooth bump: 1 at (0, 0), falling to 0 on the ellipse of radii (rx, ry), C1 at the rim."""
    q = (dx / rx) ** 2 + (dy / ry) ** 2
    return (1.0 - q) ** power if q < 1.0 else 0.0


class Curve:
    """Monotone cubic (PCHIP) interpolation through (x, y) points sorted by x; flat beyond the ends."""

    def __init__(self, points):
        pts = sorted(points)
        self.x = [p[0] for p in pts]
        self.y = [p[1] for p in pts]
        n = len(pts)
        h = [self.x[i + 1] - self.x[i] for i in range(n - 1)]
        d = [(self.y[i + 1] - self.y[i]) / h[i] for i in range(n - 1)]
        m = [0.0] * n
        if n == 2:
            m = [d[0], d[0]]
        else:
            m[0], m[-1] = d[0], d[-1]
            for i in range(1, n - 1):
                if d[i - 1] * d[i] <= 0:
                    m[i] = 0.0
                else:
                    w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
                    m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])
        self.h, self.m = h, m

    def __call__(self, x):
        xs = self.x
        if x <= xs[0]:
            return self.y[0]
        if x >= xs[-1]:
            return self.y[-1]
        lo, hi = 0, len(xs) - 1
        while hi - lo > 1:
            mid = (lo + hi) // 2
            if xs[mid] <= x:
                lo = mid
            else:
                hi = mid
        h = self.h[lo]
        t = (x - xs[lo]) / h
        t2, t3 = t * t, t * t * t
        return ((2 * t3 - 3 * t2 + 1) * self.y[lo] + (t3 - 2 * t2 + t) * h * self.m[lo]
                + (-2 * t3 + 3 * t2) * self.y[hi] + (t3 - t2) * h * self.m[hi])


def seg_dist(p, a, b):
    """Distance from point p to segment ab (Vectors), and the parameter t along it."""
    ab = b - a
    t = clamp((p - a).dot(ab) / max(ab.length_squared, 1e-12))
    return (p - (a + ab * t)).length, t


def polyline_dist(p, pts):
    best = (1e9, 0.0)
    total = 0.0
    lens = [(pts[i + 1] - pts[i]).length for i in range(len(pts) - 1)]
    run = 0.0
    for i in range(len(pts) - 1):
        d, t = seg_dist(p, pts[i], pts[i + 1])
        if d < best[0]:
            best = (d, run + t * lens[i])
        run += lens[i]
    total = run
    return best[0], (best[1] / total if total > 0 else 0.0)


# ---- deterministic noise ------------------------------------------------------------------------------------

def fbm(p, octaves=3, lacunarity=2.0, gain=0.5):
    """Perlin fBm in about [-1, 1] (mathutils.noise is deterministic: no seed state)."""
    amp, total, norm = 1.0, 0.0, 0.0
    q = Vector(p)
    for _ in range(octaves):
        total += amp * noise.noise(q, noise_basis="PERLIN_ORIGINAL")
        norm += amp
        q = q * lacunarity
        amp *= gain
    return total / norm


def cells(p):
    """Voronoi F1 distance (cellular bumps for curls and coils)."""
    d, _ = noise.voronoi(Vector(p), distance_metric="DISTANCE", exponent=2.5)
    return d[0]


def hash01(*ints):
    """A stable hash of integers into [0, 1) (no Python hash randomisation)."""
    h = 2166136261
    for i in ints:
        for byte in int(i).to_bytes(8, "little", signed=True):
            h = ((h ^ byte) * 16777619) & 0xFFFFFFFF
    return h / 4294967296.0


# ---- adaptive sampling and triangulation -----------------------------------------------------------------

def adaptive_points(u0, u1, v0, v1, spacing, base, max_depth=7):
    """Quadtree samples of [u0, u1] x [v0, v1]: a cell splits while it is larger than 1.25 x the smallest spacing
    found at its centre and corners. Returns a sorted list of (u, v) (the rectangle's corners always included)."""
    nu = max(1, math.ceil((u1 - u0) / base))
    nv = max(1, math.ceil((v1 - v0) / base))
    du, dv = (u1 - u0) / nu, (v1 - v0) / nv
    pts = set()

    def key(u, v):
        return (round(u, 7), round(v, 7))

    def visit(ua, ub, va, vb, depth):
        um, vm = 0.5 * (ua + ub), 0.5 * (va + vb)
        s = min(spacing(u, v) for u, v in ((um, vm), (ua, va), (ub, va), (ua, vb), (ub, vb),
                                           (um, va), (um, vb), (ua, vm), (ub, vm)))
        if depth < max_depth and max(ub - ua, vb - va) > 1.25 * s:
            visit(ua, um, va, vm, depth + 1)
            visit(um, ub, va, vm, depth + 1)
            visit(ua, um, vm, vb, depth + 1)
            visit(um, ub, vm, vb, depth + 1)
        else:
            for u, v in ((ua, va), (ub, va), (ua, vb), (ub, vb)):
                pts.add(key(u, v))

    for i in range(nu):
        for j in range(nv):
            visit(u0 + i * du, u0 + (i + 1) * du, v0 + j * dv, v0 + (j + 1) * dv, 0)
    return sorted(pts)


def _clip_rect(poly, u0, u1, v0, v1):
    """Sutherland-Hodgman clip of a polygon [(u, v)] to the rectangle."""
    def clip(pts, inside, inter):
        out = []
        for i in range(len(pts)):
            a, b = pts[i - 1], pts[i]
            ia, ib = inside(a), inside(b)
            if ib:
                if not ia:
                    out.append(inter(a, b))
                out.append(b)
            elif ia:
                out.append(inter(a, b))
        return out

    def ix(a, b, u):
        t = (u - a[0]) / (b[0] - a[0])
        return (u, a[1] + (b[1] - a[1]) * t)

    def iy(a, b, v):
        t = (v - a[1]) / (b[1] - a[1])
        return (a[0] + (b[0] - a[0]) * t, v)

    p = list(poly)
    for inside, inter in ((lambda q: q[0] >= u0, lambda a, b: ix(a, b, u0)),
                          (lambda q: q[0] <= u1, lambda a, b: ix(a, b, u1)),
                          (lambda q: q[1] >= v0, lambda a, b: iy(a, b, v0)),
                          (lambda q: q[1] <= v1, lambda a, b: iy(a, b, v1))):
        if not p:
            break
        p = clip(p, inside, inter)
    return p


def surface(pos, domain, points, base_slot, regions=(), creases=(), normal=None, h=1e-4, weld=1e-6,
            keep=None):
    """Triangulates `points` (u, v) over `domain` (u0, u1, v0, v1) and maps them through pos(u, v).

    regions: [(slot, polygon [(u, v)])], later ones on top; each polygon becomes exact edges and its triangles take
    its slot. creases: polylines [(u, v)] that must appear as edges (no slot change). normal(u, v) -> Vector gives
    the shading normal (default: finite differences of pos, step h). keep(u, v) -> bool can drop triangles (by
    centroid) to cut holes. Vertices that land on the same 3D point (a pole, a seam) are welded.
    Returns (Mesh, normals per vertex).
    """
    u0, u1, v0, v1 = domain
    verts = [Vector((u, v)) for u, v in points]
    faces_in = []
    edges_in = []
    rect = [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]
    base = len(verts)
    verts += [Vector(p) for p in rect]
    faces_in.append(list(range(base, base + 4)))
    region_slots = []
    for slot, poly in regions:
        poly = _clip_rect(poly, u0, u1, v0, v1)
        area = sum(poly[i - 1][0] * poly[i][1] - poly[i][0] * poly[i - 1][1] for i in range(len(poly)))
        if area < 0:
            poly = poly[::-1]   # the triangulator wants counter-clockwise faces
        if len(poly) < 3:
            region_slots.append(None)
            faces_in.append([])
            continue
        b = len(verts)
        verts += [Vector(p) for p in poly]
        faces_in.append(list(range(b, b + len(poly))))
        region_slots.append(slot)
    for line in creases:
        b = len(verts)
        verts += [Vector(p) for p in line]
        edges_in += [(b + i, b + i + 1) for i in range(len(line) - 1)]
    faces_arg = [f for f in faces_in if f]
    face_map = [i for i, f in enumerate(faces_in) if f]
    out_v, _, out_f, _, _, out_orig_f = geometry.delaunay_2d_cdt(verts, edges_in, faces_arg, 0, 1e-9, True)

    def nrm(u, v):
        if normal is not None:
            return Vector(normal(u, v)).normalized()
        du = pos(min(u + h, u1), v) - pos(max(u - h, u0), v)
        dv = pos(u, min(v + h, v1)) - pos(u, max(v - h, v0))
        n = du.cross(dv)
        return n.normalized() if n.length > 1e-14 else Vector((0, 1, 0))

    m = Mesh()
    normals = []
    index = {}
    remap = []
    for p in out_v:
        u, v = clamp(p.x, u0, u1), clamp(p.y, v0, v1)
        q = pos(u, v)
        k = (round(q.x / weld), round(q.y / weld), round(q.z / weld))
        if k not in index:
            index[k] = m.vert(q)
            normals.append(nrm(u, v))
        remap.append(index[k])
    for f, orig in zip(out_f, out_orig_f):
        c = sum((out_v[i] for i in f), Vector((0, 0))) / len(f)
        if keep is not None and not keep(c.x, c.y):
            continue
        if not orig:
            continue
        slot = base_slot
        top = -1
        for o in orig:
            fi = face_map[o] if o < len(face_map) else -1
            if fi >= 1 and fi - 1 > top and region_slots[fi - 1] is not None:
                top = fi - 1
        if top >= 0:
            slot = region_slots[top]
        idx = [remap[i] for i in f]
        dedup = []
        for i in idx:
            if i not in dedup:
                dedup.append(i)
        if len(dedup) < 3:
            continue
        # Wind so the geometric normal agrees with the shading normal.
        a, b, cc = (m.verts[i] for i in dedup[:3])
        gn = (b - a).cross(cc - a)
        if gn.length < 1e-16:
            continue
        sn = sum((normals[i] for i in dedup), Vector((0, 0, 0)))
        if gn.dot(sn) < 0:
            dedup.reverse()
        m.face(dedup, slot)
    return m, normals


def grid_surface(pos, us, vs, slot_fn, normal=None, h=1e-4, wrap_u=False, cap_v=False):
    """A plain quad grid over the given u and v lines (for parts with no paint regions), with analytic normals.
    slot_fn(u, v) -> slot per quad (at its centre). wrap_u closes the grid round u."""
    m = Mesh()
    normals = []
    u0, u1 = us[0], us[-1]
    v0, v1 = vs[0], vs[-1]

    def nrm(u, v):
        if normal is not None:
            return Vector(normal(u, v)).normalized()
        du = pos(u + h, v) - pos(u - h, v) if wrap_u else pos(min(u + h, u1), v) - pos(max(u - h, u0), v)
        dv = pos(u, min(v + h, v1)) - pos(u, max(v - h, v0))
        n = du.cross(dv)
        return n.normalized() if n.length > 1e-14 else Vector((0, 1, 0))

    cols = us[:-1] if wrap_u else us
    grid = []
    for v in vs:
        row = []
        for u in cols:
            row.append(m.vert(pos(u, v)))
            normals.append(nrm(u, v))
        grid.append(row)
    nc = len(cols)
    span = nc if wrap_u else nc - 1
    for j in range(len(vs) - 1):
        for i in range(span):
            i2 = (i + 1) % nc
            uc = 0.5 * (us[i] + us[i + 1])
            vc = 0.5 * (vs[j] + vs[j + 1])
            f = [grid[j][i], grid[j][i2], grid[j + 1][i2], grid[j + 1][i]]
            m.face(f, slot_fn(uc, vc),
                   uv=((us[i], vs[j]), (us[i + 1], vs[j]), (us[i + 1], vs[j + 1]), (us[i], vs[j + 1])))
    # Orient every face along the analytic normals.
    for k, f in enumerate(m.faces):
        sn = sum((normals[i] for i in f), Vector((0, 0, 0)))
        if m.face_normal(f).dot(sn) < 0:
            m.faces[k] = f[::-1]
            m.uvs[k] = m.uvs[k][::-1] if m.uvs[k] else None
    return m, normals


def weld_mesh(m, normals, dist=1e-6):
    """Merges coincident vertices (keeps the first normal) and drops faces that collapse."""
    out = Mesh()
    nout = []
    index = {}
    remap = []
    for v, n in zip(m.verts, normals):
        k = (round(v.x / dist), round(v.y / dist), round(v.z / dist))
        if k not in index:
            index[k] = out.vert(v)
            nout.append(n)
        remap.append(index[k])
    for f, s, uv, sm in zip(m.faces, m.mats, m.uvs, m.smooth):
        idx = []
        for i in f:
            if remap[i] not in idx:
                idx.append(remap[i])
        if len(idx) >= 3:
            out.face(idx, s, uv=None, smooth=sm)
    return out, nout


def concat(parts):
    """Concatenates [(Mesh, normals)] into one (Mesh, normals)."""
    m = Mesh()
    normals = []
    for pm, pn in parts:
        m.extend(pm)
        normals += list(pn)
    return m, normals


def transform(part, matrix):
    pm, pn = part
    rot = matrix.to_3x3()
    inv_t = rot.inverted_safe().transposed()
    return pm.transformed(matrix), [(inv_t @ n).normalized() for n in pn]


def make_smooth(part, name, collection=None):
    """A Blender object from (Mesh, per-vertex normals) with the normals as custom split normals."""
    mesh, normals = part
    obj = blend.make(mesh, name, collection)
    me = obj.data
    # No textures on these parts: drop the UV layer, so vertices are shared across faces (much smaller files).
    while me.uv_layers:
        me.uv_layers.remove(me.uv_layers[0])
    me.use_auto_smooth = True
    me.auto_smooth_angle = math.pi
    if len(normals) == len(me.vertices):
        me.normals_split_custom_set_from_vertices([tuple(frame.to_blender(n).normalized()) for n in normals])
    return obj


def flat_normals(mesh):
    """Per-vertex normals averaged from the faces (for small parts built without analytic normals)."""
    acc = [Vector((0, 0, 0)) for _ in mesh.verts]
    for f in mesh.faces:
        n = mesh.face_normal(f)
        for i in f:
            acc[i] += n
    return [a.normalized() if a.length > 1e-12 else Vector((0, 1, 0)) for a in acc]
