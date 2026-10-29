"""The podium set: podium.glb (<= 150k triangles).

Frame and placement match PodiumLayout (games/Redline/Redline.Presentation/Ceremony/PodiumLayout.cs) and the procedural
set in PodiumSet.cs: origin on the ground at the middle of the stage's front edge, +X to the right as the camera sees
it, +Y up, +Z towards the camera; the stage occupies -5.6 <= z <= 0. The driver figures, bottles, sprays, confetti and
spotlights stay in Godot; this is the set they perform on.

What it models: a graphite stage deck (skirted down into uneven verge) with a carpet top, aluminium nosing, red and
white LED edge strips, dark gloss fascia panels with REDLINE / ARCBOX lettering and diamond-plate stairs with rails at
both ends; three bevelled white blocks (2 left, 1 centre and highest, 3 right) with inset red numerals, a gold, silver
or bronze collar and a glowing base line; the LED media wall (its picture is Godot's
PodiumBackdrop texture on the `screen` slot) in a black frame with an LED surround; aluminium box-truss towers either
side carrying a light bar, par lamps and a vertical ARCBOX panel, joined by a header truss with its own light bar; the
front lighting truss on posts with five hanging lamps and a REDLINE banner; two confetti cannons on the header truss
(and two smaller ones on the deck).

Nodes (Godot reads them):
  podium                  every part, one mesh with the material slots below
  confetti_l, confetti_r  empties at the header cannons' muzzles (local +Z = the barrel's direction): the confetti
                          emitters
  step_1, step_2, step_3  empties at the middle of each block's top (PodiumLayout.StepTopCentre: the drivers stand
                          there)
Material slots (Godot binds them; `screen` takes the backdrop artwork, UVs 0..1 across the wall):
  stage carpet step gold silver bronze numeral screen panel brand_red brand_white steel steel_dark metal cannon
  led_white led_red led_warm
"""
import math

from mathutils import Vector

from lib import arch, blend, export, materials, preview as pv
from lib.mesh import Mesh, Rz, T, box, cylinder, merge, tube
from lib.validate import Contract

GROUP = "podium"
FILE = "podium.glb"

# PodiumLayout (keep in step with the C# constants; Redline.Presentation.Tests checks the export against them).
STAGE_W, STAGE_D, STAGE_H = 13.0, 5.6, 0.9
SKIRT = 1.6
STEP_W, STEP_D, STEP_PITCH, STEP_Z = 2.5, 2.3, 2.8, -3.1
STEP_RISE = {1: 1.0, 2: 0.65, 3: 0.35}
BACKDROP_Z, BACKDROP_W, BACKDROP_H = -5.4, 16.0, 7.2
TOWER_X = BACKDROP_W / 2 + 0.55
TRUSS_Y = STAGE_H + BACKDROP_H + 1.0
TRUSS_Z = 2.6
LAMP_XS = (-5.2, -2.8, 0.0, 2.8, 5.2)
HEADER_Y = STAGE_H + BACKDROP_H + 0.45
CANNON_X = 7.4
TRUSS_Z_BACK = BACKDROP_Z + 0.1

SLOTS = ("stage", "carpet", "step", "gold", "silver", "bronze", "numeral", "screen", "panel", "brand_red",
         "brand_white", "steel", "steel_dark", "metal", "cannon", "led_white", "led_red", "led_warm")


def step_x(step):
    return {1: 0.0, 2: -STEP_PITCH, 3: STEP_PITCH}[step]


def step_top(step):
    return STAGE_H + STEP_RISE[step]


def define_materials():
    materials.define("stage", "#1c1d23", metallic=0.3, roughness=0.42, coat=0.4, coat_roughness=0.1)
    materials.define("carpet", "#2b2c34", roughness=0.95)
    materials.define("step", "#eef0f3", roughness=0.3, coat=0.5, coat_roughness=0.08)
    materials.metal("gold", "#d4a52c", roughness=0.22, anisotropic=0.3)
    materials.metal("silver", "#c3c8cf", roughness=0.2, anisotropic=0.3)
    materials.metal("bronze", "#b8733a", roughness=0.26, anisotropic=0.3)
    materials.define("numeral", "#e10600", roughness=0.3, coat=0.6)
    materials.define("screen", "#ffffff", roughness=0.4)
    materials.define("panel", "#121219", metallic=0.2, roughness=0.25, coat=0.8, coat_roughness=0.05)
    materials.define("brand_red", "#e10600", roughness=0.3, coat=0.5)
    materials.define("brand_white", "#f4f4f4", roughness=0.3, coat=0.5)
    materials.metal("steel", "#c9ced6", roughness=0.3, anisotropic=0.4)
    materials.define("steel_dark", "#23242b", metallic=0.6, roughness=0.4)
    materials.metal("metal", "#8a8f96", roughness=0.38, anisotropic=0.2)
    materials.paint("cannon", "#c8102e")
    materials.emissive("led_white", "#fff4e0", 6.0)
    materials.emissive("led_red", "#ff1a12", 5.0)
    materials.emissive("led_warm", "#ffe7c4", 6.0)


