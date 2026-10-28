"""The 22 drivers' helmets: helmets/<CODE>.glb (each <= 12k triangles).

A 2026-style closed-face helmet centred on the head (the car's `helmet` empty), facing +Z, +Y up: a smooth shell with a
chin bar, a raised dark visor with its rubber gasket and side pivots, a visor strip above it, a rear spoiler and a
neck seal. The design approximates each driver's real colour scheme from data/drivers.json: a base colour and two
accents (`helmet_a`, `helmet_b`) in one motif (stripes, chevrons, halves, flag_bands, swirls), plus a small
nationality flag on the back. Colour schemes only: no copied artwork, no sponsor marks.

The shell is parametrised by (a, b): a is the angle round the lateral axis in the head's mid-plane (0 = straight
ahead, pi/2 = the top, pi = the back), b the latitude towards the sides. The poles sit at the sides, so stripes over the
crown are regular; paint regions are exact polygons in (a, b) (lib.figure.surface).

Node: helmet (one mesh). Slots: helmet_base helmet_a helmet_b visor helmet_trim flag_1..flag_4.
"""
import json
import math
import os

import bpy
from mathutils import Matrix, Vector

from lib import asset, blend, export, materials, studio
from lib import figure as fg
from lib.figure import clamp, smoothstep
from lib.flags import FLAGS
from lib.mesh import Mesh, rounded_outline
from lib.validate import Contract

GROUP = "drivers"
BUDGET = 12_000
DATA = os.path.join(asset.TOOLS_DIR, "data", "drivers.json")

RX, RY, RZF, RZB = 0.128, 0.145, 0.152, 0.150
A0, A1 = -math.pi / 2, 1.5 * math.pi
B0, B1 = -math.pi / 2 + 1e-4, math.pi / 2 - 1e-4
R = 0.14          # metres per radian in the triangulation domain
VISOR = (-0.10, 0.30, 0.9)      # a from, a to, half-width in b
EDGE = 0.008      # longest paint-region edge in the domain (m): longer straight edges would cut chords under the shell


def data():
    with open(DATA, encoding="utf-8") as f:
        return json.load(f)["drivers"]


def bottom_y(z):
    """The shell's lower edge: lowest under the chin bar."""
    return -0.118 - 0.02 * clamp(z / RZF, -1, 1)


def shell_point(a, b):
    """The shell at (a, b). The domain's two ends, a = -pi/2 and a = 3pi/2, are the same meridian (straight down):
    near the side poles the shell is kept right down there, so every front- or back-only shaping term depends on
    cos(a) and agrees on both ends (else the ends part and leave a slit down the lower sides), and the neck roll
    vanishes at the poles (else they open up)."""
    ca, sa = math.cos(a), math.sin(a)
    cb, sb = math.cos(b), math.sin(b)
    x = RX * math.copysign(abs(sb) ** 0.9, sb)
    y = RY * cb * sa
    z = (RZF if ca >= 0 else RZB) * cb * ca
    # Chin bar: the lower front pushes forward and squares off.
    low_front = smoothstep(0.1, -0.6, a) * smoothstep(0.5, -0.2, abs(b) - 0.2) * smoothstep(0.0, 0.25, ca) \
        if ca > 0 else 0.0
    z += 0.026 * low_front
    if y < 0:
        # The lower front is deeper; half way on the seam meridian, from both sides.
        front = smoothstep(0.0, 0.8, math.acos(clamp(ca, -1.0, 1.0))) * smoothstep(-0.25, 0.25, ca)
        y *= 1.0 + 0.08 * front
    # The back flares a little at the bottom (the neck roll).
    if ca < 0 and sa < 0:
        z -= 0.008 * smoothstep(-0.2, -0.9, sa) * smoothstep(0.0, -0.25, ca) * smoothstep(0.0, 0.4, cb)
    return Vector((x, y, z))


def _visor_poly():
    a0, a1, w = VISOR
    pts = [(a0 * R, -w * R), (a0 * R, w * R), (a1 * R, w * 1.02 * R), (a1 * R, -w * 1.02 * R)]
    return rounded_outline(pts, 0.03, 4)


def _sd_rect(u, v, a0, a1, w):
    """Signed distance (param metres) to the visor rectangle: negative inside."""
    du = max(a0 * R - u, u - a1 * R)
    dv = abs(v) - w * R
    out = Vector((max(du, 0), max(dv, 0))).length
    return out + min(max(du, dv), 0.0)


def pos(u, v):
    a, b = u / R, v / R
    p = shell_point(a, b)
    a0, a1, w = VISOR
    d = _sd_rect(u, v, a0, a1, w)
    n = p.normalized()
    # The visor stands proud of the shell, with a rounded edge.
    p = p + n * (0.0035 * (1 - smoothstep(-0.003, 0.004, d)))
    return p


