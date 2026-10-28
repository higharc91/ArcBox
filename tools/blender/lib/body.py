"""The driver's body for the podium figures (Task 2): race suit, gloves, boots, the champagne bottle, the team cap
and the nationality flag patch. Figure frame (Godot convention): metres, +Y up, feet on y = 0, facing +Z, +X to
the figure's left (so the right hand is on -X).

The figure stands in its rest pose holding the bottle upright in front of the chest with the right hand round its
body and the left hand under its base. Joints are where Godot pivots the parts: `Rig` computes shoulders, elbows,
wrists and the bottle frame from the height and build, and the arm parts are authored in place so each node's rest
rotation is the identity.
"""
import math

from mathutils import Matrix, Vector

from . import figure as fg
from .figure import Curve, clamp, lerp, smoothstep
from .mesh import Mesh, lathe

BUILD = {"slim": 0.95, "athletic": 1.0, "average": 1.05}

# The champagne bottle: (radius, height) up the outside, base to lip (the same profile as BottleMesh.cs).
BOTTLE_H = 0.48
BOTTLE_R = 0.064
BOTTLE_SHAPE = [
    (0.058, 0.0), (0.0635, 0.006), (BOTTLE_R, 0.03), (BOTTLE_R, 0.265), (0.061, 0.292), (0.052, 0.315),
    (0.040, 0.338), (0.029, 0.36), (0.0215, 0.385), (0.0200, 0.41), (0.0200, 0.458), (0.0232, 0.462),
    (0.0232, BOTTLE_H),
]
FOIL_FROM = 0.335


class Rig:
    """Joint positions of one figure in its rest pose."""

    def __init__(self, height, build):
        self.H = H = height
        self.k = BUILD.get(build, 1.0)
        k = self.k
        s = H / 1.78
        self.s = s
        self.head_scale = 1.065 + 0.3 * (H - 1.78)
        hs = self.head_scale
        self.eye = Vector((0.0, H - 0.123 * hs, 0.012))         # the head-local origin
        self.neck_base = H - 0.338 * hs
        self.shoulder_y = 0.815 * H
        sw = (0.192 if build != "slim" else 0.184) * s * (1 + 0.5 * (k - 1))
        self.shoulder_w = sw
        self.shoulder_r = Vector((-sw + 0.018, self.shoulder_y - 0.05 * s, -0.012))
        self.shoulder_l = Vector((sw - 0.018, self.shoulder_y - 0.05 * s, -0.012))
        self.upper = 0.183 * H
        self.fore = 0.148 * H
        self.hip_y = 0.515 * H
        self.waist_y = 0.6 * H
        self.crotch_y = 0.468 * H
        self.knee_y = 0.285 * H
        self.ankle_y = 0.048 * H
        self.hip_x = 0.088 * s * k
        # The bottle: base in front of the stomach, a little to the figure's right; upright.
        self.bottle_base = Vector((-0.045 * s, 0.505 * H, 0.225 * s * k ** 0.5))
        grip_y = self.bottle_base.y + 0.125
        # Right wrist: behind and to the right of the bottle, below the grip.
        self.wrist_r = Vector((self.bottle_base.x - 0.095, grip_y - 0.045, self.bottle_base.z - 0.045))
        # Left wrist: under the base, to the left.
        self.wrist_l = Vector((self.bottle_base.x + 0.06, self.bottle_base.y - 0.065, self.bottle_base.z - 0.02))
        self.elbow_r = ik_elbow(self.shoulder_r, self.wrist_r, self.upper, self.fore, Vector((-0.45, -0.6, -0.65)))
        self.elbow_l = ik_elbow(self.shoulder_l, self.wrist_l, self.upper, self.fore, Vector((0.55, -0.55, -0.6)))
        self.spine = Vector((0.0, self.waist_y, -0.01))


def ik_elbow(s, w, a, b, pole):
    """Two-bone IK: the elbow for shoulder s, wrist w, bone lengths a and b, bending towards `pole`."""
    d = w - s
    dist = clamp(d.length, abs(a - b) + 1e-4, a + b - 1e-4)
    dirv = d.normalized()
    cos_a = (a * a + dist * dist - b * b) / (2 * a * dist)
    sin_a = math.sqrt(max(0.0, 1 - cos_a * cos_a))
    p = Vector(pole) - dirv * Vector(pole).dot(dirv)
    p.normalize()
    return s + dirv * (a * cos_a) + p * (a * sin_a)


