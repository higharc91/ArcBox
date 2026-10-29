"""Hair and facial hair for the driver figures (Task 2), in the head-local frame of lib.head.

Hair is a shell over the scalp: the skin's (u, y) domain cut by a hairline polygon, pushed out along the skin normal
by a style thickness T(phi, y) that tapers to nothing at the hairline, with a deterministic surface texture (combed
clumps for straight hair, rounded clusters for curls, tight cells for coils). Locs are tubes laid along the scalp
and falling down the back. Under a cap only the part below the cap band shows, so the shell is split at the band:
`hair` (always drawn) and `hair_top` (drawn when the cap is off).

Styles: buzz, short_crop, textured_crop, swept, longer_top, curly, coiled, locs. Lengths: short, medium, long.
Facial hair: clean, light_stubble, stubble (painted on the skin), short_beard, full_beard, moustache, goatee (shells).
"""
import math

from mathutils import Vector

from . import figure as fg
from .figure import clamp, smoothstep, bump
from .head import U, Y_TOP
from .mesh import Mesh

LENGTH = {"short": 0.75, "medium": 1.0, "long": 1.35}


def hairline(head, style, fringe=0.0, recession=0.0, nape=0.0):
    """The hair region in the (u, y) domain: a closed polygon (front hairline, temples, round the ears, the nape)."""
    kl = head.kl
    y_front = 0.068 - 0.012 * fringe + 0.006 * recession
    temple = 0.060 - 0.008 * fringe + 0.010 * recession
    left = [
        (0.0, y_front), (0.22, y_front - 0.001), (0.42, temple + 0.001), (0.62, temple - 0.002 - 0.004 * recession),
        (0.82, 0.050 - 0.006 * fringe), (1.02, 0.040), (1.20, 0.026), (1.30, 0.004), (1.36, -0.010 * kl),
        (1.44, -0.012 * kl), (1.50, 0.008), (1.56, 0.030), (1.66, 0.040), (1.80, 0.036), (1.90, 0.012),
        (1.96, -0.030), (2.10, -0.062 - 0.02 * nape), (2.45, -0.080 - 0.035 * nape),
        (2.85, -0.088 - 0.045 * nape), (math.pi, -0.090 - 0.048 * nape),
    ]
    if style == "buzz":
        left = [(p, y + 0.004 if y > 0.05 else y) for p, y in left]
    pts = [(p * U, y) for p, y in left]
    right = [(-u, y) for u, y in pts[::-1]]
    return pts + [(math.pi * U, Y_TOP + 0.01), (-math.pi * U, Y_TOP + 0.01)] + right[:-1]


def _dist_poly(u, y, poly):
    best = 1e9
    p = Vector((u, y, 0))
    for i in range(len(poly)):
        a = Vector((poly[i - 1][0], poly[i - 1][1], 0))
        b = Vector((poly[i][0], poly[i][1], 0))
        if abs(a.y - Y_TOP - 0.01) < 1e-6 and abs(b.y - Y_TOP - 0.01) < 1e-6:
            continue   # the domain's top edge is not a hairline
        d, _ = fg.seg_dist(p, a, b)
        best = min(best, d)
    return best


def _inside(u, y, poly):
    c = False
    for i in range(len(poly)):
        x1, y1 = poly[i - 1]
        x2, y2 = poly[i]
        if (y1 > y) != (y2 > y) and u < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            c = not c
    return c


