"""The 2026 F1 car: car_2026.glb (LOD0, <= 120k triangles) and car_2026_lod1.glb (<= 30k).

Frame and placement match CarMeshBuilder (games/Redline/Redline.Presentation/Meshes/CarMeshBuilder.cs): metres, +Y up,
+Z forward, +X to the car's left, origin on the ground midway between the axles; wheelbase 3.4 m, width 1.9 m,
tyres 280 mm front / 375 mm rear wide, radii 352.5 / 355 mm. No beam wing (2026), active-aero flaps front and rear.

Nodes (Godot reads them):
  body                    every static part, one mesh with the named material slots below
  wheel_fl/fr/rl/rr       tyre + rim of each wheel, origin at the wheel centre (the node spins about its X axis);
                          fl/rl are on +X (the car's left)
  number_nose, number_endplate_l, number_endplate_r
                          empties where Godot draws the car number (Label3D): local +Z = outward normal,
                          local +Y = the text's up
  helmet                  empty at the driver's helmet centre (Task 2 attaches helmets/<CODE>.glb there)
Material slots (Godot rebinds them; team colours come from grid2026.json at runtime):
  paint_top paint_side stripe nose wing endplate halo   livery zones
  carbon tyre tyre_stripe rim light glass tcam          the rest
The exported base colours are McLaren's (sRGB -> linear), so the file looks right in any glTF viewer.
"""
import math

from mathutils import Vector

from lib import asset, blend, export, materials, preview as pv
from lib.mesh import (Mesh, Section, Span, T, Rx, Ry, Rz, S, box, cylinder, ellipsoid, lathe, loft, plate,
                      ring_rels, rounded_outline, section_at, spline, tube, wing)
from lib.validate import Contract

GROUP = "cars"

WHEELBASE = 3.4
WIDTH = 1.9
FRONT_AXLE = WHEELBASE / 2
REAR_AXLE = -WHEELBASE / 2
HALF_WIDTH = WIDTH / 2
FRONT_TYRE_W = 0.28
REAR_TYRE_W = 0.375
FRONT_TYRE_R = 0.3525
REAR_TYRE_R = 0.355
RIM_R = 0.232
HELMET = (0.0, 0.83, 0.15)
NUMBER_NOSE_Z = 2.05

WHEELS = {
    "wheel_fl": (HALF_WIDTH - FRONT_TYRE_W / 2, FRONT_TYRE_R, FRONT_AXLE),
    "wheel_fr": (-(HALF_WIDTH - FRONT_TYRE_W / 2), FRONT_TYRE_R, FRONT_AXLE),
    "wheel_rl": (HALF_WIDTH - REAR_TYRE_W / 2, REAR_TYRE_R, REAR_AXLE),
    "wheel_rr": (-(HALF_WIDTH - REAR_TYRE_W / 2), REAR_TYRE_R, REAR_AXLE),
}
NUMBERS = ("number_nose", "number_endplate_l", "number_endplate_r")
PAINT_SLOTS = ("paint_top", "paint_side", "stripe", "nose", "wing", "endplate", "halo")
OTHER_SLOTS = ("carbon", "tyre", "tyre_stripe", "rim", "light", "glass", "tcam")
SLOTS = PAINT_SLOTS + OTHER_SLOTS

# Quality per LOD.
LODS = {
    0: dict(per=3, half=12, subsurf=1, wheel=72, bevel=2, wing_n=14, wing_per=2, tube=10, grooves=True, detail=True),
    1: dict(per=1, half=6, subsurf=0, wheel=28, bevel=0, wing_n=6, wing_per=1, tube=5, grooves=False, detail=False),
}


def define_materials(livery, finish="gloss", tcam="#101012"):
    matte = finish == "matte"
    for slot, key in (("paint_top", "top"), ("paint_side", "side"), ("stripe", "stripe"), ("nose", "nose"),
                      ("wing", "wing"), ("endplate", "endplate"), ("halo", "halo")):
        materials.paint(slot, livery[key], matte=matte)
    materials.carbon("carbon")
    materials.rubber("tyre", "#141416")
    materials.define("tyre_stripe", "#f2c200", roughness=0.7)
    materials.metal("rim", "#3b3d42", roughness=0.3)
    materials.emissive("light", "#ff1a12", 6.0)
    materials.glass("glass", "#9fb3c8")
    materials.define("tcam", tcam, metallic=0.1, roughness=0.25, coat=1.0)


# ---- body shapes ------------------------------------------------------------------------------------------