# ---- lofted limbs and the torso ---------------------------------------------------------------------------

def limb(path, radii, sides=14, slot_fn=None, caps=(True, True), flatten=None):
    """A smooth tube along `path` (Vectors) whose radius at each point is radii[i] (scalar or (ra, rb) ellipse with
    ra along `flatten`'s hint). slot_fn(i, j) picks a slot per quad. Returns (Mesh, normals)."""
    m = Mesh()
    normals = []
    rings = []
    n = len(path)
    hint = Vector((0, 0, 1)) if flatten is None else Vector(flatten)
    for i, p in enumerate(path):
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        a = hint - t * hint.dot(t)
        if a.length < 1e-5:
            a = Vector((1, 0, 0)) - t * t.x
        a.normalize()
        b = t.cross(a)
        r = radii[i]
        ra, rb = (r, r) if not isinstance(r, tuple) else r
        ring = []
        for j in range(sides):
            ph = 2 * math.pi * j / sides
            d = a * (ra * math.cos(ph)) + b * (rb * math.sin(ph))
            ring.append(m.vert(p + d))
            nn = a * (rb * math.cos(ph)) + b * (ra * math.sin(ph))
            normals.append(nn.normalized())
        rings.append(ring)
    for i in range(n - 1):
        for j in range(sides):
            j2 = (j + 1) % sides
            slot = slot_fn(i, j) if slot_fn else "suit_primary"
            m.face((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]), slot)
    for cap, i, sgn in ((caps[0], 0, -1), (caps[1], n - 1, 1)):
        if not cap:
            continue
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized() * sgn
        c = m.vert(path[i] + t * 0.3 * (radii[i] if not isinstance(radii[i], tuple) else min(radii[i])))
        normals.append(t)
        ring = rings[i]
        slot = slot_fn(min(i, n - 2), 0) if slot_fn else "suit_primary"
        for j in range(sides):
            m.face((ring[j], ring[(j + 1) % sides], c), slot)
    _orient(m, normals)
    return m, normals


def _orient(m, normals):
    for k, f in enumerate(m.faces):
        sn = sum((normals[i] for i in f), Vector((0, 0, 0)))
        if m.face_normal(f).dot(sn) < 0:
            m.faces[k] = f[::-1]
            m.uvs[k] = m.uvs[k][::-1] if m.uvs[k] else None


def smooth_path(points, per=3):
    from .mesh import spline
    return spline(points, per)