def bx(x0, x1, y0, y1, z0, z1, mat):
    return box((x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), mat)


def part(mesh, name, bevel=0.0, segments=2, angle=35.0):
    o = blend.make(mesh, name)
    if bevel:
        blend.bevel(o, bevel, segments, angle)
    return blend.finish(o)


# ---- the stage ----------------------------------------------------------------------------------------------

def stage():
    hw = STAGE_W / 2
    deck = part(bx(-hw, hw, -SKIRT, STAGE_H - 0.03, -STAGE_D, 0, "stage"), "deck", bevel=0.012)
    m = Mesh()
    m.extend(bx(-hw + 0.04, hw - 0.04, STAGE_H - 0.03, STAGE_H, -STAGE_D + 0.04, -0.06, "carpet"))
    # Aluminium nosing round the deck's top edge (front and both ends).
    m.extend(bx(-hw - 0.004, hw + 0.004, STAGE_H - 0.05, STAGE_H + 0.004, -0.06, 0.006, "metal"))
    for s in (-1, 1):
        m.extend(bx(s * hw - 0.06 if s > 0 else -hw - 0.006, s * hw + 0.006 if s > 0 else -hw + 0.06,
                    STAGE_H - 0.05, STAGE_H + 0.004, -STAGE_D, -0.06, "metal"))
    # LED edge strips: red under the nosing, a soft white line along the foot; both ends too.
    m.extend(bx(-hw, hw, STAGE_H - 0.13, STAGE_H - 0.08, 0, 0.018, "led_red"))
    m.extend(bx(-hw, hw, 0.03, 0.06, 0, 0.018, "led_white"))
    for s in (-1, 1):
        x0, x1 = (hw, hw + 0.018) if s > 0 else (-hw - 0.018, -hw)
        m.extend(bx(x0, x1, STAGE_H - 0.13, STAGE_H - 0.08, -STAGE_D, 0, "led_red"))
    # Fascia: five dark gloss panels with lettering.
    widths = [2.5, 2.5, 3.0, 2.5, 2.5]
    x = -hw + 0.02
    gap = 0.04
    centres = []
    for w in widths:
        m.extend(bx(x + gap / 2, x + w - gap / 2, 0.1, STAGE_H - 0.16, 0, 0.012, "panel"))
        centres.append(x + w / 2)
        x += w
    m.extend(arch.text("REDLINE", 0.36, 0.01, "brand_white").transformed(T(centres[2], 0.3, 0.012)))
    for c in (centres[0], centres[4]):
        m.extend(arch.text("ARCBOX", 0.2, 0.01, "brand_red").transformed(T(c, 0.38, 0.012)))
    return [deck, part(m, "deck_trim")]


def blocks():
    out = []
    lips = {1: "gold", 2: "silver", 3: "bronze"}
    for s in (1, 2, 3):
        x, top = step_x(s), step_top(s)
        rise = top - STAGE_H
        hw, z0, z1 = STEP_W / 2, STEP_Z - STEP_D / 2, STEP_Z + STEP_D / 2
        block = blend.make(bx(x - hw, x + hw, STAGE_H - 0.02, top, z0, z1, "step"), f"block{s}")
        blend.bevel(block, 0.035, 3, 30)
        blend.apply(block)
        face = rise - 0.115  # the front between the glowing base line and the medal collar
        h = min(0.58 * rise, 0.8 * face)
        y0 = STAGE_H + 0.03 + face / 2 - h / 2
        numeral = arch.text(str(s), h, 0.08, "numeral").transformed(T(x, y0, z1 - 0.04))
        cutter = blend.make(numeral, f"numeral{s}")
        blend.boolean(block, cutter)
        blend.finish(block)
        blend.delete(cutter)
        out.append(block)
        m = Mesh()
        e = 0.012
        m.extend(bx(x - hw - e, x + hw + e, top - 0.085, top - 0.022, z0 - e, z1 + e, lips[s]))
        m.extend(bx(x - hw - 0.006, x + hw + 0.006, STAGE_H, STAGE_H + 0.03, z0 - 0.006, z1 + 0.006, "led_white"))
        out.append(part(m, f"block{s}_trim", bevel=0.004, segments=1))
    return out