CHASSIS = [
    # A short, blunt 2026 nose whose underside meets the front wing's main plane.
    Section(2.99, 0.148, 0.058, 0.030, 0.030, 2.0, 2.0),
    Section(2.95, 0.156, 0.084, 0.047, 0.042, 2.4, 2.3),
    Section(2.80, 0.177, 0.104, 0.071, 0.058, 2.6, 2.4),
    Section(2.50, 0.215, 0.124, 0.104, 0.086, 2.7, 2.6),
    Section(2.20, 0.257, 0.141, 0.134, 0.112, 2.8, 2.7),
    Section(1.90, 0.305, 0.165, 0.163, 0.155, 3.0, 2.8),
    Section(1.55, 0.350, 0.205, 0.205, 0.225, 3.3, 3.0),
    Section(1.10, 0.400, 0.255, 0.245, 0.300, 3.6, 3.4),
    Section(0.65, 0.420, 0.305, 0.280, 0.330, 4.0, 4.0),
    Section(0.25, 0.420, 0.345, 0.300, 0.330, 4.0, 4.0),
    Section(-0.15, 0.430, 0.350, 0.320, 0.340, 4.0, 4.0),
    # Engine cover: narrows fast behind the roll hoop (the coke-bottle waist between the sidepods).
    Section(-0.50, 0.440, 0.295, 0.365, 0.350, 3.4, 3.6),
    Section(-0.90, 0.430, 0.225, 0.325, 0.330, 3.0, 3.2),
    Section(-1.30, 0.420, 0.165, 0.235, 0.300, 2.8, 3.0),
    Section(-1.70, 0.400, 0.128, 0.160, 0.270, 2.6, 2.8),
    Section(-2.00, 0.380, 0.105, 0.110, 0.220, 2.4, 2.6),
    Section(-2.24, 0.370, 0.080, 0.070, 0.130, 2.4, 2.4),
]

AIRBOX = [
    Section(-0.10, 0.880, 0.100, 0.095, 0.200, 2.3, 2.6),
    Section(-0.22, 0.890, 0.125, 0.130, 0.230, 2.5, 2.8),
    Section(-0.45, 0.860, 0.130, 0.150, 0.250, 2.6, 3.0),
    Section(-0.80, 0.800, 0.090, 0.085, 0.220, 2.4, 3.0),
    Section(-1.15, 0.740, 0.035, 0.030, 0.120, 2.0, 2.4),
]

SIDEPOD = [
    # Downwash sidepods: a flat-topped shoulder over a deep undercut that sweeps in to the floor.
    Section(0.98, 0.480, 0.165, 0.140, 0.100, 3.2, 2.2, 0.480, 0.35),
    Section(0.86, 0.480, 0.190, 0.150, 0.170, 3.8, 1.8, 0.480, 0.60),
    Section(0.40, 0.460, 0.215, 0.160, 0.270, 4.2, 1.6, 0.465, 0.78),
    Section(-0.10, 0.430, 0.205, 0.140, 0.290, 3.8, 1.6, 0.435, 0.76),
    Section(-0.60, 0.365, 0.170, 0.115, 0.270, 3.0, 1.8, 0.365, 0.62),
    Section(-1.05, 0.285, 0.128, 0.078, 0.205, 2.6, 2.2, 0.268, 0.42),
    Section(-1.40, 0.222, 0.088, 0.050, 0.140, 2.4, 2.4, 0.180, 0.25),
]


def chassis_zone(z, rel, side):
    if rel < -0.55:
        return "carbon"
    if z > 2.35:
        return "nose"
    if 0.1 < rel < 0.22:
        return "stripe"
    if rel <= 0.1:
        return "paint_side"
    return "paint_top"


def sidepod_zone(z, rel, side):
    if rel < -0.55:
        return "carbon"
    if rel > 0.36:
        return "paint_top"
    if rel > 0.22:
        return "stripe"
    return "paint_side"


def surface_top(stations, per, x, z):
    """Height of a loft's upper surface at (x, z) and its slope dy/dz along the centre line."""
    s = section_at(stations, per, z)
    a = min(1.0, abs(x - s.x) / s.w)
    rel = max(0.0, 1 - a ** s.n) ** (1 / s.n)
    y = s.y + s.top * rel
    s2 = section_at(stations, per, z + 0.01)
    slope = ((s2.y + s2.top) - (s.y + s.top)) / 0.01
    return y, slope