def torso(rig):
    """The suit's torso from the crotch to the collar: a loft of front/back superellipse halves, with secondary-colour
    side panels and shoulder yokes, a belt band and a stand-up collar. Returns (Mesh, normals)."""
    H, s, k = rig.H, rig.s, rig.k
    sw = rig.shoulder_w
    rows = [
        # y/H, half width, front depth, back depth, exponent
        (0.455, 0.150, 0.080, 0.090, 2.4),
        (0.475, 0.165, 0.092, 0.105, 2.5),
        (0.515, 0.172, 0.098, 0.115, 2.5),
        (0.560, 0.156, 0.104, 0.103, 2.5),
        (0.600, 0.146, 0.104, 0.094, 2.6),
        (0.650, 0.156, 0.110, 0.097, 2.6),
        (0.700, 0.172, 0.118, 0.104, 2.7),
        (0.745, 0.184, 0.117, 0.106, 2.8),
        (0.780, sw / s - 0.006, 0.104, 0.100, 3.0),
        (0.798, sw / s - 0.020, 0.094, 0.090, 2.9),
        (0.812, sw / s - 0.055, 0.080, 0.078, 2.6),
        (0.826, 0.098, 0.066, 0.068, 2.3),
        (0.838, 0.072, 0.058, 0.062, 2.1),
    ]
    ys = [r[0] * H for r in rows]
    cW = Curve([(ys[i], rows[i][1] * s * k * (1.04 if rows[i][0] < 0.78 else 1.0)) for i in range(len(rows))])
    cF = Curve([(ys[i], rows[i][2] * s * k ** 1.2) for i in range(len(rows))])
    cB = Curve([(ys[i], rows[i][3] * s * k) for i in range(len(rows))])
    cN = Curve([(ys[i], rows[i][4]) for i in range(len(rows))])
    belt0, belt1 = 0.585 * H, 0.612 * H

    def pos(phi, y):
        W, F, B, n = cW(y), cF(y), cB(y), cN(y)
        sn, cs = math.sin(phi), math.cos(phi)
        x = W * math.copysign(abs(sn) ** (2 / n), sn)
        z = (F if cs >= 0 else -B) * abs(cs) ** (2 / n)
        if belt0 < y < belt1:
            bulge = 0.007 * math.sin(math.pi * (y - belt0) / (belt1 - belt0)) ** 0.5
            r = math.hypot(x, z)
            if r > 1e-6:
                x += x / r * bulge
                z += z / r * bulge
        return Vector((x, y, z - 0.01))

    def slot(phi, y):
        if belt0 <= y <= belt1:
            return "belt"
        side = abs(abs(phi) - math.pi / 2)
        if y > 0.80 * H and 1.35 < abs(phi) < 1.8:
            return "suit_secondary"            # epaulettes
        if 0.47 * H < y < 0.77 * H and side < 0.52:
            return "suit_secondary"            # side panels from the hip to the armpit
        if 0.63 * H < y < 0.665 * H and abs(phi) < 1.1:
            return "suit_secondary"            # a chest band
        return "suit_primary"

    phis = sorted(set([-math.pi + 2 * math.pi * i / 40 for i in range(41)]
                      + [s_ * (math.pi / 2 + d) for s_ in (1, -1) for d in (-0.52, 0.52)] + [-1.1, 1.1]
                      + [s_ * a for s_ in (1, -1) for a in (1.35, 1.8)]))
    ylist = sorted(set([lerp(ys[0], ys[-1], i / 34) for i in range(35)] + [belt0, belt1, 0.77 * H, 0.80 * H, 0.47 * H, 0.63 * H, 0.665 * H]))
    m, nrm = fg.grid_surface(pos, phis, ylist, slot, wrap_u=True)
    # Close the bottom (inside the legs) with a fan.
    parts = [(m, nrm)]
    # Collar: a stand-up band round the neck.
    hs = rig.head_scale
    cy0 = max(0.83 * H, rig.neck_base + 0.012 * hs)
    cy1 = rig.neck_base + 0.082 * hs

    def cpos(phi, y):
        t = (y - cy0) / (cy1 - cy0)
        rx = (0.066 - 0.005 * t) * hs
        rz_f = (0.064 - 0.006 * t) * hs
        rz_b = (0.069 - 0.004 * t) * hs
        c = math.cos(phi)
        z = (rz_f if c >= 0 else rz_b) * c
        # Lower at the front, where the collar opens under the chin.
        dip = 0.018 * hs * max(0.0, c) ** 3 * t
        return Vector((rx * math.sin(phi), y - dip, z + 0.006))

    cphis = [-math.pi + 2 * math.pi * i / 32 for i in range(33)]
    cys = [cy0 - 0.012, cy0, lerp(cy0, cy1, 0.5), cy1]
    collar = fg.grid_surface(cpos, cphis, cys, lambda p, y: "suit_secondary" if y > lerp(cy0, cy1, 0.72) else
                             "suit_primary", wrap_u=True)
    # Thicken the collar's rim: an inner wall.
    def ipos(phi, y):
        p = cpos(phi, cy1)
        r = Vector((p.x, 0, p.z - 0.004))
        q = p - r.normalized() * 0.006
        return Vector((q.x, y, q.z))
    inner = fg.grid_surface(ipos, cphis, [cy1 - 0.03, cy1], lambda p, y: "suit_secondary", wrap_u=True)
    inner = (inner[0].flip(), [-n for n in inner[1]])
    parts += [collar, inner]
    return fg.concat(parts), pos


def surface_point(pos, x, y, front=True):
    """The point of a torso-like surface pos(phi, y) at lateral x and height y on its front (or back), and its
    outward normal."""
    lo, hi = (-math.pi / 2, math.pi / 2) if front else (math.pi / 2, 3 * math.pi / 2)
    for _ in range(50):
        mid = 0.5 * (lo + hi)
        px = pos(mid if front else mid, y).x
        if (px < x) == front:
            lo = mid
        else:
            hi = mid
    phi = 0.5 * (lo + hi)
    p = pos(phi, y)
    du = pos(phi + 1e-3, y) - pos(phi - 1e-3, y)
    dv = pos(phi, y + 1e-3) - pos(phi, y - 1e-3)
    n = du.cross(dv).normalized()
    if n.z < 0 and front:
        n = -n
    return p, n