def _band(a0, a1, b0, b1):
    return [(a0 * R, b0 * R), (a1 * R, b0 * R), (a1 * R, b1 * R), (a0 * R, b1 * R)]


def _curve_band(path, width):
    """A polygon of `width` (rad) round a polyline [(a, b)]."""
    left, right = [], []
    for i, (a, b) in enumerate(path):
        a2, b2 = path[min(i + 1, len(path) - 1)]
        a1, b1 = path[max(i - 1, 0)]
        da, db = a2 - a1, b2 - b1
        L = math.hypot(da, db) or 1.0
        na, nb = -db / L, da / L
        t = i / (len(path) - 1)
        ww = width * (0.35 + 0.65 * math.sin(math.pi * min(1.0, t * 1.1)) ** 0.5)
        left.append(((a + na * ww) * R, (b + nb * ww) * R))
        right.append(((a - na * ww) * R, (b - nb * ww) * R))
    return left + right[::-1]


def motif_regions(motif):
    """[(slot, polygon)] for the motif, bottom layer first."""
    out = []
    if motif == "stripes":
        out.append(("helmet_a", _band(0.45, 2.75, -0.16, 0.16)))
        for s in (1, -1):
            lo, hi = sorted((s * 0.26, s * 0.38))
            out.append(("helmet_b", _band(0.45, 2.7, lo, hi)))
        out.append(("helmet_a", _band(-0.5, -0.3, -0.6, 0.6)))
    elif motif == "chevrons":
        for k, (slot, a0) in enumerate((("helmet_a", 0.75), ("helmet_b", 1.3), ("helmet_a", 1.85))):
            th = 0.17 if slot == "helmet_a" else 0.11
            poly = [(a0, 0.0), (a0 + 0.75, 1.05), (a0 + 0.75 + th, 1.05), (a0 + th, 0.0),
                    (a0 + 0.75 + th, -1.05), (a0 + 0.75, -1.05)]
            out.append((slot, [(a * R, b * R) for a, b in poly]))
    elif motif == "halves":
        top = []
        n = 24
        for i in range(n + 1):
            b = -1.25 + 2.5 * i / n
            top.append((0.42 + 0.05 * math.cos(3 * b), b))
        back = []
        for i in range(n + 1):
            b = 1.25 - 2.5 * i / n
            back.append((2.75 - 0.08 * math.cos(2 * b), b))
        out.append(("helmet_a", [(a * R, b * R) for a, b in top + back]))
        edge = [(a - 0.05, b) for a, b in top]
        out.append(("helmet_b", [(a * R, b * R) for a, b in top[::-1] + edge]))
    elif motif == "flag_bands":
        out.append(("helmet_a", _band(0.42, 2.8, -0.42, 0.42)))
        out.append(("helmet_b", _band(0.42, 2.8, -0.14, 0.14)))
        out.append(("helmet_b", _band(-0.5, -0.3, -0.6, 0.6)))
    elif motif == "swirls":
        for s in (1, -1):
            path = [(0.55 + 2.0 * t, s * (1.15 - 0.75 * t + 0.28 * math.sin(math.pi * 2 * t))) for t in
                    [i / 16 for i in range(17)]]
            out.append(("helmet_a", _curve_band(path, 0.17)))
            path2 = [(a + 0.25, b * 0.72) for a, b in path]
            out.append(("helmet_b", _curve_band(path2, 0.07)))
    return out


def flag_regions(code):
    """The nationality flag on the back of the shell, high up."""
    spec = FLAGS[code]
    a_mid, b_w, a_h = 2.35, 0.26, 0.17
    out = []
    for slot, polys in spec["layers"]:
        for poly in polys:
            # x right (as seen from behind = towards -X), y down (towards the bottom = larger a).
            out.append((slot, [((a_mid - a_h / 2 + py * a_h) * R, (b_w - 2 * b_w * px) * R) for px, py in poly]))
    return out


def visor_regions():
    a0, a1, w = VISOR
    gasket = rounded_outline([(a0 - 0.05, -w - 0.06), (a0 - 0.05, w + 0.06), (a1 + 0.05, w + 0.07),
                              (a1 + 0.05, -w - 0.07)], 0.4, 4)
    strip = _band(a1 + 0.05, a1 + 0.2, -(w + 0.02), w + 0.02)
    return [("helmet_trim", [(a * R, b * R) for a, b in gasket]), ("helmet_a", strip), ("visor", _visor_poly())]


def keep(u, v):
    p = pos(u, v)
    return p.y > bottom_y(p.z)


def _rim(a, b):
    """Height above the lower edge (negative: cut away)."""
    p = shell_point(a, b)
    return p.y - bottom_y(p.z)


