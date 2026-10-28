"""The parametric head for the driver figures (Task 2): face, eyes, brows, ears, facial hair and hair.

Head-local frame (Godot convention): metres, +Y up, +Z forward (the face), +X to the head's left; origin on the
eye line, level with the ear canals (so y = 0 is the eye line and z = 0 the tragion plane).

The skin is a star-shaped surface over (phi, y): phi is the angle round the vertical axis (0 = front, +pi/2 = the
head's left), y the height. Each height has a section made of two superellipse halves (front and back) with their
own depth and exponent, interpolated over y (`Curve`); facial features are smooth displacements along the
section's outward normal (brow, nose, lips, chin, cheekbones, jaw ridge, nasolabial folds). The eye region is set
outright: lids that wrap a sphere round the eyeball, and an almond aperture whose rim is an exact edge loop.
Every parameter is a small number round 0 (the average adult man), usually -1..1, read from data/drivers.json.

The surfaces are triangulated adaptively (lib.figure): ~1.3 mm round the eyelids, ~2 mm on the nose and lips,
~3.5 mm on the face and coarse on the back of the head, which the hair and cap cover.
"""
import math

from mathutils import Matrix, Vector

from . import figure as fg
from .figure import Curve, bump, clamp, lerp, smoothstep
from .mesh import Mesh, spline

U = 0.08          # metres per radian of phi in the triangulation domain (keeps triangles near-isotropic)
Y_TOP = 0.123     # top of the skull above the eye line
Y_BOT = -0.215    # where the neck goes into the suit collar
EYE_R = 0.0118    # eyeball radius
GAIN = 2.0        # how strongly the face and nose parameters act (the data stays in the friendly -1..1 range)


def _p(d, key, default=0.0):
    v = d.get(key, default) if d else default
    return float(v) if isinstance(v, (int, float)) else default