def stairs():
    """Diamond-plate flights up to the deck at both ends, with rails."""
    m = Mesh()
    n, run, rise = 5, 0.3, STAGE_H / 5
    za, zb = -3.0, -1.2
    for s in (-1, 1):
        x_bottom = s * (STAGE_W / 2 + n * run)
        # Local X = width (world Z), local Z = climbing direction (towards the deck), Y up.
        climb = Vector((-s, 0, 0))
        right = Vector((0, 1, 0)).cross(climb)  # right-handed: X = Y x Z
        mat = arch.frame((x_bottom, 0, 0), right, (0, 1, 0), climb)
        prof = arch.stair_profile(n, run, rise, base=-SKIRT)
        lo, hi = sorted((za * right.z, zb * right.z))
        flight = arch.prism(prof, lo, hi, "metal").transformed(mat)
        m.extend(flight)
        for z in (za, zb):
            p0 = Vector((x_bottom + s * 0.05, 0.95, z))
            p1 = Vector((s * (STAGE_W / 2 + 0.1), STAGE_H + 0.95, z))
            m.extend(tube([p0, p1], 0.022, sides=8, mat="steel"))
            for p in (p0, p1, (p0 + p1) / 2):
                base_y = min(STAGE_H, (math.floor((abs(x_bottom) - abs(p.x)) / run) + 1) * rise)
                m.extend(tube([(p.x, base_y, z), (p.x, p.y, z)], 0.02, sides=8, mat="steel"))
    return part(m, "stairs", bevel=0.004, segments=1)


# ---- backdrop, trusses and lamps -------------------------------------------------------------------------

def backdrop():
    hw = BACKDROP_W / 2
    top = STAGE_H + BACKDROP_H
    m = Mesh()
    m.extend(bx(-hw - 0.25, hw + 0.25, -SKIRT, top + 0.25, BACKDROP_Z - 0.35, BACKDROP_Z - 0.02, "stage"))
    scr = Mesh()
    z = BACKDROP_Z + 0.02
    v = [scr.vert(p) for p in ((-hw, STAGE_H, z), (hw, STAGE_H, z), (hw, top, z), (-hw, top, z))]
    scr.face(v, "screen", uv=((0, 0), (1, 0), (1, 1), (0, 1)), smooth=False)
    m.extend(scr)
    m.extend(bx(-hw - 0.1, hw + 0.1, top + 0.07, top + 0.17, BACKDROP_Z - 0.02, BACKDROP_Z + 0.05, "led_white"))
    for s in (-1, 1):
        x0, x1 = sorted((s * (hw + 0.05), s * (hw + 0.15)))
        m.extend(bx(x0, x1, STAGE_H - 0.05, top + 0.17, BACKDROP_Z - 0.02, BACKDROP_Z + 0.05, "led_white"))
    # Uplights on the deck between the blocks and the wall, washing the wall.
    for x in (-5.4, -1.4, 1.4, 5.4):
        m.extend(bx(x - 0.16, x + 0.16, STAGE_H, STAGE_H + 0.05, BACKDROP_Z + 0.25, BACKDROP_Z + 0.6, "steel_dark"))
        m.extend(lamp((x, STAGE_H + 0.2, BACKDROP_Z + 0.42), (0, 1, -0.45), 0.24, 0.12))
    return part(m, "backdrop")


def lamp(pos, aim, length=0.42, radius=0.17):
    """A par can: housing, lens and a yoke, its lens facing along `aim`."""
    m = Mesh()
    d = Vector(aim).normalized()
    p = Vector(pos)
    m.extend(tube([p - d * length * 0.5, p + d * length * 0.5], radius, sides=16, mat="steel_dark"))
    m.extend(tube([p + d * length * 0.5, p + d * (length * 0.5 + 0.012)], radius * 0.86, sides=16, mat="led_warm"))
    m.extend(tube([p + d * (length * 0.5 - 0.02), p + d * (length * 0.5 + 0.03)], radius * 1.04, sides=16,
                  mat="steel_dark", caps=True))
    return m