def build_body(q):
    parts = []
    per = q["per"]

    # Monocoque, nose and engine cover: one loft, panel lines at the nose joint and the engine-cover split.
    rels = ring_rels(q["half"], 3.0, breaks=(-0.55, 0.1, 0.22))
    chassis = loft(CHASSIS, per, rels, chassis_zone, z_breaks=(2.35,),
                   z_grooves=(2.12, -1.32) if q["grooves"] else ())
    ch = blend.make(chassis, "chassis")
    if q["subsurf"]:
        blend.subsurf(ch, q["subsurf"])
        blend.apply(ch)

    # Cockpit opening: a rounded pocket cut out of the tub (its walls and seat are carbon).
    opening = rounded_outline([(0.66, 0.155), (0.66, -0.155), (-0.08, -0.215), (-0.08, 0.215)], 0.11, 5)
    cutter = blend.make(plate(opening, 0.9, origin=(0, 0.76, 0), u=(0, 0, 1), v=(1, 0, 0), mat="carbon"), "cockpit_cut")
    blend.boolean(ch, cutter)
    blend.apply(ch)
    blend.delete(cutter)
    parts.append(blend.mark_sharp(ch, 50))

    # Padded cockpit rim along the opening's edge.
    rim_pts = []
    pts = opening + [opening[0]]
    for (z0, x0), (z1, x1) in zip(pts, pts[1:]):
        for k in range(3):
            t = k / 3
            z, x = z0 + (z1 - z0) * t, x0 + (x1 - x0) * t
            y, _ = surface_top(CHASSIS, per, x, z)
            rim_pts.append((x * 1.02, y - 0.006, z))
    rim_pts.append(rim_pts[0])
    parts.append(blend.finish(blend.make(tube(rim_pts, 0.02, 0.016, q["tube"], major=(0, 1, 0), mat="carbon",
                                              caps=False), "cockpit_rim"), 60))
    if q["detail"]:
        # Steering wheel and a headrest pad behind the helmet.
        sw = box((0.27, 0.11, 0.035), (0, 0.66, 0.47), "carbon")
        o = blend.make(sw, "steering")
        blend.bevel(o, 0.012, 2, 30)
        parts.append(blend.finish(o, 30))
        pad = ellipsoid((0.17, 0.07, 0.07), (0, 0.72, -0.05), 20, 8, "carbon")
        parts.append(blend.finish(blend.make(pad, "headrest"), 60))

    # Airbox / roll hoop with its inlet.
    rels_a = ring_rels(max(6, q["half"] - 2), 2.6, breaks=())
    ab = blend.make(loft(AIRBOX, per, rels_a, lambda z, r, s: "paint_top"), "airbox")
    if q["subsurf"]:
        blend.subsurf(ab, q["subsurf"])
        blend.apply(ab)
    inlet = blend.make(ellipsoid((0.07, 0.078, 0.16), (0, 0.885, -0.10), 20, 10, "carbon"), "airbox_inlet")
    blend.boolean(ab, inlet)
    blend.apply(ab)
    blend.delete(inlet)
    parts.append(blend.mark_sharp(ab, 50))

    # T-cam on top of the roll hoop (black on a team's first car, yellow on the second: slot "tcam").
    tcam = wing([Span(-0.15, 1.045, -0.27, 0.075, 0.0, 0.16, 0.0), Span(0.15, 1.045, -0.27, 0.075, 0.0, 0.16, 0.0)],
                "tcam", n=max(6, q["wing_n"] // 2))
    parts.append(blend.finish(blend.make(tcam, "tcam"), 40))
    if q["detail"]:
        for x in (-0.05, 0.05):
            parts.append(blend.finish(blend.make(tube([(x, 1.0, -0.30), (x, 1.045, -0.30)], 0.008, 0.008, 6,
                                                      mat="carbon"), "tcam_post"), 60))

    # Shark fin.
    fin = plate([(-0.55, 0.96), (-1.62, 0.60), (-1.62, 0.66), (-0.62, 1.0)], 0.012, u=(0, 0, 1), v=(0, 1, 0),
                mat="paint_top")
    parts.append(_bevelled(fin, "fin", q, 0.004))

    # Sidepods with deep undercuts and letterbox inlets.
    rels_s = ring_rels(max(6, q["half"] - 2), 3.0, breaks=(-0.55, 0.22, 0.36))
    for side in (1, -1):
        st = [Section(s.z, s.y, s.w, s.top, s.bottom, s.n, s.nb, side * s.x, s.pinch) for s in SIDEPOD]
        pod = blend.make(loft(st, per, rels_s, sidepod_zone), f"sidepod_{side}")
        if q["subsurf"]:
            blend.subsurf(pod, q["subsurf"])
            blend.apply(pod)
        letterbox = rounded_outline([(-0.10, -0.085), (0.11, -0.085), (0.11, 0.095), (-0.10, 0.095)], 0.05, 4)
        cut = blend.make(plate([(side * a, b) for a, b in letterbox], 0.36, origin=(side * 0.52, 0.505, 1.0),
                               u=(1, 0, 0), v=(0, 1, 0), mat="carbon"), "pod_cut")
        blend.boolean(pod, cut)
        blend.apply(pod)
        blend.delete(cut)
        parts.append(blend.mark_sharp(pod, 50))

    parts += build_floor(q)
    parts += build_front_wing(q)
    parts += build_rear_wing(q)
    parts += build_halo_mirrors(q)
    parts += build_suspension(q)
    parts += build_rear_end(q)
    return blend.join(parts, "body")


def _bevelled(mesh, name, q, width, angle=30):
    o = blend.make(mesh, name)
    if q["bevel"]:
        blend.bevel(o, width, q["bevel"], angle)
    return blend.finish(o, 30)


def build_floor(q):
    parts = []
    half = [(1.28, 0.26), (1.12, 0.52), (0.92, 0.70), (0.80, 0.72), (-1.12, 0.72), (-1.22, 0.64), (-1.42, 0.53),
            (-2.06, 0.53)]
    outline = half + [(z, -x) for z, x in reversed(half)]
    parts.append(_bevelled(plate(outline, 0.022, origin=(0, 0.061, 0), u=(0, 0, 1), v=(1, 0, 0), mat="carbon"),
                           "floor", q, 0.006))
    for s in (1, -1):
        # Floor edge: an up-turned lip, a curled edge wing along it, and an in-wash board ahead of the sidepod.
        edge = [(0.80, 0.05), (-1.10, 0.05), (-1.12, 0.09), (-0.30, 0.115), (0.55, 0.135), (0.80, 0.09)]
        parts.append(_bevelled(plate(edge, 0.012, origin=(s * 0.716, 0, 0), u=(0, 0, 1), v=(0, 1, 0), mat="carbon"),
                               "floor_edge", q, 0.003))
        n_edge = 9 if q["detail"] else 4
        spans = []
        for k in range(n_edge):
            t = k / (n_edge - 1)
            z = 0.74 - 1.80 * t
            curl = math.sin(math.pi * t)
            spans.append(Span(-z, 0.088 + 0.02 * curl, 0.0, 0.085 + 0.03 * curl, 0.32 + 0.25 * curl, 0.14, -0.08))
        edge_wing = wing(spans[::-1], "carbon", max(6, q["wing_n"] // 2))
        # Span along X -> along Z (Ry 90deg: x -> z, chord (-z) -> -x, i.e. inwards on the +X side).
        edge_wing = edge_wing.transformed(T(s * 0.80, 0, 0) @ (Ry(math.pi / 2) if s > 0 else S(-1, 1, 1) @ Ry(math.pi / 2)))
        parts.append(blend.finish(blend.make(edge_wing, "floor_edge_wing"), 40))
        board = [(1.16, 0.06), (0.88, 0.06), (0.90, 0.21), (1.04, 0.24), (1.16, 0.15)]
        parts.append(_bevelled(plate(board, 0.012, origin=(s * 0.665, 0, 0), u=(0, 0, 1), v=(0, 1, 0),
                                     mat="carbon"), "inwash_board", q, 0.003))
        for x in (0.33, 0.47):
            fence = [(1.26, 0.03), (0.86, 0.03), (0.86, 0.06), (1.10, 0.12), (1.26, 0.07)]
            parts.append(_bevelled(plate(fence, 0.01, origin=(s * x, 0, 0), u=(0, 0, 1), v=(0, 1, 0), mat="carbon"),
                                   "fence", q, 0.002))
    # Diffuser: a ramp up to the exit between side walls, four strakes and a gurney lip.
    ramp = [(-1.15, 0.052), (-1.60, 0.10), (-2.07, 0.31), (-2.07, 0.325), (-1.60, 0.115), (-1.15, 0.068)]
    parts.append(_bevelled(plate(ramp, 1.04, origin=(0, 0, 0), u=(0, 0, 1), v=(0, 1, 0), mat="carbon"),
                           "diffuser", q, 0.004))
    for s in (1, -1):
        wall = [(-1.15, 0.05), (-2.08, 0.05), (-2.08, 0.34), (-1.60, 0.13), (-1.15, 0.075)]
        parts.append(_bevelled(plate(wall, 0.012, origin=(s * 0.526, 0, 0), u=(0, 0, 1), v=(0, 1, 0), mat="carbon"),
                               "diffuser_wall", q, 0.003))
        for x in (0.18, 0.36):
            strake = [(-1.40, 0.05), (-2.07, 0.05), (-2.07, 0.30), (-1.60, 0.10)]
            parts.append(_bevelled(plate(strake, 0.01, origin=(s * x, 0, 0), u=(0, 0, 1), v=(0, 1, 0),
                                         mat="carbon"), "strake", q, 0.002))
    parts.append(_bevelled(box((1.04, 0.022, 0.012), (0, 0.335, -2.065), "carbon"), "gurney", q, 0.003))
    return parts


def _span_list(xs, fn):
    """Symmetric span stations from -tip to +tip: fn(|x|) -> Span fields for x >= 0."""
    out = []
    for x in [-v for v in reversed(xs) if v > 0] + list(xs):
        y, z, chord, aoa, th, cam = fn(abs(x))
        out.append(Span(x, y, z, chord, aoa, th, cam))
    return out


def build_front_wing(q):
    """2026 front wing: a low, wide main plane and two gently curved flaps (the rear one is the active-aero flap),
    small endplates with an out-turned foot."""
    parts = []
    tip = 0.828
    xs = [0.0, 0.14, 0.28, 0.42, 0.55, 0.66, 0.75, tip]

    def main(x):
        u = x / tip
        return 0.092 + 0.022 * u ** 2, 3.14 - 0.05 * u * u, 0.29 - 0.03 * u, 0.06 + 0.03 * u, 0.085, -0.05

    parts.append(blend.finish(blend.make(wing(_span_list(xs, main), "wing", q["wing_n"], q["wing_per"]),
                                         "fw_main"), 40))
    fx = [0.10, 0.22, 0.36, 0.50, 0.62, 0.72, tip]

    def flap1(x):
        u = (x - 0.10) / (tip - 0.10)
        return 0.128 + 0.035 * u ** 2, 2.875 - 0.04 * u * u, 0.19, 0.30 + 0.05 * u, 0.09, -0.055

    def flap2(x):
        u = (x - 0.10) / (tip - 0.10)
        return 0.180 + 0.045 * u ** 2, 2.715 - 0.035 * u * u, 0.16, 0.52 + 0.06 * u, 0.09, -0.055

    for name, fn, slot in (("fw_flap1", flap1, "paint_top"), ("fw_flap2", flap2, "wing")):
        for s in (1, -1):
            spans = [Span(s * x, *fn(x)) for x in fx]
            if s < 0:
                spans = spans[::-1]
            parts.append(blend.finish(blend.make(wing(spans, slot, q["wing_n"], q["wing_per"]), name), 40))
    for s in (1, -1):
        ep = rounded_outline([(3.15, 0.035), (2.50, 0.035), (2.48, 0.255), (2.66, 0.275), (3.08, 0.165)], 0.03, 3)
        parts.append(_bevelled(plate(ep, 0.012, origin=(s * (tip + 0.006), 0, 0), u=(0, 0, 1), v=(0, 1, 0),
                                     mat="endplate"), "fw_endplate", q, 0.004))
        foot = [(3.13, 0.0), (2.54, 0.0), (2.54, 0.04), (3.09, 0.045)]
        parts.append(_bevelled(plate(foot, 0.01, origin=(s * (tip + 0.012), 0.038, 0), u=(0, 0, 1), v=(1, 0, 0),
                                     mat="endplate"), "fw_foot", q, 0.003))
    # Short pylons where the nose sits on the main plane.
    for s in (1, -1):
        py = [(2.98, 0.105), (2.84, 0.112), (2.86, 0.135), (2.97, 0.13)]
        parts.append(_bevelled(plate(py, 0.012, origin=(s * 0.045, 0, 0), u=(0, 0, 1), v=(0, 1, 0), mat="carbon"),
                               "nose_pylon", q, 0.003))
    return parts


RW_TIP = 0.496
ENDPLATE_T = 0.014
ENDPLATE_LEAN = 0.07
ENDPLATE_PIVOT_Y = 0.62
REAR_ENDPLATE = [(-1.98, 0.64), (-2.53, 0.61), (-2.565, 1.0), (-2.44, 1.045), (-2.24, 1.005), (-2.05, 0.93)]
ENDPLATE_NUMBER = (-2.28, 0.80)   # (z, y) of the number on the endplate's outer face
ENDPLATE_NUMBER_SIZE = 0.16       # glyph height there (CarModel.EndplateNumberSize)


def rear_endplate_matrix(s):
    """Local plate (centred on x = 0) -> the endplate on side s: at the wing tip, leaning out about its foot."""
    return T(s * (RW_TIP + ENDPLATE_T / 2), ENDPLATE_PIVOT_Y, 0) @ Rz(-s * ENDPLATE_LEAN) @ T(0, -ENDPLATE_PIVOT_Y, 0)


def endplate_number_frame(s):
    m = rear_endplate_matrix(s)
    z, y = ENDPLATE_NUMBER
    pos = m @ Vector((s * ENDPLATE_T / 2, y, z))
    rot = m.to_3x3()
    n = (rot @ Vector((s, 0, 0))).normalized()
    up = (rot @ Vector((0, 1, 0))).normalized()
    return pos, up.cross(n), up, n


def build_rear_wing(q):
    parts = []
    tip = RW_TIP
    xs = [0.0, 0.16, 0.30, 0.42, tip]

    def main(x):
        u = x / tip
        return 0.835 - 0.015 * u * u, -2.02, 0.32, 0.13, 0.11, -0.07

    def flap(x):
        return 0.885, -2.315, 0.22, 0.46, 0.1, -0.06

    parts.append(blend.finish(blend.make(wing(_span_list(xs, main), "wing", q["wing_n"], q["wing_per"]),
                                         "rw_main"), 40))
    parts.append(blend.finish(blend.make(wing(_span_list(xs, flap), "paint_top", q["wing_n"], q["wing_per"]),
                                         "rw_flap"), 40))
    for s in (1, -1):
        # 2026 endplates: short, slim, a curved top cut away towards the front, leaning out slightly; LED strip on
        # the trailing edge.
        m = rear_endplate_matrix(s)
        ep = rounded_outline(REAR_ENDPLATE, 0.05, 4)
        parts.append(_bevelled(plate(ep, ENDPLATE_T, u=(0, 0, 1), v=(0, 1, 0), mat="endplate").transformed(m),
                               "rw_endplate", q, 0.004))
        led = box((0.022, 0.24, 0.012), (0, 0.80, -2.548), "light").transformed(m)
        parts.append(blend.finish(blend.make(led, "rw_light"), 30))
        # Swan-neck pylons hooking over the main plane's leading edge.
        x = s * 0.06
        path = [(x, 0.40, -1.90), (x, 0.66, -1.96), (x, 0.93, -2.0), (x, 0.975, -2.06), (x, 0.96, -2.13),
                (x, 0.885, -2.17)]
        parts.append(blend.finish(blend.make(tube(path, 0.032, 0.008, q["tube"], major=(0, 0, 1), mat="carbon",
                                                  per=3 if q["detail"] else 1), "swan_neck"), 50))
    if q["detail"]:
        parts.append(blend.finish(blend.make(ellipsoid((0.035, 0.022, 0.09), (0, 0.985, -2.36), 16, 8, "carbon"),
                                             "rw_actuator"), 60))
    return parts


def build_halo_mirrors(q):
    parts = []
    hoop = [(0.335, 0.70, -0.20), (0.33, 0.86, 0.02), (0.29, 0.915, 0.22), (0.17, 0.94, 0.42), (0.0, 0.947, 0.535),
            (-0.17, 0.94, 0.42), (-0.29, 0.915, 0.22), (-0.33, 0.86, 0.02), (-0.335, 0.70, -0.20)]
    parts.append(blend.finish(blend.make(tube(hoop, 0.026, 0.037, q["tube"] + 4, major=(0, 1, 0), mat="halo",
                                              per=6 if q["detail"] else 2), "halo"), 60))
    pillar = [(0.0, 0.947, 0.535), (0.0, 0.90, 0.63), (0.0, 0.80, 0.70), (0.0, 0.69, 0.74)]
    parts.append(blend.finish(blend.make(tube(pillar, 0.034, 0.022, q["tube"], major=(0, 0, 1), mat="halo",
                                              per=4 if q["detail"] else 1), "halo_pillar"), 60))
    for s in (1, -1):
        # Wide, flat mirror housings on a short aero stalk from the cockpit shoulder, glass facing back.
        c = (s * 0.47, 0.765, 0.50)
        housing = ellipsoid((0.115, 0.033, 0.05), c, 28 if q["detail"] else 10, 10 if q["detail"] else 5, "paint_top")
        parts.append(blend.finish(blend.make(housing.transformed(T(c) @ Rz(-s * 0.06) @ T(-Vector(c))), "mirror"), 60))
        glass = ellipsoid((0.102, 0.026, 0.006), (c[0], c[1], c[2] - 0.045), 24 if q["detail"] else 8, 4, "glass")
        parts.append(blend.finish(blend.make(glass.transformed(T(c) @ Rz(-s * 0.06) @ T(-Vector(c))), "mirror_glass"), 60))
        stalk = tube([(s * 0.29, 0.70, 0.53), (s * 0.40, 0.75, 0.51)], 0.03, 0.006, q["tube"], major=(0, 0, 1),
                     mat="carbon")
        parts.append(blend.finish(blend.make(stalk, "mirror_stalk"), 60))
    return parts


def build_suspension(q):
    parts = []
    sides = q["tube"]

    def arm(a, b, ra=0.028, rb=0.011):
        parts.append(blend.finish(blend.make(tube([a, b], ra, rb, sides, major=(0, 0, 1), mat="carbon"), "arm"), 50))

    for s in (1, -1):
        f, r = FRONT_AXLE, REAR_AXLE
        fx = s * (HALF_WIDTH - FRONT_TYRE_W / 2 - 0.105)
        rx = s * (HALF_WIDTH - REAR_TYRE_W / 2 - 0.135)
        # Front: wishbones, push rod, track rod.
        arm((s * 0.17, 0.27, f - 0.30), (fx, 0.24, f))
        arm((s * 0.15, 0.28, f + 0.22), (fx, 0.24, f))
        arm((s * 0.20, 0.50, f - 0.24), (fx, 0.47, f - 0.01))
        arm((s * 0.15, 0.49, f + 0.20), (fx, 0.47, f - 0.01))
        arm((s * 0.16, 0.56, f - 0.06), (fx * 0.93, 0.27, f), 0.02, 0.012)
        arm((s * 0.14, 0.36, f + 0.07), (fx, 0.36, f + 0.10), 0.016, 0.009)
        # Rear: wishbones, pull rod, toe link.
        arm((s * 0.28, 0.27, r + 0.32), (rx, 0.25, r))
        arm((s * 0.24, 0.29, r - 0.25), (rx, 0.25, r))
        arm((s * 0.27, 0.50, r + 0.24), (rx, 0.50, r))
        arm((s * 0.19, 0.48, r - 0.20), (rx, 0.50, r))
        arm((s * 0.22, 0.28, r + 0.12), (rx * 0.93, 0.50, r), 0.02, 0.012)
        arm((s * 0.22, 0.40, r - 0.12), (rx, 0.40, r - 0.12), 0.016, 0.009)
        # Uprights and brake-duct drums inside the wheels (static: the wheel nodes spin, these don't).
        for name, (cx, cy, cz) in WHEELS.items():
            if (cx > 0) != (s > 0):
                continue
            front = cz > 0
            w = FRONT_TYRE_W if front else REAR_TYRE_W
            inner = cx - s * (w / 2 - 0.07)
            drum = cylinder(0.175, 0.09, 28 if q["detail"] else 10, "carbon", "x", (inner, cy, cz))
            parts.append(blend.finish(blend.make(drum, "brake_drum"), 50))
            upright = box((0.05, 0.27, 0.09), (cx - s * (w / 2 - 0.02), cy, cz), "carbon")
            parts.append(blend.finish(blend.make(upright, "upright"), 30))
    return parts


def build_rear_end(q):
    parts = []
    parts.append(blend.finish(blend.make(cylinder(0.046, 0.15, 24 if q["detail"] else 8, "rim", "z", (0, 0.43, -2.20)),
                                         "exhaust"), 50))
    parts.append(blend.finish(blend.make(cylinder(0.036, 0.004, 20 if q["detail"] else 8, "carbon", "z",
                                                  (0, 0.43, -2.2765)), "exhaust_inner"), 50))
    parts.append(_bevelled(box((0.12, 0.06, 0.03), (0, 0.30, -2.245), "light"), "rain_light", q, 0.006))
    return parts


# ---- wheels ---------------------------------------------------------------------------------------------

def tyre_profile(radius, w):
    """(r, a) from the inner bead up the -a sidewall, over the shoulder and tread, down the +a side; stripe flags."""
    shoulder = 0.046
    stripe_lo, stripe_hi = radius - 0.080, radius - 0.064

    def bulge(r):
        t = (r - RIM_R) / (radius - shoulder - RIM_R)
        return -w / 2 + 0.006 + 0.014 * (1 - t) ** 2 - 0.006 * math.sin(math.pi * t)

    side = []
    for r in (RIM_R + 0.003, RIM_R + 0.012, RIM_R + 0.028, RIM_R + 0.045, stripe_lo - 0.012):
        side.append((r, bulge(r), False))
    side.append((stripe_lo, bulge(stripe_lo), True))
    side.append((stripe_hi, bulge(stripe_hi), False))
    side.append((radius - shoulder, -w / 2, False))
    for k in range(1, 7):
        a = k / 6 * math.pi / 2
        side.append((radius - shoulder + shoulder * math.sin(a), -w / 2 + shoulder - shoulder * math.cos(a), False))
    tread = []
    for k in range(1, 6):
        a = -w / 2 + shoulder + (w - 2 * shoulder) * k / 6
        tread.append((radius - 0.003 * (2 * a / (w - 2 * shoulder)) ** 2, a, False))
    other = [(r, -a, flag) for r, a, flag in reversed(side)]
    # The stripe flag marks the span *starting* at a point; mirror it onto the other sidewall.
    for i in range(len(other) - 1):
        r0, r1 = other[i][0], other[i + 1][0]
        other[i] = (other[i][0], other[i][1], abs(r0 - stripe_hi) < 1e-9 and abs(r1 - stripe_lo) < 1e-9)
    return side + tread + other


def build_wheel(name, centre, q):
    front = centre[2] > 0
    w = FRONT_TYRE_W if front else REAR_TYRE_W
    radius = FRONT_TYRE_R if front else REAR_TYRE_R
    segs = q["wheel"]
    prof = tyre_profile(radius, w)
    m = Mesh()
    m.extend(lathe([(r, a) for r, a, _ in prof], segs, lambda i: "tyre_stripe" if prof[i][2] else "tyre", outward=-1))

    # Rim: outer flange and dished barrel (brushed metal), recessed dark cover, ten spokes, hub and centre-lock nut.
    a_out = w / 2
    rim = [(RIM_R + 0.006, a_out - 0.010), (RIM_R + 0.004, a_out - 0.003), (RIM_R - 0.008, a_out - 0.002),
           (RIM_R - 0.018, a_out - 0.012), (RIM_R - 0.024, a_out - 0.040), (RIM_R - 0.026, a_out - 0.060),
           (RIM_R - 0.026, -a_out + 0.030), (RIM_R - 0.012, -a_out + 0.012), (RIM_R + 0.006, -a_out + 0.010)]
    m.extend(lathe(rim, segs, "rim", outward=-1))
    face_a = a_out - 0.058
    m.extend(lathe([(RIM_R - 0.026, face_a), (0.07, face_a - 0.01), (0.0, face_a - 0.01)], segs // 2, "carbon",
                   outward=-1))
    m.extend(lathe([(0.0, -a_out + 0.03), (RIM_R - 0.026, -a_out + 0.03)], segs // 2, "carbon", outward=-1))
    spoke_n = 10
    for k in range(spoke_n if q["detail"] else 5):
        ang = 2 * math.pi * k / (spoke_n if q["detail"] else 5)
        outline = [(0.058, -0.019), (RIM_R - 0.03, -0.010), (RIM_R - 0.022, 0.0), (RIM_R - 0.03, 0.010), (0.058, 0.019)]
        sp = plate(outline, 0.016, origin=(face_a + 0.012, 0, 0), u=(0, 1, 0), v=(0, 0, 1), mat="rim")
        sp = sp.transformed(Rx(ang))
        m.extend(sp)
    hub = cylinder(0.068, 0.05, 32 if q["detail"] else 10, "rim", "x", (a_out - 0.04, 0, 0))
    m.extend(hub)
    nut = cylinder(0.036, 0.04, 6, "rim", "x", (a_out - 0.026, 0, 0))
    m.extend(nut)
    if centre[0] < 0:
        m = m.mirrored_x()
    m = m.transformed(T(centre))
    o = blend.make(m, name)
    blend.mark_sharp(o, 35)
    blend.set_origin(o, centre)
    return o


# ---- assembly and export --------------------------------------------------------------------------------

def nose_number_frame():
    y, slope = surface_top(CHASSIS, LODS[0]["per"], 0.0, NUMBER_NOSE_Z)
    n = Vector((0, 1, -slope)).normalized()
    return (0.0, y + 0.006, NUMBER_NOSE_Z), n


def build_car(lod):
    q = LODS[lod]
    blend.reset()
    define_materials(asset.team("mclaren")["livery"])
    body = build_body(q)
    wheels = [build_wheel(name, c, q) for name, c in WHEELS.items()]
    pos, n = nose_number_frame()
    from lib.frame import facing
    empties = [blend.empty("number_nose", pos, *facing(n, (0, 0, -1)))]
    for name, s in (("number_endplate_l", 1), ("number_endplate_r", -1)):
        empties.append(blend.empty(name, *endplate_number_frame(s)))
    empties.append(blend.empty("helmet", HELMET))
    return body, wheels, empties


def build(ctx):
    for lod, fname in ((0, "car_2026.glb"), (1, "car_2026_lod1.glb")):
        body, wheels, empties = build_car(lod)
        tris = blend.triangles(body) + sum(blend.triangles(w) for w in wheels)
        print(f"[car] LOD{lod}: body {blend.triangles(body)} + wheels {sum(blend.triangles(w) for w in wheels)}"
              f" = {tris} triangles")
        export.glb(ctx.model(fname), [body] + wheels + empties)


def _car_checks(lod):
    def extra(s):
        errs = []
        for name, c in WHEELS.items():
            t = s["nodes"].get(name, {}).get("translation")
            if t is None or max(abs(t[i] - c[i]) for i in range(3)) > 1e-3:
                errs.append(f"{name} at {t}, expected {c}")
        if lod == 0:
            bc = s["base_colours"].get("paint_top")
            if bc is None or abs(bc[0] - 1.0) > 0.01 or abs(bc[1] - 0.2159) > 0.01 or bc[2] > 0.01:
                errs.append(f"paint_top base colour {bc} is not linear papaya (1, 0.216, 0)")
        return errs
    return extra


def contracts():
    common = dict(nodes=("body",) + tuple(WHEELS) + NUMBERS + ("helmet",),
                  size={"x": (1.85, 1.91), "z": (5.4, 6.0), "y": (1.0, 1.2)}, min_y=(-0.002, 0.002))
    return [
        Contract("car_2026.glb", 120_000, materials=SLOTS, max_bytes=6 * 1024 * 1024, extra=_car_checks(0), **common),
        Contract("car_2026_lod1.glb", 30_000, materials=PAINT_SLOTS + ("carbon", "tyre", "rim", "light", "tcam"),
                 max_bytes=2 * 1024 * 1024, extra=_car_checks(1), **common),
    ]


# ---- previews ----------------------------------------------------------------------------------------------

def preview(ctx):
    team = asset.team(ctx.args.get("team", "mclaren"))
    lod = int(ctx.args.get("lod", "0"))
    body, wheels, empties = build_car(lod)
    define_materials(team["livery"], team.get("finish", "gloss"))
    for slot in SLOTS:
        materials.get(slot)  # refresh the slot colours for this team
    pv.stage()
    res = tuple(int(v) for v in ctx.args.get("res", "1280x720").split("x"))
    samples = int(ctx.args.get("samples", "64"))
    views = {
        "front34": ((4.4, 1.55, 6.6), (0.0, 0.36, 0.45), 50),
        "side": ((7.6, 0.75, 0.0), (0.0, 0.45, 0.0), 45),
        "rear34": ((-3.6, 1.7, -5.4), (0.0, 0.45, -0.4), 50),
        "top": ((0.01, 9.5, 0.0), (0.0, 0.0, 0.0), 40),
    }
    for name in ctx.args.get("views", "front34,side,rear34").split(","):
        eye, target, lens = views[name]
        path = f"{ctx.preview_dir}/car_{team['id']}_lod{lod}_{name}.png"
        pv.render(path, eye, target, lens, res, samples)
        print(f"[car] preview {path}")