class Head:
    """One driver's head. `traits` is the driver's entry in drivers.json (face, nose, eyes, lips, ears, brows ...)."""

    def __init__(self, traits):
        f = traits.get("face", {})
        n = traits.get("nose", {})
        e = traits.get("eyes", {})
        lp = traits.get("lips", {})
        ea = traits.get("ears", {})
        self.t = traits
        g = GAIN
        f = {k: v * g if isinstance(v, (int, float)) else v for k, v in f.items()}
        n = {k: v * g if isinstance(v, (int, float)) else v for k, v in n.items()}
        self.fw = _p(f, "width")
        self.fl = _p(f, "length")
        self.jw = _p(f, "jaw_width")
        self.js = _p(f, "jaw_square")
        self.jd = _p(f, "jaw_definition")
        self.cp = _p(f, "chin_projection")
        self.cw = _p(f, "chin_width")
        self.ch = _p(f, "chin_height")
        self.cleft = _p(f, "chin_cleft")
        self.cb = _p(f, "cheekbones")
        self.cf = _p(f, "cheek_fullness")
        self.br = _p(f, "brow_ridge")
        self.fs = _p(f, "forehead_slope")
        self.age = _p(f, "age_lines")
        self.nl = _p(n, "length")
        self.nw = _p(n, "width")
        self.np = _p(n, "projection")
        self.nb = _p(n, "bridge")          # -1 concave .. +1 a dorsal hump
        self.nbw = _p(n, "bridge_width")
        self.ntip = _p(n, "tip")           # -1 drooping .. +1 upturned
        self.nflare = _p(n, "nostril_flare")
        self.es = _p(e, "spacing")
        self.esz = _p(e, "size")
        self.eo = _p(e, "openness")
        self.etilt = _p(e, "tilt")
        self.edepth = _p(e, "depth")
        self.ehood = _p(e, "hooding")
        self.lfull = _p(lp, "fullness")
        self.lw = _p(lp, "width")
        self.smile = _p(lp, "smile", 0.3)
        self.ear_size = _p(ea, "size") * GAIN
        self.ear_out = _p(ea, "protrusion") * GAIN

        kl = 1.0 + 0.055 * self.fl
        self.kl = kl
        # Landmark heights (below the eye line scaled by the face length).
        self.y_root = 0.006
        self.y_tip = (-0.0405 - 0.0055 * self.nl) * kl
        self.y_sub = self.y_tip - (0.0115 - 0.002 * self.ntip) * kl
        self.y_sto = -0.0752 * kl
        self.y_chin = (-0.109 - 0.004 * self.ch) * kl
        self.y_menton = (-0.121 - 0.005 * self.ch) * kl
        self.mw = 0.0245 + 0.0022 * self.lw
        self.ex = 0.0315 + 0.0022 * self.es
        self.aw = 0.0136 + 0.0008 * self.esz
        self.ahu = 0.0047 + 0.0009 * self.eo + 0.0003 * self.esz
        self.ahl = 0.0035 + 0.0005 * self.eo
        self.z_ec = 0.0758 - 0.0022 * self.edepth

        fw, jw = self.fw, self.jw
        wj = 0.0058 * jw
        rows = [
            # y, W, F, B, nf, nb
            (0.018, 0.0760, 0.0985, 0.100, 2.45, 2.3),
            (0.000, 0.0752, 0.0960, 0.097, 2.55, 2.3),
            (-0.020, 0.0730 + 0.0015 * self.cb, 0.0948, 0.092, 2.5, 2.25),
            (-0.045, 0.0695, 0.0930, 0.084, 2.4, 2.2),
            (-0.065, 0.0665 + 0.4 * wj, 0.0910, 0.074, 2.45 + 0.15 * jw, 2.15),
            (-0.082, 0.0628 + wj, 0.0890, 0.066, 2.3 + 0.3 * jw + 0.15 * self.js, 2.1),
            (-0.096, 0.0600 + wj, 0.0862, 0.061, 2.1 + 0.3 * jw + 0.2 * self.js, 2.0),
            (-0.110, 0.0580 + 0.6 * wj, 0.0812 + 0.004 * self.cp, 0.060, 1.85 + 0.2 * jw + 0.15 * self.cw, 2.0),
            (-0.121, 0.0575, 0.0690 + 0.004 * self.cp, 0.060, 1.8 + 0.15 * self.cw, 2.0),
            (-0.133, 0.0530, 0.0500, 0.059, 2.0, 2.0),
            (-0.150, 0.0530, 0.0470, 0.059, 2.0, 2.0),
            (-0.185, 0.0560, 0.0480, 0.062, 2.0, 2.0),
            (-0.215, 0.0630, 0.0520, 0.066, 2.0, 2.0),
        ]
        k_face = 1.0 + 0.06 * fw
        ys, W, F, B, NF, NB = [], [], [], [], [], []
        for y, w, fr, b, nf, nb in rows:
            yy = y * kl if y < 0 else y
            if yy > -0.13:
                w *= k_face if y < 0.01 else 1.0 + 0.03 * fw
            ys.append(yy)
            W.append(w)
            F.append(fr)
            B.append(b)
            NF.append(nf)
            NB.append(nb)
        # The cranium: a dome from y = 0.032 to the top.
        yc = 0.032
        wc, fc, bc = 0.0762 * (1 + 0.03 * fw), 0.0975, 0.1005
        for t in (0.0, 0.3, 0.55, 0.75, 0.88, 0.95, 0.985, 1.0):
            y = yc + t * (Y_TOP - yc)
            s = math.sqrt(max(0.0, 1 - t * t))
            ys.append(y)
            W.append(wc * s)
            F.append(fc * s - 0.009 * self.fs * t * s)
            B.append(bc * s)
            NF.append(2.4 - 0.3 * t)
            NB.append(2.2 - 0.2 * t)
        order = sorted(range(len(ys)), key=lambda i: ys[i])
        self.cW = Curve([(ys[i], W[i]) for i in order])
        self.cF = Curve([(ys[i], F[i]) for i in order])
        self.cB = Curve([(ys[i], B[i]) for i in order])
        self.cNF = Curve([(ys[i], NF[i]) for i in order])
        self.cNB = Curve([(ys[i], NB[i]) for i in order])
        self._nose_curves()

    # ---- the base sections -----------------------------------------------------------------------------

    def section(self, y):
        return self.cW(y), self.cF(y), self.cB(y), self.cNF(y), self.cNB(y)

    def base(self, phi, y):
        W, F, B, nf, nb = self.section(y)
        s, c = math.sin(phi), math.cos(phi)
        if c >= 0:
            x = W * math.copysign(abs(s) ** (2 / nf), s)
            z = F * abs(c) ** (2 / nf)
        else:
            x = W * math.copysign(abs(s) ** (2 / nb), s)
            z = -B * abs(c) ** (2 / nb)
        return Vector((x, y, z))

    def phi_at(self, x, y):
        """phi of the base point with lateral position x on the front half at height y."""
        W, _, _, nf, _ = self.section(y)
        r = clamp(abs(x) / max(W, 1e-6), 0, 1)
        return math.copysign(math.asin(r ** (nf / 2)), x)

    def normal_h(self, phi, y):
        a = self.base(phi - 1e-4, y)
        b = self.base(phi + 1e-4, y)
        t = b - a
        n = Vector((t.z, 0.0, -t.x))
        if n.length < 1e-12:
            return Vector((0, 1, 0))
        n.normalize()
        p = self.base(phi, y)
        if n.dot(Vector((p.x, 0, p.z))) < 0:
            n = -n
        return n

    # ---- features -----------------------------------------------------------------------------------------

    def _nose_curves(self):
        kl = self.kl
        yr, yt, ys = self.y_root, self.y_tip, self.y_sub
        L = yr - yt
        pt = 0.0215 + 0.0045 * self.np
        hump = 0.0018 * self.nb
        self.nose_tip_proj = pt
        self.cP = Curve([
            (yr + 0.014, 0.0), (yr + 0.004, 0.0012), (yr - 0.002, 0.0028),
            (yr - 0.35 * L, 0.0035 + 0.38 * (pt - 0.0035) + hump),
            (yr - 0.62 * L, 0.0035 + 0.66 * (pt - 0.0035) + 0.6 * hump),
            (yt + 0.0075 * kl, pt * 0.94), (yt, pt), (yt - 0.0045 * kl, pt * 0.80),
            (ys + 0.0018, 0.0085), (ys, 0.0045), (ys - 0.004, 0.0),
        ])
        bw = 0.0085 + 0.0015 * self.nbw
        tw = 0.0105 + 0.0018 * self.nw
        self.alar = 0.0165 + 0.0022 * self.nw + 0.0012 * self.nflare
        self.cNW = Curve([
            (yr + 0.012, bw * 1.25), (yr, bw), (yr - 0.5 * L, bw * 0.97), (yt + 0.008, tw * 0.9), (yt, tw),
            (ys + 0.006, self.alar * 0.82), (ys, 0.011), (ys - 0.006, 0.010),
        ])

    def eye_centre(self, side):
        return Vector((side * self.ex, 0.0, self.z_ec))

    def aperture(self, side, t):
        """Upper and lower lid margins (y) at t in [-1 (inner corner), 1 (outer)], and their x."""
        x = side * (self.ex + self.aw * t)
        yc = 0.0012 * self.etilt * t - 0.0004 * (1 - t) * 0.5
        a = max(0.0, 1 - t * t)
        yu = yc + self.ahu * a ** 0.72 * (1 + 0.12 * (-t)) * (1 - 0.18 * self.ehood * max(0.0, t))
        yl = yc - self.ahl * a ** 0.95 * (1 + 0.1 * t)
        return x, yu, yl

    ROLL = 0.0011    # the lid margin rolls in over this distance inside the aperture

    def aperture_outline(self, side, n=28, inset=0.0):
        up, lo = [], []
        for i in range(n + 1):
            t = -1 + 2 * i / n
            t = math.copysign(abs(t) ** 0.8, t)
            x, yu, yl = self.aperture(side, t)
            if inset:
                if abs(t) > 0.93:
                    continue
                k = min(inset, 0.45 * (yu - yl))
                yu, yl = yu - k, yl + k
            up.append((x, yu))
            lo.append((x, yl))
        # A closed loop: along the upper margin, back along the lower one (corners shared).
        return up + lo[-2:0:-1]

    def in_aperture(self, x, y):
        side = 1 if x > 0 else -1
        t = (x * side - self.ex) / self.aw
        if abs(t) >= 1:
            return False
        _, yu, yl = self.aperture(side, t)
        return yl < y < yu

    def _eye_z(self, side, x, y, z_face, inside=None):
        """The skin depth near the eye on `side` at frontal (x, y), given the face depth there without the eye:
        the lids wrap a sphere round the eyeball, the orbit is a bowl pressed into the face, and the two meet in a
        soft crease (smooth max). Inside the aperture the margin rolls in behind the eyeball. `inside` overrides
        the aperture test (normals are taken on one side of the lid margin)."""
        c = self.eye_centre(side)
        dx, dy = x - c.x, y - c.y
        t = (x * side - self.ex) / self.aw
        tc = clamp(t, -1, 1)
        _, yu, yl = self.aperture(side, tc)
        if inside is None:
            inside = abs(t) < 1 and yl < y < yu
        r2 = dx * dx + dy * dy
        if inside:
            dist = max(0.0, min(yu - y, y - yl))
            R = EYE_R + 0.0015 - 0.0045 * smoothstep(0, self.ROLL, dist)
            return c.z + math.sqrt(max(0.0, R * R - r2)) if r2 < R * R else c.z
        if y >= yu:
            d = y - yu
            fold = 0.0022 - 0.0010 * self.ehood
            R = EYE_R + 0.0015 + fold * math.sin(math.pi * clamp(d / 0.008, 0, 1)) ** 0.8
        else:
            d = max(0.0, yl - y)
            R = EYE_R + 0.0015 + 0.0010 * smoothstep(0, 0.004, d)
        lid = c.z + math.sqrt(R * R - r2) if r2 < R * R else -1.0
        depth = 0.0145 + 0.0015 * self.edepth
        ry = 0.0175 if dy > 0 else 0.0205
        socket = z_face - depth * bump(dx, dy, 0.0235, ry, 1.4)
        z = _smax(lid, socket, 0.0016)
        # Right round the aperture the skin lies on the lid sphere (deep inner and outer corners).
        if abs(t) < 1:
            gap = (y - yu) if y >= yu else (yl - y)
        else:
            gap = math.hypot((abs(t) - 1) * self.aw, y - (yu if y > yu else yl if y < yl else y))
        near = 1 - smoothstep(0.0012, 0.0042, gap)
        if near > 0 and lid > 0:
            z = lerp(z, lid, near)
        return z

    def displacement(self, b, phi):
        """Feature displacement (m) along the section normal at base point b."""
        x, y, z = b.x, b.y, b.z
        if z < -0.01:
            return 0.0
        front = smoothstep(-0.01, 0.03, z)
        d = 0.0
        ax = abs(x)

        # Brow ridge and glabella.
        brow = (0.0026 + 0.0026 * self.br) * bump(0, y - 0.0195, 1, 0.0105) * (1 - smoothstep(0.045, 0.062, ax))
        d += brow
        d -= 0.0022 * bump(x, y - self.y_root, 0.012, 0.010)
        # Temples.
        d -= 0.0018 * bump(ax - 0.064, y - 0.03, 0.016, 0.02)
        # Cheekbones and cheeks.
        d += (0.0022 + 0.0028 * self.cb) * bump(ax - 0.047, y + 0.017, 0.024, 0.015)
        d += 0.0042 * self.cf * bump(ax - 0.045, y + 0.056, 0.026, 0.026)
        d -= 0.0012 * bump(ax - 0.022, y + 0.017, 0.012, 0.008)       # tear trough
        # Jaw ridge: along the mandible from the angle to the chin.
        if self.jd or self.js:
            W, _, _, _, _ = self.section(-0.09 * self.kl)
            gon = Vector((W * 0.98, -0.087 * self.kl, 0.012))
            chin = Vector((0.018, self.y_menton + 0.004, 0.072))
            dist, tt = fg.seg_dist(Vector((ax, y, z)), gon, chin)
            d += (0.0012 * self.jd + 0.0008 * self.js) * max(0.0, 1 - (dist / 0.009) ** 2) ** 2
            d += 0.0025 * self.js * bump(ax - gon.x, y - gon.y, 0.016, 0.016)
        # Nose.
        nw = self.cNW(y)
        P = self.cP(y)
        if P > 0 and ax < nw:
            tt = ax / nw
            d += P * (1 - tt * tt) ** 1.7
        ys = self.y_sub
        d += (0.0062 + 0.0018 * self.nflare) * bump(ax - (self.alar - 0.0058), y - (ys + 0.0045), 0.0062, 0.0058)
        d -= 0.0022 * bump(ax - (self.alar + 0.0012), y - (ys + 0.005), 0.0028, 0.0065)
        # Nasolabial folds (age).
        if self.age > 0:
            a = Vector((self.alar + 0.002, ys + 0.004, 0))
            bb = Vector((self.mw + 0.008, self.y_sto - 0.008, 0))
            dist, _ = fg.seg_dist(Vector((ax, y, 0)), a, bb)
            side = (Vector((ax, y, 0)) - a).cross(bb - a).z
            if dist < 0.007:
                k = (1 - dist / 0.007) ** 2
                d += self.age * (0.0016 * k if side < 0 else -0.0009 * k)
        # Lips.
        d += self._lips(x, y)
        # Chin.
        cw = 0.0165 * (1 + 0.3 * self.cw)
        d += (0.0055 + 0.004 * self.cp) * bump(x, y - self.y_chin, cw, 0.0145)
        d -= 0.0018 * bump(x, y - (self.y_sto - 0.0185 * self.kl), 0.016, 0.0045)
        if self.cleft > 0:
            d -= 0.0012 * self.cleft * bump(x, y - self.y_chin, 0.0028, 0.007)
        # Throat.
        d += 0.0038 * bump(x, y + 0.168, 0.01, 0.014)
        return d * front

    def eye_region(self, x, y):
        side = 1 if x > 0 else -1
        dx, dy = x - side * self.ex, y
        return side * x > 0.004 and (dx / 0.0235) ** 2 + (dy / 0.0205) ** 2 < 1.0

    def lip_lines(self, x):
        """(stomion, upper vermilion border, lower vermilion border) heights at x (|x| < mouth width)."""
        mw = self.mw
        t = clamp(abs(x) / mw, 0, 1)
        sto = self.y_sto + 0.0028 * self.smile * t * t
        hu = (0.0072 + 0.0020 * self.lfull) * self.kl
        hl = (0.0088 + 0.0025 * self.lfull) * self.kl
        bow = 1.0 - 0.13 * bump(x, 0, 0.0035, 1) + 0.06 * bump(abs(x) - 0.0055, 0, 0.004, 1)
        upper = sto + hu * (1 - t ** 1.8) ** 0.55 * bow
        lower = sto - hl * (1 - t ** 2.0) ** 0.6
        return sto, upper, lower

    def _lips(self, x, y):
        mw = self.mw
        ax = abs(x)
        d = 0.0
        sto, upper, lower = self.lip_lines(min(ax, mw))
        lat = max(0.0, 1 - (ax / (mw * 1.12)) ** 2)
        full = 1 + 0.35 * self.lfull
        # Upper lip: from the subnasale down to the vermilion border, then rolled in to the stomion.
        ys = self.y_sub
        a_u, a_l = 0.0064 * full, 0.0072 * full
        if ys > y >= upper:
            q = (y - upper) / max(ys - upper, 1e-4)
            d += (a_u * (1 - q) ** 1.35 + 0.0018 * bump(0, q - 0.75, 1, 0.25)) * lat ** 0.8
            v = smoothstep(ys, ys - 0.004, y) * (1 - smoothstep(upper + 0.003, upper, y))
            d += v * (0.0010 * bump(ax - 0.0052, 0, 0.0032, 1) - 0.0006 * bump(x, 0, 0.003, 1))
        elif upper > y >= sto:
            sv = (y - sto) / max(upper - sto, 1e-4)
            d += (0.0030 + (a_u - 0.0030) * math.sin(min(1.0, sv / 0.8) * math.pi / 2)) * lat ** 0.8
        elif sto > y > lower - 0.009:
            sv = (sto - y) / max(sto - lower, 1e-4)
            if sv <= 1:
                v = 0.0030 + (a_l - 0.0030) * math.sin(math.pi * sv / 1.12)
            else:
                border = 0.0030 + (a_l - 0.0030) * math.sin(math.pi / 1.12)
                q = (lower - y) / 0.009
                v = border * (1 - smoothstep(0, 1, q)) - 0.0006 * bump(0, q - 0.6, 1, 0.4)
            d += v * lat ** 0.7
        # The mouth line and the corners.
        d -= 0.0024 * bump(0, y - sto, 1, 0.0011) * (1 - smoothstep(mw * 0.9, mw * 1.05, ax))
        d -= 0.0008 * bump(ax - mw, y - sto - 0.0006, 0.004, 0.003)
        return d

    # ---- the skin -----------------------------------------------------------------------------------------

    def pos(self, u, y, inside=None):
        phi = u / U
        b = self.base(phi, y)
        n = self.normal_h(phi, y)
        p = b + n * self.displacement(b, phi)
        if b.z > 0.04 and self.eye_region(b.x, y):
            p.z = self._eye_z(1 if b.x > 0 else -1, b.x, y, p.z, inside)
        return p

    def point(self, x, y, offset=0.0):
        """The skin point at frontal (x, y), pushed `offset` along the surface normal."""
        u = self.phi_at(x, y) * U
        p = self.pos(u, y)
        if offset:
            p = p + self.normal(u, y) * offset
        return p

    def normal(self, u, y, h=2.5e-4):
        b = self.base(u / U, y)
        ins = self.in_aperture(b.x, y) if b.z > 0.04 and abs(b.x) > 0.01 else False
        du = self.pos(u + h, y, ins) - self.pos(u - h, y, ins)
        dv = self.pos(u, min(y + h, Y_TOP), ins) - self.pos(u, y - h, ins)
        n = du.cross(dv)
        if n.length < 1e-14:
            return Vector((0, 1, 0))
        n.normalize()
        p = self.pos(u, y)
        if n.dot(Vector((p.x, 0.3 * (p.y - 0.0), p.z))) < 0:
            n = -n
        return n

    def spacing(self, u, y):
        """Target edge length (m) of the skin triangulation at (u, y)."""
        phi = u / U
        if y < -0.135:
            return 0.013
        if abs(phi) > 1.9 or y > 0.06:
            return 0.013
        if abs(phi) > 1.3:
            return 0.009
        b = self.base(phi, y)
        ax = abs(b.x)
        s = 0.0062 if y > 0.035 else 0.0055
        for side in (1, -1):
            dx, dy = b.x - side * self.ex, y
            r = math.sqrt((dx / 0.019) ** 2 + (dy / 0.0145) ** 2)
            if r < 1.0:
                s = min(s, 0.0015 + 0.0028 * smoothstep(0.45, 1.0, r))
        if ax < 0.022 and self.y_root + 0.008 > y > self.y_sub - 0.004:
            s = min(s, 0.0026)
        if ax < self.mw + 0.005 and self.y_sub > y > self.y_sto - 0.014:
            s = min(s, 0.0024 if ax < self.mw else 0.003)
        return s

    def lip_regions(self):
        """Paint regions in the (u, y) domain: the vermilion of both lips and the dark mouth line."""
        mw = self.mw
        n = 24
        xs = [mw * (-1 + 2 * i / n) for i in range(n + 1)]
        xs = [math.copysign(abs(x / mw) ** 0.8 * mw, x) for x in xs]
        def uv(x, y):
            return (self.phi_at(x, y) * U, y)
        upper, lower, line_up, line_dn = [], [], [], []
        for x in xs:
            sto, up, lo = self.lip_lines(x)
            upper.append(uv(x, up))
            lower.append(uv(x, lo))
            line_up.append(uv(x, sto + 0.00055 * (1 - abs(x / mw))))
            line_dn.append(uv(x, sto - 0.00055 * (1 - abs(x / mw))))
        sto_line = [uv(x, self.lip_lines(x)[0]) for x in xs]
        lips_poly = upper + lower[::-1][1:-1]
        mouth_poly = line_up + line_dn[::-1][1:-1]
        return [("lips", lips_poly), ("mouth", mouth_poly)], [sto_line]

    def facial_hair_regions(self, style, inset=0.0):
        """Polygons in the (u, y) domain covered by facial hair of `style` (stubble paint, beard shells). `inset`
        pulls the cheek line and the neck line in (m), for the denser inner area of a soft-edged stubble."""
        if style in (None, "", "clean"):
            return []
        if style == "moustache":
            return [self._moustache_poly()]
        if style == "goatee":
            return [self._goatee_poly(), self._moustache_poly()]
        kl = self.kl
        uv = lambda x, y: (self.phi_at(x, y) * U, y)
        i = inset
        # Cheek line: from the sideburn, down across the cheek to beside the mouth, up round the nose.
        cheek = [(1.47 * U, 0.012 - 1.2 * i), (1.40 * U, -0.006 - i), (1.26 * U, -0.022 - i),
                 (1.06 * U, -0.033 * kl - i), (0.84 * U, -0.040 * kl - i), (0.66 * U, -0.047 * kl - i),
                 uv(self.mw + 0.010 + i, self.y_sub - 0.004 - i), uv(self.alar + 0.002 + i, self.y_sub + 0.0005 - i),
                 uv(0.008, self.y_sub - 0.0015 - i), (0.0, self.y_sub - 0.002 - i)]
        neck = [(0.0, -0.160 + 1.4 * i), (0.55 * U, -0.157 + 1.4 * i), (1.05 * U, -0.145 + 1.4 * i),
                (1.38 * U, -0.118 + i), (1.55 * U, -0.075 * kl), (1.56 * U, -0.02), (1.54 * U, 0.012 - 1.2 * i)]
        upper = [(p.x, p.y) for p in spline([(u, y, 0) for u, y in cheek], 3)]   # sideburn -> under the nose
        lowr = [(p.x, p.y) for p in spline([(u, y, 0) for u, y in neck], 3)]     # chin centre -> sideburn
        poly_left = lowr + upper[1:-1]                              # chin centre .. sideburn .. under-nose
        right = [(-u, y) for u, y in poly_left]
        return [[(0.0, self.y_sub - 0.002 - i)] + right[::-1][:-1] + poly_left[1:]]

    def _moustache_poly(self):
        pts = []
        n = 12
        for i in range(n + 1):
            x = -(self.mw + 0.003) + 2 * (self.mw + 0.003) * i / n
            sto, up, _ = self.lip_lines(clamp(x, -self.mw, self.mw))
            pts.append((self.phi_at(x, up) * U, up + 0.0005))
        top = []
        for i in range(n + 1):
            x = (self.mw + 0.002) - 2 * (self.mw + 0.002) * i / n
            y = self.y_sub - 0.0025 - 0.002 * (abs(x) / self.mw) ** 2
            top.append((self.phi_at(x, y) * U, y))
        return pts + top

    def _goatee_poly(self):
        kl = self.kl
        pts = []
        n = 14
        for i in range(n + 1):
            a = math.pi * i / n
            x = -(self.mw * 0.62) * math.cos(a)
            y = self.y_sto - 0.0115 * kl - (0.026 * kl) * math.sin(a) ** 0.9
            pts.append((self.phi_at(x, y) * U, y))
        return pts[::-1]