def legs(rig):
    """Both suit legs (with an outer stripe) and race boots. Returns (Mesh, normals)."""
    H, s, k = rig.H, rig.s, rig.k
    parts = []
    for side in (1, -1):
        x0 = side * rig.hip_x
        pts = [Vector((x0, rig.hip_y + 0.02, 0.0)), Vector((x0 * 1.02, rig.crotch_y - 0.02, 0.005)),
               Vector((x0 * 1.0, 0.40 * H, 0.012)), Vector((x0 * 0.96, rig.knee_y, 0.018)),
               Vector((x0 * 0.92, 0.17 * H, 0.0)), Vector((x0 * 0.9, rig.ankle_y + 0.05, -0.012)),
               Vector((x0 * 0.9, rig.ankle_y, -0.02))]
        path = smooth_path(pts, 3)
        prof = Curve([(0.0, 0.104), (0.2, 0.097), (0.42, 0.080), (0.55, 0.070), (0.68, 0.071), (0.85, 0.060),
                      (1.0, 0.056)])
        radii = []
        for i, p in enumerate(path):
            t = i / (len(path) - 1)
            r = prof(t) * s * k
            radii.append((r * 1.02, r * 0.98))
        sides = 16

        def slot(i, j, side=side):
            # Outer stripe: the ring columns facing outwards (local frame a = +Z hint, so phase ~ +-pi/2 is X).
            ph = 2 * math.pi * (j + 0.5) / sides
            out = math.sin(ph) * side
            return "suit_secondary" if abs(math.cos(ph)) < 0.22 and out < 0 else "suit_primary"
        parts.append(limb(path, radii, sides, slot, caps=(False, True)))
        parts.append(boot(rig, side))
    return fg.concat(parts)


def boot(rig, side):
    """A low-profile race boot: a lofted shoe with a thin sole."""
    s = rig.s
    x = side * rig.hip_x * 0.9
    L = 0.27 * s
    m = Mesh()
    stations = []
    zs = [-0.06 * s, -0.04 * s, 0.0, 0.06 * s, 0.12 * s, 0.17 * s, L - 0.06 * s - 0.0]
    for i, z in enumerate(zs):
        t = i / (len(zs) - 1)
        w = (0.040 + 0.012 * math.sin(math.pi * min(1, t * 1.3))) * s
        h = (0.105 - 0.075 * t) * s if t < 0.95 else 0.03 * s
        stations.append((z, w, h))
    stations.append((L - 0.065 * s + 0.0, 0.035 * s, 0.028 * s))
    stations.append((L - 0.06 * s + 0.006, 0.02 * s, 0.02 * s))
    seg = 14
    rings = []
    normals = []
    for z, w, h in stations:
        ring = []
        for j in range(seg):
            ph = 2 * math.pi * j / seg
            cx, cy = math.sin(ph), -math.cos(ph)
            yy = max(0.0, h * 0.5 + h * 0.5 * cy * (1.0 if cy > 0 else 1.0))
            px = x + w * cx * (1.0 if cy > -0.6 else 1.0)
            ring.append(m.vert(Vector((px, max(yy, 0.0), z + 0.02 * s))))
            normals.append(Vector((cx, cy, 0)).normalized())
        rings.append(ring)
    for i in range(len(rings) - 1):
        for j in range(seg):
            j2 = (j + 1) % seg
            m.face((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]), "boots")
    for ring, d in ((rings[0], -1), (rings[-1], 1)):
        c = m.vert(sum((m.verts[i] for i in ring), Vector((0, 0, 0))) / len(ring))
        normals.append(Vector((0, 0, d)))
        for j in range(seg):
            m.face((ring[j], ring[(j + 1) % seg], c), "boots")
    m.orient_closed()
    return m, fg.flat_normals(m)