class Style:
    """Thickness and texture of one hairstyle over the head."""

    def __init__(self, traits):
        h = traits.get("hair", {})
        sh = h.get("shape", {})
        self.style = _val(h.get("style"), "short_crop")
        self.texture = _val(h.get("texture"), "straight")
        self.length = LENGTH.get(_val(h.get("length"), "medium"), 1.0)
        self.fringe = float(sh.get("fringe", 0.0))        # 0 .. 1: hair brought down over the forehead
        self.sweep = float(sh.get("sweep", 0.0))          # -1 flat .. +1 swept up and back at the front
        self.volume = float(sh.get("volume", 0.0))        # -1 .. +1
        self.recession = float(sh.get("recession", 0.0))  # 0 .. 1: a receding hairline
        self.sides = float(sh.get("sides", 0.0))          # -1 faded .. +1 full sides
        self.part = float(sh.get("part", 0.0))            # side parting: -1 on the right, +1 on the left, 0 none
        self.tied = bool(sh.get("tied", False))           # locs gathered at the back of the head
        self.nape = float(sh.get("nape", 0.0))            # 0 .. 1: longer hair at the back (down the neck)

    def thickness(self, phi, y, p, nrm):
        """Hair depth (m) above the scalp at (phi, y); p is the scalp point, nrm its normal."""
        s, L = self.style, self.length
        top = smoothstep(0.035, 0.085, y) * (1 - smoothstep(1.7, 2.4, abs(phi)) * 0.3)
        side = 1 - top
        front = smoothstep(1.2, 0.2, abs(phi)) * smoothstep(0.04, 0.075, y)
        crown = smoothstep(1.6, 2.6, abs(phi)) * smoothstep(0.06, 0.1, y)
        sides_k = 1 + 0.35 * self.sides
        vol = 1 + 0.3 * self.volume
        if s == "buzz":
            t = 0.0028
        elif s == "short_crop":
            t = (0.004 * sides_k * side + (0.0085 * L) * top) * vol
        elif s == "textured_crop":
            t = (0.0042 * sides_k * side + (0.0115 * L + 0.003 * front) * top) * vol
        elif s == "swept":
            t = (0.005 * sides_k * side + (0.0095 * L + (0.0045 + 0.0045 * self.sweep) * front - 0.002 * crown) * top) * vol
        elif s == "longer_top":
            t = (0.007 * sides_k * side + (0.016 * L + 0.005 * front) * top) * vol
        elif s == "curly":
            t = (0.012 * sides_k * side * L + (0.026 * L + 0.006 * front) * top) * vol
        elif s == "coiled":
            t = (0.010 * sides_k * side * L + (0.020 * L) * top) * vol
        elif s == "locs":
            t = 0.0045
        else:
            t = 0.006
        # The nape and round the ears stay short (unless the hair is worn longer at the back).
        t *= 1 - 0.45 * smoothstep(0.0, -0.06, y) * (1 - self.nape)
        if self.nape:
            t += 0.007 * self.nape * smoothstep(1.8, 2.6, abs(phi)) * smoothstep(0.02, -0.04, y)
        if self.part and s not in ("buzz", "locs"):
            side = 1 if self.part > 0 else -1
            php = side * 0.55
            # A groove along the parting; the hair heaps up on the far side of it.
            along = smoothstep(0.055, 0.075, y) * smoothstep(2.4, 1.6, abs(phi))
            t *= 1 - 0.7 * along * fg.bump(phi - php, 0, 0.07, 1)
            t *= 1 + 0.25 * abs(self.part) * along * smoothstep(php, php - side * 0.8, phi) * (1 if side > 0 else 1)
        return t

    def texture_offset(self, p, nrm):
        """Small displacement (m) along the normal that gives the shell its strands, curls or coils."""
        s = self.style
        if s == "buzz":
            return 0.0003 * fg.fbm(p * 900, 2)
        if s in ("curly",) or self.texture == "curly":
            c = fg.cells(p * 95)
            return 0.0042 * (0.55 - c) + 0.0012 * fg.fbm(p * 260, 2)
        if s == "coiled" or self.texture == "coily":
            c = fg.cells(p * 150)
            return 0.0030 * (0.55 - c) + 0.0008 * fg.fbm(p * 400, 2)
        if s == "locs":
            return 0.0008 * fg.fbm(p * 300, 2)
        # Straight / wavy: combed clumps that run back on the top and down the sides.
        up = clamp(nrm.y)
        k_across = 85 if self.texture == "straight" else 65
        a = fg.fbm(Vector((p.x * k_across, p.y * 18, p.z * 18)), 3)
        b = fg.fbm(Vector((p.x * 18, p.y * 18, p.z * k_across)), 3)
        amp = 0.0032 if s in ("textured_crop", "longer_top") else 0.0024 if s == "swept" else 0.0018
        if self.texture == "wavy":
            amp *= 1.3
        return amp * (up * a + (1 - up) * b)