# ---- skin, eyes, brows, ears --------------------------------------------------------------------------------

def _smax(a, b, k):
    h = max(k - abs(a - b), 0.0) / k
    return max(a, b) + h * h * k * 0.25


def _poly_dist(p, poly):
    best = 1e9
    for i in range(len(poly)):
        a, b = Vector(poly[i - 1] + (0,)), Vector(poly[i] + (0,))
        d, _ = fg.seg_dist(Vector((p[0], p[1], 0)), a, b)
        best = min(best, d)
    return best


def _inside(p, poly):
    x, y = p
    c = False
    for i in range(len(poly)):
        x1, y1 = poly[i - 1]
        x2, y2 = poly[i]
        if (y1 > y) != (y2 > y) and x < (x2 - x1) * (y - y1) / (y2 - y1) + x1:
            c = not c
    return c


def lash_regions(head):
    """Dark lash lines along each upper lid margin (and a fainter one under the lower lid), in the (u, y) domain."""
    out = []
    for side in (1, -1):
        up, lo = [], []
        n = 22
        for i in range(n + 1):
            t = -0.96 + 1.98 * i / n
            x, yu, yl = head.aperture(side, t)
            w = 0.00085 * (1 - 0.55 * abs(t)) + 0.00025 * max(0.0, t)
            up.append((head.phi_at(x, yu - 0.00005) * U, yu - 0.00005))
            lo.append((head.phi_at(x, yu + w) * U, yu + w))
        out.append(("lashes", up + lo[::-1]))
    return out