def arm(rig, side):
    """One arm in its rest pose: upper sleeve (shoulder cap included) and forearm sleeve with a cuff, as two
    (Mesh, normals) parts; the joint is where the forearm overlaps the upper arm."""
    s, k = rig.s, rig.k
    sh = rig.shoulder_r if side < 0 else rig.shoulder_l
    el = rig.elbow_r if side < 0 else rig.elbow_l
    wr = rig.wrist_r if side < 0 else rig.wrist_l
    d1 = (el - sh).normalized()
    up_path = [sh - d1 * 0.035, sh + d1 * 0.01] + \
        [sh + (el - sh) * t for t in (0.25, 0.5, 0.75, 1.0)] + [el + d1 * 0.03]
    up_r = [0.040, 0.057, 0.059, 0.058, 0.056, 0.054, 0.048]
    def stripe(i, j, n=14):
        ph = 2 * math.pi * (j + 0.5) / n
        return "suit_secondary" if abs(math.sin(ph) * side + 1) < 0.25 else "suit_primary"
    upper = limb(smooth_path(up_path, 2), _expand([r * s * k for r in up_r], 2, len(up_path)), 14,
                 stripe, caps=(True, True), flatten=(0, 0, 1))
    d2 = (wr - el).normalized()
    fo_path = [el - d2 * 0.025] + [el + (wr - el) * t for t in (0.2, 0.45, 0.7, 0.88, 1.0)]
    fo_r = [0.052 * s * k, 0.054 * s * k, 0.050 * s * k, 0.045 * s * k, 0.040 * s * k, 0.041 * s * k]
    n_seg = len(fo_path)
    fore = limb(smooth_path(fo_path, 2), _expand(fo_r, 2, n_seg), 14,
                lambda i, j: "suit_secondary" if i >= 2 * (n_seg - 1) - 2 else "suit_primary", caps=(True, True))
    return upper, fore


def _expand(vals, per, n):
    """Radii for a path densified by spline(per): interpolate the per-point values."""
    out = []
    for i in range(n - 1):
        for j in range(per):
            out.append(lerp(vals[i], vals[i + 1], j / per))
    out.append(vals[-1])
    return out


# ---- hands ----------------------------------------------------------------------------------------------------

def _finger(m, normals, pts, r, slot, sides=7):
    part = limb(smooth_path(pts, 2), [r * (1 - 0.18 * i / (2 * len(pts))) for i in range(2 * (len(pts) - 1) + 1)],
                sides, lambda i, j: slot, caps=(False, True))
    base = len(m.verts)
    m.extend(part[0])
    normals += part[1]


def right_hand(rig):
    """The right glove gripping the bottle's body: palm on its right side, four fingers wrapped round the front,
    the thumb round the back. Returns (Mesh, normals)."""
    b = rig.bottle_base
    s = rig.s
    m = Mesh()
    normals = []
    R = BOTTLE_R + 0.011
    grip_y = b.y + 0.125
    # Palm: a rounded block against the bottle (pointing up the bottle), from the wrist.
    palm_c = Vector((b.x - R - 0.006, grip_y + 0.005, b.z - 0.012))
    wr = rig.wrist_r
    palm = limb([wr + Vector((0.0, 0.0, 0.0)), wr.lerp(palm_c, 0.5), palm_c, palm_c + Vector((0.006, 0.04, 0.012))],
                [(0.03, 0.022), (0.036, 0.02), (0.04, 0.019), (0.038, 0.017)], 12, lambda i, j: "gloves",
                caps=(True, True), flatten=(1, 0, 0))
    m.extend(palm[0])
    normals += palm[1]
    # Four fingers round the front (angles round the bottle axis measured from -X towards +Z then +X).
    for f in range(4):
        y = grip_y + 0.042 - f * 0.021
        r = 0.0095 - 0.0008 * f
        length = (0.95, 1.08, 1.02, 0.86)[f]
        pts = []
        for i in range(6):
            a = math.radians(-8 + i * 19 * length)
            rr = R + 0.002 - 0.001 * i
            pts.append(Vector((b.x - rr * math.cos(a), y - 0.002 * i, b.z + rr * math.sin(a))))
        _finger(m, normals, pts, r, "gloves")
    # Thumb: from the palm's back edge round the back of the bottle.
    pts = []
    for i in range(5):
        a = math.radians(-35 - i * 17)
        rr = R + 0.002
        pts.append(Vector((b.x - rr * math.cos(a), grip_y + 0.03 + 0.008 * i, b.z + rr * math.sin(a))))
    _finger(m, normals, pts, 0.0115, "gloves")
    _orient(m, normals)
    return m, normals