def _val(v, default):
    if isinstance(v, dict):
        return v.get("value", default)
    return v if v is not None else default


def cap_band(head, phi):
    """The cap's lower edge height at phi (front above the brows, over the ears, down to the occiput)."""
    a = abs(phi)
    return 0.050 - 0.016 * smoothstep(0.4, 1.7, a) - 0.010 * smoothstep(1.7, 2.9, a)


def shell(head, style, split=None):
    """The hair shell as [(name, (Mesh, normals))]: 'hair' below the cap band and 'hair_top' above it (split=True),
    or one 'hair' part (split=False)."""
    poly = hairline(head, style.style, style.fringe, style.recession, style.nape)
    domain = (-math.pi * U, math.pi * U, -0.1 - 0.05 * style.nape, Y_TOP)

    def spacing(u, y):
        phi = u / U
        return 0.0065 if abs(phi) > 2.2 or y < -0.02 else 0.0052

    def T(u, y):
        phi = u / U
        p = head.pos(u, y)
        n = head.normal(u, y)
        d = _dist_poly(u, y, poly)
        e = smoothstep(0.0, 0.007, d)
        t = style.thickness(phi, y, p, n)
        return p, n, t * e + (style.texture_offset(p, n) * e if t > 0.0035 else 0.0) - 0.0005 * (1 - e)

    def pos(u, y):
        p, n, t = T(u, y)
        return p + n * t

    def normal(u, y, h=4e-4):
        du = pos(u + h, y) - pos(u - h, y)
        dv = pos(u, min(y + h, Y_TOP)) - pos(u, y - h)
        n = du.cross(dv)
        if n.length < 1e-14:
            return Vector((0, 1, 0))
        n.normalize()
        q = pos(u, y)
        return n if n.dot(Vector((q.x, q.y - 0.02, q.z))) > 0 else -n

    pts = fg.adaptive_points(*domain, spacing, 0.012)
    pts = [(u, y) for u, y in pts if _inside(u, y, poly) or _dist_poly(u, y, poly) < 0.006]
    parts = []
    if split:
        def band_y(u):
            return cap_band(head, u / U)
        below = lambda u, y: _inside(u, y, poly) and y < band_y(u)
        above = lambda u, y: _inside(u, y, poly) and y >= band_y(u)
        band = [(u0 * U, cap_band(head, u0)) for u0 in [-math.pi + 2 * math.pi * i / 72 for i in range(73)]]
        regions = [("hair", poly)]
        parts.append(("hair", fg.surface(pos, domain, pts, "hair", regions=regions, creases=[band], normal=normal,
                                         keep=below)))
        parts.append(("hair_top", fg.surface(pos, domain, pts, "hair", regions=regions, creases=[band],
                                             normal=normal, keep=above)))
    else:
        parts.append(("hair", fg.surface(pos, domain, pts, "hair", regions=[("hair", poly)], normal=normal,
                                         keep=lambda u, y: _inside(u, y, poly))))
    return parts, T


def max_thickness_at(head, style, phi, y):
    """Hair depth at (phi, y) with its texture, for fitting a cap over it."""
    u = phi * U
    p = head.pos(u, y)
    n = head.normal(u, y)
    t = style.thickness(phi, y, p, n)
    return t + (0.004 if style.style in ("curly", "coiled") else 0.0025)


# ---- locs --------------------------------------------------------------------------------------------------