def skin(head, facial_hair_style):
    """The head and neck skin with the lips, the mouth line and the stubble area painted. Returns (Mesh, normals)."""
    domain = (-math.pi * U, math.pi * U, Y_BOT, Y_TOP)
    pts = fg.adaptive_points(*domain, head.spacing, 0.012)
    regions, creases = head.lip_regions()
    regions = regions + lash_regions(head)
    for side in (1, -1):
        for inset in (0.0, head.ROLL):
            loop = [(head.phi_at(x, y) * U, y) for x, y in head.aperture_outline(side, inset=inset)]
            creases.append(loop + loop[:1])
    stub = head.facial_hair_regions(facial_hair_style)
    inner = head.facial_hair_regions(facial_hair_style, inset=0.006) if facial_hair_style in (
        "light_stubble", "stubble", "short_beard", "full_beard") else []
    regions = [("stubble_edge" if inner else "stubble", p) for p in stub] + [("stubble", p) for p in inner] + regions
    return fg.surface(head.pos, domain, pts, "skin", regions=regions, creases=creases,
                      normal=lambda u, y: head.normal(u, y))


def eyes(head, gaze=(0.0, -0.02)):
    """Both eyeballs: sclera ('eyes'), iris ('iris') and pupil ('pupil') as polar caps, looking along +Z."""
    out = []
    pupil_a, iris_a = 0.17, 0.50
    for side in (1, -1):
        c = head.eye_centre(side)
        m = Mesh()
        rings = [0.0, 0.10, pupil_a, 0.30, 0.42, iris_a, 0.62, 0.85, 1.15, 1.5, 1.9, 2.3, 2.7, math.pi]
        segs = 18
        grid = []
        for a in rings:
            row = []
            for j in range(segs if 0 < a < math.pi else 1):
                ph = 2 * math.pi * j / segs
                r = EYE_R
                if a < iris_a:
                    r += 0.0006 * math.cos(a / iris_a * math.pi / 2)  # the cornea bulges a little
                row.append(m.vert(Vector((math.sin(a) * math.cos(ph) * r, math.sin(a) * math.sin(ph) * r,
                                          math.cos(a) * r))))
            grid.append(row)
        for i in range(len(rings) - 1):
            a = 0.5 * (rings[i] + rings[i + 1])
            slot = "pupil" if a < pupil_a else "iris" if a < iris_a else "eyes"
            r0, r1 = grid[i], grid[i + 1]
            for j in range(segs):
                j2 = (j + 1) % segs
                if len(r0) == 1:
                    m.face((r0[0], r1[j], r1[j2]), slot)
                elif len(r1) == 1:
                    m.face((r0[j], r1[0], r0[j2]), slot)
                else:
                    m.face((r0[j], r1[j], r1[j2], r0[j2]), slot)
        m.orient_closed()
        rot = Matrix.Rotation(gaze[1], 4, "X") @ Matrix.Rotation(-side * 0.035 + gaze[0], 4, "Y")
        m = m.transformed(Matrix.Translation(c) @ rot)
        out.append((m, [(v - c).normalized() for v in m.verts]))
    return fg.concat(out)