def towers():
    out = Mesh()
    top = STAGE_H + BACKDROP_H
    for s in (-1, 1):
        x = s * TOWER_X
        out.extend(arch.truss((x, -SKIRT, TRUSS_Z_BACK), (x, top + 1.3, TRUSS_Z_BACK), 0.5, 0.5, 0.55, 0.032, 0.02,
                              "steel", up=(0, 0, 1)))
        out.extend(bx(x - 0.45, x + 0.45, -0.03, 0.03, TRUSS_Z_BACK - 0.45, TRUSS_Z_BACK + 0.45, "steel_dark"))
        zf = TRUSS_Z_BACK + 0.27
        # Light bar on the inner front edge.
        xi = x - s * 0.2
        out.extend(bx(xi - 0.05, xi + 0.05, STAGE_H + 0.1, top, zf, zf + 0.06, "steel_dark"))
        out.extend(bx(xi - 0.03, xi + 0.03, STAGE_H + 0.15, top - 0.05, zf + 0.06, zf + 0.085, "led_white"))
        # Par lamps up the upper half.
        for i in range(7):
            out.extend(lamp((x + s * 0.06, 4.5 + i * 0.6, zf + 0.25), (0, 0, 1), 0.3, 0.16))
        # A vertical ARCBOX panel below them.
        out.extend(bx(x - 0.28 + s * 0.06, x + 0.28 + s * 0.06, STAGE_H + 0.1, 4.05, zf, zf + 0.05, "panel"))
        word = arch.text("ARCBOX", 0.3, 0.012, "brand_white").transformed(T(x + s * 0.06 + 0.13, (STAGE_H + 0.1 + 4.05) / 2, zf + 0.05) @ Rz(math.pi / 2))
        out.extend(word)
    # Header truss over the wall, with its own light bar.
    out.extend(arch.truss((-TOWER_X - 0.25, HEADER_Y + 0.3, TRUSS_Z_BACK), (TOWER_X + 0.25, HEADER_Y + 0.3, TRUSS_Z_BACK),
                          0.5, 0.5, 0.55, 0.032, 0.02, "steel", up=(0, 0, 1)))
    zf = TRUSS_Z_BACK + 0.27
    out.extend(bx(-TOWER_X + 0.4, TOWER_X - 0.4, HEADER_Y + 0.22, HEADER_Y + 0.38, zf, zf + 0.05, "steel_dark"))
    out.extend(bx(-TOWER_X + 0.45, TOWER_X - 0.45, HEADER_Y + 0.26, HEADER_Y + 0.34, zf + 0.05, zf + 0.07, "led_white"))
    return part(out, "towers")


def front_truss():
    m = Mesh()
    y, z = TRUSS_Y, TRUSS_Z
    m.extend(arch.truss((-TOWER_X - 0.6, y, z), (TOWER_X + 0.6, y, z), 0.45, 0.45, 0.5, 0.03, 0.019, "steel",
                        up=(0, 1, 0)))
    for s in (-1, 1):
        x = s * TOWER_X
        m.extend(arch.truss((x, -SKIRT, z), (x, y + 0.25, z), 0.4, 0.4, 0.5, 0.028, 0.018, "steel", up=(0, 0, 1)))
        m.extend(bx(x - 0.4, x + 0.4, -0.03, 0.03, z - 0.4, z + 0.4, "steel_dark"))
        m.extend(arch.truss((x, y, z - 0.2), (x, y, TRUSS_Z_BACK + 0.25), 0.4, 0.4, 0.5, 0.028, 0.018, "steel",
                            up=(0, 1, 0)))
    # Five hanging lamps aimed at the steps (as PodiumSet's spotlights).
    for x in LAMP_XS:
        target = Vector((x * 1.05, STAGE_H + 1.6, STEP_Z))
        pos = Vector((x, y - 0.6, z - 0.25))
        m.extend(bx(x - 0.04, x + 0.04, y - 0.42, y - 0.2, z - 0.29, z - 0.21, "steel_dark"))
        m.extend(bx(x - 0.22, x + 0.22, y - 0.45, y - 0.41, z - 0.31, z - 0.19, "steel_dark"))
        for s in (-1, 1):
            m.extend(bx(x + s * 0.2 - 0.02, x + s * 0.2 + 0.02, y - 0.62, y - 0.42, z - 0.28, z - 0.22, "steel_dark"))
        m.extend(lamp(pos, target - pos))
    # REDLINE banner over the truss.
    bz = z + 0.05
    m.extend(bx(-6.2, 6.2, y + 0.32, y + 1.12, bz - 0.03, bz + 0.03, "panel"))
    m.extend(bx(-6.2, 6.2, y + 0.3, y + 0.34, bz - 0.035, bz + 0.035, "led_red"))
    m.extend(arch.text("REDLINE", 0.5, 0.015, "brand_red").transformed(T(0, y + 0.47, bz + 0.03)))
    for x in (-4.6, 4.6):
        m.extend(arch.text("ARCBOX", 0.26, 0.015, "brand_white").transformed(T(x, y + 0.6, bz + 0.03)))
    for x in (-5.6, 0, 5.6):
        m.extend(bx(x - 0.03, x + 0.03, y + 0.2, y + 0.32, bz - 0.03, bz + 0.03, "steel_dark"))
    return part(m, "front_truss")