def locs(head, style, back_y=-0.25, back_z=-0.115, count=34):
    """Locs: tubes that start in rows on the scalp, lie back over the crown and fall down the back. Returns
    (Mesh, normals). Their tops lie close to the scalp, so a cap covers them."""
    L = style.length
    m = Mesh()
    normals = []
    rows = [(0.072, 9), (0.095, 9), (0.112, 7), (0.05, 9)]
    k = 0
    for ry, n in rows:
        for i in range(n):
            if k >= count:
                break
            phi = -1.15 + 2.3 * (i + 0.5) / n + 0.08 * (fg.hash01(k, 3) - 0.5)
            if ry == 0.05:
                phi = (1.9 + 1.1 * (i + 0.5) / n) * (1 if i % 2 == 0 else -1)
            start = (phi * U, ry)
            if style.tied:
                path = _loc_path_tied(head, start, -0.13 * L, k, count)
            else:
                path = _loc_path(head, start, back_y * L, back_z, k)
            r = 0.0042 + 0.0008 * fg.hash01(k, 7)
            _tube(m, normals, path, r, 6, k)
            k += 1
    return m, normals


def _loc_path_tied(head, start, bottom, k, n_total):
    """From a scalp point, back over the scalp to a tie at the back of the head, then down as a bundle."""
    u, y = start
    phi = u / U
    pts = []
    tie_u, tie_y = (math.pi - 0.02) * U, 0.03
    for j in range(8):
        t = j / 7
        ph = phi + ((math.pi if phi >= 0 else -math.pi) - phi) * t ** 0.8
        yy = y + (tie_y - y) * t ** 1.3
        uu = ph * U
        lift = 0.0075 + 0.008 * smoothstep(0.6, 1.0, t)
        pts.append(head.pos(uu, yy) + head.normal(uu, yy) * lift)
    tie = pts[-1]
    a = 2 * math.pi * fg.hash01(k, 11)
    rr = 0.006 + 0.012 * fg.hash01(k, 13)
    for j in range(1, 8):
        t = j / 7
        yy = tie.y + (bottom - tie.y) * t * (0.75 + 0.25 * fg.hash01(k, 17))
        off = rr * smoothstep(0, 0.4, t)
        pts.append(Vector((tie.x + math.cos(a) * off, yy, tie.z - 0.012 * t - 0.01 * smoothstep(0, 0.3, t)
                           + math.sin(a) * off)))
    return fg_spline(pts)


def _loc_path(head, start, bottom, back_z, k):
    """From a scalp point, back over the head close to the scalp, then down behind the neck to `bottom`."""
    u, y = start
    phi = u / U
    pts = []
    # Over the scalp towards the back (phi drifts to the back of the head).
    side = 1 if phi >= 0 else -1
    target_phi = side * (2.95 - 0.25 * abs(math.sin(phi)) + 0.12 * fg.hash01(k, 5))
    for j in range(7):
        t = j / 6
        ph = phi + (target_phi - phi) * t
        yy = y + (0.02 - y) * t * t
        uu = ph * U
        p = head.pos(uu, yy) + head.normal(uu, yy) * 0.0075
        pts.append(p)
    # Then down the back, fanning a little.
    last = pts[-1]
    spread = 0.5 * last.x
    for j in range(1, 9):
        t = j / 8
        yy = last.y + (bottom - last.y) * t
        zz = last.z + (back_z - last.z) * smoothstep(0, 0.6, t) - 0.01 * t
        xx = last.x + spread * t * 0.6 + 0.004 * math.sin(7 * t + k)
        pts.append(Vector((xx, yy, zz)))
    return fg_spline(pts)


def fg_spline(pts, per=2):
    from .mesh import spline
    return spline(pts, per)