def brows(head, traits):
    """Eyebrows: thin tapered shells on the skin, thicker at the inner end."""
    b = traits.get("brows", {})
    thick = 0.0068 + 0.0018 * _p(b, "thickness")
    arch = 0.0022 + 0.0018 * _p(b, "arch")
    angle = 0.002 * _p(b, "angle")
    lift = 0.0015 * _p(b, "height")
    bulk = 0.0013 + 0.0006 * _p(b, "thickness")
    parts = []
    for side in (1, -1):
        x0 = 0.0125 + 0.0015 * head.es
        x1 = head.ex + head.aw + 0.0075

        def centre(t):
            x = x0 + (x1 - x0) * t
            peak = 0.68
            if t < peak:
                rise = math.sin(0.5 * math.pi * t / peak) ** 1.3
            else:
                rise = 1 - 1.6 * ((t - peak) / (1 - peak)) ** 1.6
            y = 0.0155 + lift + arch * rise - angle * t
            return x, y

        def width(t):
            return thick * (1.0 - 0.62 * t ** 1.4) * (0.75 + 0.25 * min(1.0, t / 0.12))

        def at(t, v):
            x, y = centre(t)
            y += v * width(t) / 2
            return side * x, y

        def pos(t, v):
            x, y = at(t, v)
            u = head.phi_at(x, y) * U
            off = bulk * (1 - v * v) ** 0.6 * (1 - 0.5 * t) - 0.00025
            return head.pos(u, y) + head.normal(u, y) * off

        def nrm(t, v):
            x, y = at(t, v)
            return head.normal(head.phi_at(x, y) * U, y)

        ts = [i / 14 for i in range(15)]
        vs = [-1, -0.6, -0.2, 0.2, 0.6, 1]
        parts.append(fg.grid_surface(pos, ts, vs, lambda t, v: "brows", normal=nrm))
    return fg.concat(parts)