def _bisect(f, lo, hi, n=40):
    flo = f(lo)
    for _ in range(n):
        m = 0.5 * (lo + hi)
        if f(m) * flo > 0:
            lo, flo = m, f(m)
        else:
            hi = m
    return 0.5 * (lo + hi)


def bottom_crease():
    """The lower edge as an exact edge loop in the domain: for each b, the a where the shell meets bottom_y.

    At the sides the edge runs onto the seam meridian (a = -pi/2 = 3pi/2, the domain's two ends); both loops end
    exactly there, at the b where the edge meets it, so the edge stays exact (not cut ragged by `keep`) right to the
    seam and the ends of the two loops weld."""
    pts_f, pts_b = [], []
    b_seam = _bisect(lambda b: _rim(A0, b), 0.0, B1)
    for i in range(49):
        b = B0 + (B1 - B0) * i / 48
        if abs(b) >= b_seam:
            continue
        for lst, lo, hi in ((pts_f, -math.pi / 2, 0.6), (pts_b, 2.5, 1.5 * math.pi)):
            # Bisection on a in [lo, hi] for y - bottom_y = 0 (above inside the range end nearer the top).
            if _rim(lo, b) * _rim(hi, b) > 0:
                continue
            lst.append((_bisect(lambda a: _rim(a, b), lo, hi) * R, b * R))
    loops = []
    for lst, a_end in ((pts_f, A0), (pts_b, A1)):
        # Towards the seam the edge runs nearly along a: trace that last stretch in steps of a (solving for b).
        a_l, b_l = lst[-1][0] / R, lst[-1][1] / R
        n = max(2, math.ceil(abs(a_end - a_l) * R / 0.004))
        tail = []
        for k in range(1, n):
            a = a_l + (a_end - a_l) * k / n
            tail.append((a * R, _bisect(lambda b: _rim(a, b), b_l, B1) * R))
        tail.append((a_end * R, b_seam * R))
        loops.append([(u, -v) for u, v in reversed(tail)] + lst + tail)
    return loops


def build_helmet(code, scheme):
    materials.paint("helmet_base", scheme["base"])
    materials.paint("helmet_a", scheme["a"])
    materials.paint("helmet_b", scheme["b"])
    materials.define("visor", "#1d1f26", metallic=0.85, roughness=0.08)
    materials.define("helmet_trim", "#121214", roughness=0.6)
    seat = next(d for t in asset.grid()["teams"] for d in t["drivers"] if d["code"] == code)
    for slot, hexc in FLAGS[seat["nationality"]]["colours"].items():
        materials.define(slot, hexc, roughness=0.4, coat=1.0)
    domain = (A0 * R, A1 * R, B0 * R, B1 * R)

    def spacing(u, v):
        b = v / R
        return 0.019 * max(math.cos(b), 0.45)

    pts = fg.adaptive_points(*domain, spacing, 0.03)
    regions = motif_regions(scheme["motif"]) + flag_regions(seat["nationality"]) + visor_regions()
    shell = fg.surface(pos, domain, pts, "helmet_base", regions=regions, creases=bottom_crease(), keep=keep,
                       max_edge=EDGE)
    parts = [shell, spoiler(), neck_seal(), pivots()]
    obj = fg.make_smooth(fg.concat(parts), "helmet")
    return obj


def spoiler():
    """A small rear spoiler on the crown with endplates."""
    m = Mesh()
    a_lo, a_hi, w = 2.05, 2.4, 0.42
    parts = []
    top = lambda s, r: shell_point(a_lo + (a_hi - a_lo) * r, s * w) * (1.0 + 0.11 * (0.25 + 0.75 * r))
    bot = lambda s, r: shell_point(a_lo + (a_hi - a_lo) * r, s * w) * (1.0 + 0.105 * (0.25 + 0.75 * r))
    ss = [-1 + 2 * i / 10 for i in range(11)]
    rs = [0.0, 0.33, 0.66, 1.0]
    up = fg.grid_surface(top, ss, rs, lambda s, r: "helmet_base",
                         normal=lambda s, r: shell_point(a_lo + (a_hi - a_lo) * r, s * w).normalized())
    dn = fg.grid_surface(bot, ss, rs, lambda s, r: "helmet_trim",
                         normal=lambda s, r: -shell_point(a_lo + (a_hi - a_lo) * r, s * w).normalized())
    parts += [up, dn]
    for s in (-1, 1):
        def plate(r, k, s=s):
            base = shell_point(a_lo + (a_hi - a_lo) * r, s * w)
            return base * (1.0 + 0.11 * (0.25 + 0.75 * r) * k)
        side = fg.grid_surface(plate, [0.0, 0.5, 1.0], [0.0, 1.0], lambda r, k: "helmet_trim",
                               normal=lambda r, k, s=s: Vector((s, 0, 0)))
        parts.append(side)
    return fg.concat(parts)