def cannon(base, aim, scale=1.0):
    """A confetti cannon: clamp plate, swivel, a red barrel along `aim` with a dark muzzle ring. Returns the mesh and the
    muzzle point."""
    d = Vector(aim).normalized()
    b = Vector(base)
    m = Mesh()
    m.extend(box((0.34 * scale, 0.06 * scale, 0.34 * scale), (b.x, b.y + 0.03 * scale, b.z), "steel_dark"))
    m.extend(cylinder(0.07 * scale, 0.14 * scale, 12, "steel_dark", "y", (b.x, b.y + 0.12 * scale, b.z)))
    p0 = b + Vector((0, 0.2 * scale, 0))
    length = 0.62 * scale
    m.extend(tube([p0 - d * 0.08 * scale, p0 + d * length], 0.1 * scale, sides=16, mat="cannon"))
    m.extend(tube([p0 + d * (length - 0.06 * scale), p0 + d * (length + 0.03 * scale)], 0.118 * scale, sides=16,
                  mat="steel_dark"))
    m.extend(tube([p0 + d * 0.05 * scale, p0 + d * 0.16 * scale], 0.112 * scale, sides=16, mat="steel_dark"))
    return m, p0 + d * (length + 0.03 * scale)


def cannons():
    m = Mesh()
    empties = []
    for name, s in (("confetti_l", -1), ("confetti_r", 1)):
        aim = Vector((-s * 0.35, 1, 0.55)).normalized()
        mesh, muzzle = cannon((s * CANNON_X, HEADER_Y + 0.55, TRUSS_Z_BACK), aim)
        m.extend(mesh)
        from lib.frame import facing
        empties.append(blend.empty(name, tuple(muzzle), *facing(aim, (0, 0, 1))))
    for s in (-1, 1):
        mesh, _ = cannon((s * 6.05, STAGE_H, -0.45), Vector((-s * 0.25, 1, 0.2)), 0.8)
        m.extend(mesh)
    return part(m, "cannons", bevel=0.004, segments=1), empties


# ---- assembly ------------------------------------------------------------------------------------------------

def build_set():
    blend.reset()
    define_materials()
    parts = stage() + blocks() + [stairs(), backdrop(), towers(), front_truss()]
    c, empties = cannons()
    parts.append(c)
    body = blend.join(parts, "podium")
    for s in (1, 2, 3):
        empties.append(blend.empty(f"step_{s}", (step_x(s), step_top(s), STEP_Z)))
    return body, empties


def build(ctx):
    body, empties = build_set()
    print(f"[podium] {blend.triangles(body)} triangles")
    export.glb(ctx.model(FILE), [body] + empties)


def _checks(s):
    errs = []
    for name, sx in (("confetti_l", -1), ("confetti_r", 1)):
        t = s["nodes"].get(name, {}).get("translation")
        if t is None or not (6.5 < sx * t[0] < 8.5 and 8.5 < t[1] < 10.5):
            errs.append(f"{name} at {t}: not on the header truss")
    return errs


def contracts():
    return [Contract(FILE, 150_000, nodes=("podium", "confetti_l", "confetti_r", "step_1", "step_2", "step_3"),
                     materials=SLOTS,
                     size={"x": (17.5, 19.5), "y": (10.5, 13.5), "z": (8.0, 9.5)}, min_y=(-SKIRT - 0.02, -SKIRT + 0.01),
                     max_bytes=4 * 1024 * 1024, extra=_checks)]


def preview(ctx):
    build_set()
    pv.stage(ground_hex="#55575c")
    res = tuple(int(v) for v in ctx.args.get("res", "1280x720").split("x"))
    samples = int(ctx.args.get("samples", "48"))
    views = {
        "front": ((0.0, 3.2, 17.0), (0.0, 4.0, -2.0), 32),
        "three_quarter": ((10.5, 4.5, 12.0), (0.0, 3.5, -2.5), 35),
        "steps": ((-2.0, 2.4, 4.5), (0.0, 1.4, -2.5), 35),
    }
    for name in ctx.args.get("views", "front,three_quarter,steps").split(","):
        eye, target, lens = views[name]
        path = f"{ctx.preview_dir}/podium_{name}.png"
        pv.render(path, eye, target, lens, res, samples)
        print(f"[podium] preview {path}")