def left_hand(rig):
    """The left glove cupped under the bottle's base, fingers curled up its left side."""
    b = rig.bottle_base
    m = Mesh()
    normals = []
    wr = rig.wrist_l
    palm_c = Vector((b.x + 0.012, b.y - 0.022, b.z + 0.0))
    palm = limb([wr, wr.lerp(palm_c, 0.5), palm_c, palm_c + Vector((-0.04, -0.002, 0.012))],
                [(0.03, 0.022), (0.036, 0.019), (0.042, 0.018), (0.036, 0.016)], 12, lambda i, j: "gloves",
                caps=(True, True), flatten=(0, 1, 0))
    m.extend(palm[0])
    normals += palm[1]
    for f in range(4):
        z = b.z + 0.03 - f * 0.02
        pts = []
        for i in range(5):
            t = i / 4
            x = b.x - 0.035 - 0.035 * t
            y = b.y - 0.018 + 0.03 * t * t
            pts.append(Vector((x, y, z)))
        _finger(m, normals, pts, 0.009, "gloves")
    pts = [Vector((b.x + 0.04, b.y - 0.01, b.z + 0.03)), Vector((b.x + 0.06, b.y + 0.02, b.z + 0.035)),
           Vector((b.x + 0.068, b.y + 0.045, b.z + 0.03))]
    _finger(m, normals, pts, 0.011, "gloves")
    _orient(m, normals)
    return m, normals


# ---- the bottle ----------------------------------------------------------------------------------------------

def bottle():
    """The champagne bottle in its own frame (base at the origin, +Y up): glass, a cream label with a red band, and
    the foil (team colour in Godot) from the shoulder over the cork. Returns (Mesh, normals)."""
    def r_at(y):
        for (r0, y0), (r1, y1) in zip(BOTTLE_SHAPE, BOTTLE_SHAPE[1:]):
            if y0 <= y <= y1:
                return lerp(r0, r1, (y - y0) / max(1e-6, y1 - y0))
        return BOTTLE_SHAPE[-1][0]
    ys = sorted(set([y for _, y in BOTTLE_SHAPE] + [0.12, 0.19, 0.2, 0.215, FOIL_FROM]))
    prof = [(0.0, 0.0)] + [(r_at(y), y) for y in ys] + [(0.0, BOTTLE_H)]

    def slot(i):
        y0, y1 = prof[i][1], prof[i + 1][1]
        ym = 0.5 * (y0 + y1)
        if ym >= FOIL_FROM or (y0 >= BOTTLE_H - 1e-6 and y1 >= BOTTLE_H - 1e-6):
            return "bottle_foil"
        if 0.19 <= ym <= 0.2:
            return "bottle_band"
        if 0.12 <= ym <= 0.215:
            return "bottle_label"
        return "bottle_glass"
    # lathe() revolves (r, a) about X; build along X then turn X into +Y.
    m = lathe([(r, y) for r, y in prof], 28, slot, outward=-1)
    m = m.transformed(Matrix.Rotation(math.pi / 2, 4, "Z"))
    m.orient_closed()
    nrm = []
    for v in m.verts:
        r = math.hypot(v.x, v.z)
        nrm.append(Vector((v.x, 0.0, v.z)).normalized() if r > 1e-5 else Vector((0, 1 if v.y > 0.2 else -1, 0)))
    # Slope-aware normals on the shoulder: use the face average there.
    fl = fg.flat_normals(m)
    for i, v in enumerate(m.verts):
        if 0.27 < v.y < 0.47 or v.y < 0.01 or v.y > BOTTLE_H - 0.001:
            nrm[i] = fl[i]
    return m, nrm


# ---- cap and flag ------------------------------------------------------------------------------------------

def flag_patch(code, centre, normal, up, width=0.062):
    """A nationality flag (simplified, no emblems or text) as a slightly raised patch. Faces use the slots
    flag_1..flag_4 (colours set per driver). Returns (Mesh, normals)."""
    from .flags import FLAGS
    spec = FLAGS[code]
    n = Vector(normal).normalized()
    u = Vector(up)
    u = (u - n * u.dot(n)).normalized()
    r = u.cross(n)   # to the right as you look at the patch
    h = width * 2 / 3
    m = Mesh()
    lift = 0.0
    for layer, (slot, polys) in enumerate(spec["layers"]):
        for poly in polys:
            idx = []
            for px, py in poly:
                p = Vector(centre) + (-r) * ((px - 0.5) * width) + u * ((0.5 - py) * h) + n * (0.0015 + 0.0004 * layer)
                idx.append(m.vert(p))
            m.face(idx, slot, smooth=False)
    for k, f in enumerate(m.faces):
        if m.face_normal(f).dot(n) < 0:
            m.faces[k] = f[::-1]
    return m, [n] * len(m.verts)