EAR_OUTLINE = [
    (0.0035, 0.036), (-0.0075, 0.0345), (-0.0165, 0.029), (-0.0215, 0.018), (-0.0225, 0.006), (-0.019, -0.006),
    (-0.0135, -0.016), (-0.0095, -0.025), (-0.004, -0.0305), (0.003, -0.0295), (0.007, -0.022), (0.0095, -0.010),
    (0.0105, 0.004), (0.010, 0.020), (0.0085, 0.031),
]


def _closed_spline(pts, per):
    n = len(pts)
    out = []
    for i in range(n):
        p0, p1, p2, p3 = (Vector(pts[(i + k) % n] + (0,)) for k in (-1, 0, 1, 2))
        for j in range(per):
            t = j / per
            t2, t3 = t * t, t * t * t
            out.append(0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2
                              + (-p0 + 3 * p1 - 3 * p2 + p3) * t3))
    return out


def ears(head):
    """Both ears: helix rim, antihelix, concha bowl, a lobe, and a back that tucks into the head."""
    k = 1.0 + 0.07 * head.ear_size
    outline = [Vector((v.x * k, v.y * k, 0)) for v in _closed_spline(EAR_OUTLINE, 2)]
    nu = len(outline)
    centre = Vector((0.003 * k, -0.002 * k, 0))
    # depth profile across the ear (v: 0 rim .. 1 ear canal) for the upper ear; the lobe stays flat and thick.
    prof = Curve([(0.0, -0.0015), (0.05, 0.0028), (0.12, 0.0034), (0.2, 0.0005), (0.3, -0.0004), (0.4, 0.0028),
                  (0.5, 0.0018), (0.62, -0.0045), (0.8, -0.0075), (0.92, -0.0085), (1.0, -0.0095)])

    def lobe_w(i):
        p = outline[i]
        return smoothstep(-0.012, -0.024, p.y)

    def front_pos(i, v):
        p = outline[i % nu]
        q = p.lerp(centre, v)
        lw = lobe_w(i % nu)
        dep = lerp(prof(v), 0.0012 * (1 - v), lw)
        attach = smoothstep(0.0055, 0.0095, p.x * (1 - v))      # the front edge sinks into the head
        return Vector((q.x, q.y, dep - 0.006 * attach))

    vs = [0.0, 0.03, 0.07, 0.12, 0.18, 0.26, 0.34, 0.42, 0.52, 0.64, 0.78, 0.9, 1.0]
    m = Mesh()
    grid = []
    for v in vs:
        grid.append([m.vert(front_pos(i, v)) for i in range(nu)])
    for j in range(len(vs) - 1):
        for i in range(nu):
            i2 = (i + 1) % nu
            m.face((grid[j][i], grid[j][i2], grid[j + 1][i2], grid[j + 1][i]), "skin")
    # The back: from the rim, behind, to a root ring set into the head.
    back = []
    for w in (0.35, 0.75, 1.0):
        row = []
        for i in range(nu):
            p = outline[i]
            q = p.lerp(centre, 0.12 + 0.35 * w)
            row.append(m.vert(Vector((q.x, q.y, -0.002 - 0.0095 * w ** 1.2))))
        back.append(row)
    rows = [grid[0]] + back
    for j in range(len(rows) - 1):
        for i in range(nu):
            i2 = (i + 1) % nu
            m.face((rows[j][i2], rows[j][i], rows[j + 1][i], rows[j + 1][i2]), "skin")
    # Orient: front faces toward +Z of the ear plane.
    for kf, f in enumerate(m.faces):
        n = m.face_normal(f)
        c = m.centroid(f)
        want = Vector((0, 0, 1)) if c.z > -0.0015 or kf < (len(vs) - 1) * nu else Vector((0, 0, -1))
        if kf >= (len(vs) - 1) * nu:
            want = Vector((c.x - centre.x, c.y - centre.y, -0.6))
        if n.dot(want) < 0:
            m.faces[kf] = f[::-1]
    normals = fg.flat_normals(m)
    out = []
    head.lobes = []
    for side in (1, -1):
        y = -0.016 * head.kl
        W, _, _, _, _ = head.section(y)
        out_ang = math.radians(21 + 9 * head.ear_out)
        lean = math.radians(14)
        # Ear plane: local +Z points outward (+X side), local +X backwards (-Z head), local +Y up.
        frame_m = Matrix(((0, 0, 1, 0), (0, 1, 0, 0), (1, 0, 0, 0), (0, 0, 0, 1)))
        rot = Matrix.Rotation(out_ang, 4, "Y") @ Matrix.Rotation(lean, 4, "Z")
        pos = Matrix.Translation(Vector((W + 0.0035, y, -0.004)))
        mat = pos @ frame_m @ rot
        if side < 0:
            mat = Matrix.Scale(-1, 4, Vector((1, 0, 0))) @ mat
        out.append(fg.transform((m, normals), mat))
        lobe = mat @ Vector((-0.0015 * k, -0.0255 * k, 0.0022))
        head.lobes.append(lobe)
    return fg.concat(out)


def studs(head, r=0.0021):
    """Small round stud earrings on both lobes (slot 'jewellery')."""
    parts = []
    from .mesh import ellipsoid
    for p in head.lobes:
        m = ellipsoid((r, r, r), tuple(p), 10, 6, "jewellery")
        parts.append((m, [(v - p).normalized() for v in m.verts]))
    return fg.concat(parts)