def _tube(m, normals, path, r, sides, seed):
    """A loc: a tube with gentle twisted lumps, capped at the end."""
    rings = []
    n = len(path)
    prev = None
    for i, p in enumerate(path):
        t = (path[min(i + 1, n - 1)] - path[max(i - 1, 0)]).normalized()
        a = Vector((0, 0, 1)) if prev is None else prev
        a = (a - t * a.dot(t))
        if a.length < 1e-6:
            a = Vector((1, 0, 0)) - t * t.x
        a.normalize()
        prev = a
        b = t.cross(a)
        taper = 1.0 if i < n - 2 else 0.7
        ring = []
        for j in range(sides):
            ph = 2 * math.pi * j / sides + 0.6 * i
            lump = 1 + 0.12 * math.sin(i * 2.1 + seed)
            d = a * math.cos(ph) + b * math.sin(ph)
            ring.append(m.vert(p + d * r * lump * taper))
            normals.append(d)
        rings.append(ring)
    for i in range(n - 1):
        for j in range(sides):
            j2 = (j + 1) % sides
            m.face((rings[i][j], rings[i][j2], rings[i + 1][j2], rings[i + 1][j]), "hair")
    end = m.vert(path[-1] + (path[-1] - path[-2]).normalized() * r * 0.6)
    normals.append((path[-1] - path[-2]).normalized())
    for j in range(sides):
        m.face((rings[-1][j], rings[-1][(j + 1) % sides], end), "hair")
    # Orient outwards.
    for kf in range(len(m.faces) - (n - 1) * sides - sides, len(m.faces)):
        f = m.faces[kf]
        sn = sum((normals[i] for i in f), Vector((0, 0, 0)))
        if m.face_normal(f).dot(sn) < 0:
            m.faces[kf] = f[::-1]


# ---- facial hair -------------------------------------------------------------------------------------------

def beard(head, style_name, density=1.0):
    """A beard, moustache or goatee shell over the skin (None for stubble/clean). Returns (Mesh, normals)."""
    if style_name not in ("short_beard", "full_beard", "moustache", "goatee"):
        return None
    polys = head.facial_hair_regions("stubble" if style_name in ("short_beard", "full_beard") else style_name)
    depth = {"short_beard": 0.0032, "full_beard": 0.0075, "moustache": 0.0022, "goatee": 0.0026}[style_name] * density
    parts = []
    for poly in polys:
        us = [p[0] for p in poly]
        ys = [p[1] for p in poly]
        domain = (min(us) - 0.002, max(us) + 0.002, min(ys) - 0.002, max(ys) + 0.002)

        def thick(u, y):
            d = _dist_poly_closed(u, y, poly)
            e = smoothstep(0.0, 0.006, d)
            # Thinner on the cheeks, fullest on the chin and jaw.
            chin = 1 + 0.5 * smoothstep(-0.07, -0.11, y) if style_name == "full_beard" else 1.0
            p = head.pos(u, y)
            tex = 0.0007 * fg.fbm(p * 420, 2)
            return depth * chin * e + tex * e - 0.0004 * (1 - e)

        def pos(u, y):
            return head.pos(u, y) + head.normal(u, y) * thick(u, y)

        pts = fg.adaptive_points(*domain, lambda u, y: 0.0032, 0.01)
        part = fg.surface(pos, domain, pts, "beard", regions=[("beard", poly)],
                          normal=lambda u, y: head.normal(u, y), keep=lambda u, y: _inside(u, y, poly))
        parts.append(part)
    return fg.concat(parts)


def _dist_poly_closed(u, y, poly):
    best = 1e9
    p = Vector((u, y, 0))
    for i in range(len(poly)):
        a = Vector((poly[i - 1][0], poly[i - 1][1], 0))
        b = Vector((poly[i][0], poly[i][1], 0))
        d, _ = fg.seg_dist(p, a, b)
        best = min(best, d)
    return best


# ---- sculpted clumps -------------------------------------------------------------------------------------------