def neck_seal():
    """Closes the bottom opening with a dark padded seal a little inside the edge (at least ~4 mm inside the shell
    all round: at 0.094 m wide it reached the shell's lower sides and showed through as a dark line)."""
    m = Mesh()
    ring = []
    n = 32
    for i in range(n):
        ph = 2 * math.pi * i / n
        x, z = 0.088 * math.sin(ph), 0.108 * math.cos(ph) - 0.005
        y = bottom_y(z) + 0.012
        ring.append(m.vert(Vector((x, y, z))))
    c = m.vert(Vector((0, -0.095, 0.0)))
    for i in range(n):
        m.face((ring[i], ring[(i + 1) % n], c), "helmet_trim")
    for k, f in enumerate(m.faces):
        if m.face_normal(f).y > 0:
            m.faces[k] = f[::-1]
    return m, [Vector((0, -1, 0))] * len(m.verts)


def pivots():
    """The visor's side pivots: small discs at each end of the visor."""
    parts = []
    from lib.mesh import cylinder
    a0, a1, w = VISOR
    for s in (1, -1):
        p = shell_point(0.5 * (a0 + a1), s * (w + 0.16))
        n = p.normalized()
        m = cylinder(0.014, 0.008, 14, "helmet_trim", "x", (0, 0, 0))
        rot = Vector((1, 0, 0)).rotation_difference(n).to_matrix().to_4x4()
        m = m.transformed(Matrix.Translation(p + n * 0.002) @ rot)
        parts.append((m, fg.flat_normals(m)))
    return fg.concat(parts)


def build(ctx):
    d = data()
    only = set(ctx.args["drivers"].split(",")) if ctx.args.get("drivers") else None
    for t in asset.grid()["teams"]:
        for drv in t["drivers"]:
            code = drv["code"]
            if only and code not in only:
                continue
            blend.reset()
            obj = build_helmet(code, d[code]["helmet"])
            print(f"[helmet] {code}: {blend.triangles(obj)} triangles")
            export.glb(ctx.model(f"helmets/{code}.glb"), [obj])


def contracts():
    out = []
    for t in asset.grid()["teams"]:
        for drv in t["drivers"]:
            out.append(Contract(f"helmets/{drv['code']}.glb", BUDGET, nodes=("helmet",),
                                materials=("helmet_base", "helmet_a", "helmet_b", "visor"),
                                size={"x": (0.24, 0.30), "y": (0.26, 0.34), "z": (0.28, 0.36)},
                                max_bytes=1024 * 1024))
    return out


FONT = os.path.join(asset.REPO_ROOT, "src", "ArcBox.Host", "fonts", "TitilliumWeb-SemiBold.ttf")


def preview(ctx):
    """Renders every helmet from the front three-quarter and the back three-quarter and composes helmets_sheet.png."""
    from PIL import Image, ImageDraw, ImageFont
    d = data()
    only = set(ctx.args["drivers"].split(",")) if ctx.args.get("drivers") else None
    res = int(ctx.args.get("res", "300"))
    samples = int(ctx.args.get("samples", "24"))
    shots = []
    for t in asset.grid()["teams"]:
        for drv in t["drivers"]:
            code = drv["code"]
            if only and code not in only:
                continue
            blend.reset()
            build_helmet(code, d[code]["helmet"])
            studio.stage((0, 0, 0), 0.35)
            f1 = f"{ctx.preview_dir}/helmet_{code}_front.png"
            f2 = f"{ctx.preview_dir}/helmet_{code}_back.png"
            studio.render(f1, (0.55, 0.22, 0.75), (0, 0.0, 0), 70, (res, res), samples)
            studio.render(f2, (-0.6, 0.45, -0.65), (0, 0.01, 0), 70, (res, res), samples)
            shots.append((code, drv["name"], f1, f2))
    cols, label_h = 4, 40
    rows = math.ceil(len(shots) / cols)
    img = Image.new("RGB", (cols * 2 * res + (cols + 1) * 8, rows * (res + label_h) + (rows + 1) * 8), (24, 25, 29))
    draw = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, 24) if os.path.exists(FONT) else ImageFont.load_default()
    for i, (code, name, f1, f2) in enumerate(shots):
        r, c = divmod(i, cols)
        x, y = 8 + c * (2 * res + 8), 8 + r * (res + label_h + 8)
        img.paste(Image.open(f1).convert("RGB"), (x, y + label_h))
        img.paste(Image.open(f2).convert("RGB"), (x + res, y + label_h))
        conf = d[code]["helmet"]["confidence"]
        draw.text((x + 6, y + 6), f"{code}  {name}   ({d[code]['helmet']['motif']}, {conf})", fill=(235, 235, 240),
                  font=font)
    path = f"{ctx.preview_dir}/helmets_sheet.png"
    img.save(path)
    print(f"[helmet] contact sheet {path}")