def clumps(head, style, T, split=True):
    """Sculpted locks on top of the shell that give each style its silhouette: a quiff swept up and back, a fringe
    pushed forward, hair combed away from a side parting, tousled volume, longer hair at the nape. Each lock is a
    tapered, flattened tube rooted in the shell and bent along the style's flow. Returns {"hair": part, "hair_top":
    part}: locks rooted under the cap band go with `hair` (they show under the cap), the rest with `hair_top`."""
    from .body import limb
    s = style.style
    if s in ("buzz", "locs", "curly", "coiled", "short_crop") and not style.part and not style.nape:
        return {}
    L = style.length
    poly = hairline(head, s, style.fringe, style.recession, style.nape)
    out = {"hair": [], "hair_top": []}
    n_phi, n_y = 26, 9
    for i in range(n_phi):
        for j in range(n_y):
            k = i * 31 + j
            phi = -2.9 + 5.8 * (i + 0.5 + 0.6 * (fg.hash01(k, 1) - 0.5)) / n_phi
            y = -0.02 + (Y_TOP - 0.01 + 0.02) * (j + 0.5 + 0.6 * (fg.hash01(k, 2) - 0.5)) / n_y
            u = phi * U
            if not _inside(u, y, poly) or _dist_poly(u, y, poly) < 0.01:
                continue
            top = smoothstep(0.03, 0.08, y)
            front = smoothstep(1.3, 0.3, abs(phi)) * top
            back = smoothstep(1.9, 2.6, abs(phi))
            # Which locks this style has.
            want = 0.0
            if s in ("textured_crop", "swept", "longer_top", "short_crop"):
                want = top
            if style.fringe > 0:
                want = max(want, front)
            if style.nape > 0:
                want = max(want, back * smoothstep(0.06, -0.02, y))
            if style.part:
                want = max(want, top * 0.8)
            if want < 0.5 or fg.hash01(k, 3) > 0.55 + 0.4 * want:
                continue
            p, n, t = T(u, y)
            base = p + n * (t * 0.55)
            # Flow: a tangent direction on the scalp.
            back_dir = Vector((0, 0, -1))
            down_dir = Vector((0, -1, 0))
            side_dir = Vector((1 if phi >= 0 else -1, 0, 0))
            flow = back_dir * top + down_dir * (1 - top)
            lift = 0.14 * L * (1 + 0.6 * style.volume)
            length = (0.028 + 0.014 * L) * (1 + 0.35 * style.volume)
            if style.sweep:
                flow = flow + Vector((0, 0.5 * style.sweep, -0.4 * style.sweep)) * front
                lift += 0.3 * max(0.0, style.sweep) * front
            if style.fringe > 0 and front > 0.3 and y > 0.06:
                flow = Vector((0.15 * side_dir.x, -0.55, 1.0)) * front + flow * (1 - front)
                lift *= 0.4
                length *= 1.1 + 0.5 * style.fringe
            if style.part:
                ps = 1 if style.part > 0 else -1
                away = Vector((-ps, 0, 0)) if (phi - ps * 0.55) * ps < 0 else Vector((ps, 0, 0))
                flow = flow * 0.5 + (away + Vector((0, -0.3, -0.25))) * top
            if style.nape > 0 and back > 0.5 and y < 0.06:
                flow = down_dir + back_dir * 0.25
                length *= 1.4 + 0.8 * style.nape
                lift = 0.05
            if s == "textured_crop" and not style.part:
                jit = Vector(((fg.hash01(k, 4) - 0.5), 0.6 * fg.hash01(k, 5), (fg.hash01(k, 6) - 0.5)))
                flow = flow + jit * 0.45
                lift += 0.08 * style.volume
            f = flow - n * flow.dot(n)
            if f.length < 1e-4:
                continue
            f.normalize()
            pts = []
            for st in range(6):
                q = st / 5
                bend = n * (length * (lift * math.sin(math.pi * q * 0.75) - 0.18 * q * q))
                pts.append(base + f * (length * q) + bend)
            w = 0.0105 + 0.004 * fg.hash01(k, 7)
            radii = [(w * (1 - 0.7 * (q / 5) ** 1.6), w * 0.4 * (1 - 0.65 * (q / 5))) for q in range(6)]
            side_hint = f.cross(n)
            part = limb(pts, radii, 5, lambda a, b: "hair", caps=(False, True), flatten=side_hint)
            key = "hair" if (not split or y < cap_band(head, phi) - 0.004) else "hair_top"
            out[key].append(part)
    return {k: fg.concat(v) for k, v in out.items() if v}
