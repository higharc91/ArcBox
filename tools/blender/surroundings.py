"""COTA's surroundings: surroundings/*.glb. The TV places them from Redline.Presentation.Course.Surroundings (the
plan) and binds every slot to its own material (World/Surroundings.cs).

  amphitheatre.glb
      The amphitheatre beside the Observation Tower. Frame: origin at the middle of the stage's front edge, on the
      highest ground under the footprint (Godot puts it there), +Z towards the audience, +Y up. Footprint
      -42 <= x <= 42, -16 <= z <= 48 (Surroundings.AmphiHalfWidth/AmphiBack/AmphiRim); everything that meets the
      ground is skirted down to y = -8, so lower ground round it never shows a gap. A lawn bowl of eleven concentric
      terraces (concrete risers, three aisles) fanning out +/-50 degrees from behind the stage, earth berms round it,
      a paved floor, the stage deck with a back wall carrying REDLINE/ARCBOX lettering, two LED screens on frames, and
      the covered stage: a white tensile canopy on four masts with tie cables and a lighting truss; a mixing riser on
      the middle aisle. Nodes: amphitheatre.
  plaza_props.glb
      The Grand Plaza's furniture, one node (one mesh, origin on the ground) each, drawn as MultiMeshes:
      light_pole (8 m, LED head, red banner), planter (concrete box with a small tree), kiosk (food kiosk: counter and
      red awning towards +Z, REDLINE sign on the roof), flag_pole (12 m, red REDLINE flag).
  tents.glb
      Hospitality tents: marquee (21 x 11 m clear-span marquee: aluminium frame, glazed walls over a canvas dado, a
      gable roof, a red fascia with REDLINE/ARCBOX lettering, a deck) and pagoda (6 x 6 m peaked pagoda with a red
      valance). Length along X, origin on the ground at the middle.
  parked_cars.glb
      Low-poly parked cars for MultiMesh (about 150 triangles each), +Z forward, origin on the ground at the middle:
      sedan, suv, pickup, hatch. Slots car_paint (Godot tints it per instance), car_glass, car_dark.
  shrubs.glb
      Texas scrub at unit scale (footprint radius ~1): shrub_round, shrub_juniper, shrub_low. Slot shrub (tinted per
      instance).
  groves.glb, far_clumps.glb
      Tree clumps at unit scale (radius ~1) for MultiMesh: grove_oak and grove_cedar (a few crowns each, ~300
      triangles) for treelines and creek lines, far_clump (one 36-triangle dome) for distant clumps out to the
      horizon. Slots grove (tinted per instance), bark.
  skyline.glb
      A generic downtown silhouette at real scale (towers 25-215 m over about 2 km, +Z towards the viewer), placed
      far away, scaled down and fogged by the TV. Slots skyline, skyline_glass. No real building is copied.

No logos or trademarks: the only lettering is REDLINE and ARCBOX.
"""
import math
import random

from mathutils import Vector

from lib import arch, blend, export, materials, preview as pv
from lib.mesh import Mesh, Ry, Rz, T, box, cylinder, tube
from lib.validate import Contract

GROUP = "surroundings"

AMPHI_HALF_WIDTH, AMPHI_BACK, AMPHI_RIM = 42.0, -16.0, 48.0
SKIRT = -8.0
FOCUS = Vector((0.0, 0.0, -4.0))
FAN = math.radians(50)
TERRACES = 11
R0, RSTEP, RISE = 10.0, 3.0, 0.5
AISLES = (-26.0, 0.0, 26.0)

PROPS = ("light_pole", "planter", "kiosk", "flag_pole")
TENTS = ("marquee", "pagoda")
CARS = ("sedan", "suv", "pickup", "hatch")
SHRUBS = ("shrub_round", "shrub_juniper", "shrub_low")
GROVES = ("grove_oak", "grove_cedar")

AMPHI_SLOTS = ("lawn", "concrete", "paving", "stage", "panel", "brand_white", "screen", "canopy", "steel",
               "steel_dark", "led_warm")


def define_materials():
    materials.define("lawn", "#6f8a3c", roughness=0.95)
    materials.define("concrete", "#c4c1b9", roughness=0.9)
    materials.define("paving", "#cfc6b6", roughness=0.85)
    materials.define("stage", "#1c1d23", metallic=0.3, roughness=0.45)
    materials.define("panel", "#16171c", metallic=0.2, roughness=0.3, coat=0.6)
    materials.define("brand_white", "#f4f4f4", roughness=0.35, coat=0.4)
    materials.emissive("screen", "#2a4f8a", 1.5)
    materials.define("canopy", "#f2f2ee", roughness=0.6)
    materials.define("steel", "#dcdfe3", metallic=0.3, roughness=0.4)
    materials.define("steel_dark", "#383b42", metallic=0.5, roughness=0.42)
    materials.emissive("led_warm", "#ffe7c4", 5.0)
    materials.emissive("led_white", "#f4f7ff", 4.0)
    materials.paint("red", "#d0101c")
    materials.define("canvas", "#f4f3ef", roughness=0.7)
    materials.define("glass", "#2a3b4f", metallic=0.6, roughness=0.06)
    materials.define("deck", "#8a6a48", roughness=0.8)
    materials.define("kiosk", "#eeeeea", roughness=0.5)
    materials.define("bark", "#4a3b2c", roughness=0.95)
    materials.define("shrub", "#5d6b33", roughness=0.9)
    materials.define("grove", "#3f5228", roughness=0.9)
    materials.paint("car_paint", "#8a8f99")
    materials.define("car_glass", "#141a22", metallic=0.6, roughness=0.08)
    materials.define("car_dark", "#1b1c1f", roughness=0.8)
    materials.define("skyline", "#a9b0ba", roughness=0.7)
    materials.define("skyline_glass", "#4d5b6c", metallic=0.5, roughness=0.15)


# ---- helpers ------------------------------------------------------------------------------------------------

def facing(m, pts, slot, want, uv=None, smooth=False):
    """Adds the polygon `pts` with its front towards `want` (a direction)."""
    idx = [m.vert(p) for p in pts]
    m.face(idx, slot, uv=uv, smooth=smooth)
    if m.face_normal(m.faces[-1]).dot(Vector(want)) < 0:
        m.faces[-1] = m.faces[-1][::-1]
        if m.uvs[-1]:
            m.uvs[-1] = m.uvs[-1][::-1]
    return m


def both(m, pts, slot, want, gap=0.02):
    """A thin membrane: the polygon facing `want` and its back, `gap` behind it (so the two never z-fight)."""
    facing(m, pts, slot, want)
    back = Vector(want).normalized() * gap
    facing(m, [Vector(p) - back for p in pts], slot, -Vector(want))


def bx(x0, x1, y0, y1, z0, z1, mat):
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))
    z0, z1 = sorted((z0, z1))
    return box((x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), mat)


def skirt(m, pts, top_y, slot, outward_fn):
    """Vertical faces under the polyline `pts` (at their own heights, or top_y) down to SKIRT."""
    for a, b in zip(pts, pts[1:]):
        ya = a.y if top_y is None else top_y
        yb = b.y if top_y is None else top_y
        quad = [Vector((a.x, ya, a.z)), Vector((b.x, yb, b.z)), Vector((b.x, SKIRT, b.z)), Vector((a.x, SKIRT, a.z))]
        facing(m, quad, slot, outward_fn((a + b) / 2))


def polar(r, a, y):
    return Vector((FOCUS.x + r * math.sin(a), y, FOCUS.z + r * math.cos(a)))


def part(mesh, name, sharp=40.0):
    return blend.finish(blend.make(mesh, name), sharp)


# ---- the amphitheatre -------------------------------------------------------------------------------------

def _angles():
    out = set()
    steps = 20
    for i in range(steps + 1):
        out.add(round(-FAN + 2 * FAN * i / steps, 6))
    for a in AISLES:
        for d in (-2.0, 2.0):
            out.add(round(math.radians(a + d), 6))
    return sorted(out)


def _in_aisle(a0, a1):
    mid = math.degrees((a0 + a1) / 2)
    return any(abs(mid - a) < 2.0 for a in AISLES)


def amphitheatre():
    m = Mesh()
    angles = _angles()
    radii = [R0 + RSTEP * k for k in range(TERRACES + 1)]
    top = RISE * TERRACES
    # Terraces: lawn treads (concrete in the aisles) and concrete risers facing the stage.
    for k in range(TERRACES):
        y = RISE * (k + 1)
        r0, r1 = radii[k], radii[k + 1]
        for a0, a1 in zip(angles, angles[1:]):
            slot = "concrete" if _in_aisle(a0, a1) else "lawn"
            facing(m, [polar(r0, a0, y), polar(r0, a1, y), polar(r1, a1, y), polar(r1, a0, y)], slot, (0, 1, 0),
                   uv=[(r0, a0 * r0), (r0, a1 * r0), (r1, a1 * r1), (r1, a0 * r1)])
            mid = (a0 + a1) / 2
            inward = (-math.sin(mid), 0, -math.cos(mid))
            facing(m, [polar(r0, a0, y - RISE), polar(r0, a1, y - RISE), polar(r0, a1, y), polar(r0, a0, y)],
                   "concrete", inward)
    # Rim walkway and the back berm (wider than the fan, so it meets the side berms), then its skirt.
    wide = _arc(-FAN - math.radians(6), FAN + math.radians(6), 28)
    r_rim, r_walk, r_out = radii[-1], radii[-1] + 2.0, radii[-1] + 7.5
    for a0, a1 in zip(wide, wide[1:]):
        facing(m, [polar(r_rim, a0, top), polar(r_rim, a1, top), polar(r_walk, a1, top), polar(r_walk, a0, top)],
               "concrete", (0, 1, 0))
        facing(m, [polar(r_walk, a0, top), polar(r_walk, a1, top), polar(r_out, a1, -3.0), polar(r_out, a0, -3.0)],
               "lawn", (0, 1, 0))
    skirt(m, [polar(r_out, a, -3.0) for a in wide], None, "lawn",
          lambda c: (c.x - FOCUS.x, 0, c.z - FOCUS.z))
    # Side berms along the fan's straight edges: each terrace's edge slopes 7 m outwards down to -3.
    for side in (-1, 1):
        a = side * FAN
        out = Vector((side * math.cos(a), 0, -side * math.sin(a)))
        outer = []
        for k in range(TERRACES):
            y = RISE * (k + 1)
            e0, e1 = polar(radii[k], a, y), polar(radii[k + 1], a, y)
            o0, o1 = polar(radii[k], a, -3.0) + out * 7, polar(radii[k + 1], a, -3.0) + out * 7
            facing(m, [e0, e1, o1, o0], "lawn", (0, 1, 0))
            # The step between this terrace's edge and the one below.
            facing(m, [polar(radii[k], a, y - RISE), e0, o0], "lawn", -Vector((math.sin(a), 0, math.cos(a))))
            outer += [o0, o1]
        facing(m, [polar(radii[-1], a, top), polar(r_walk + 0.5, a, top), polar(radii[-1], a, -3.0) + out * 7],
               "lawn", (0, 1, 0))
        skirt(m, outer, None, "lawn", lambda c, o=out: o)
        # The front end of the berm, down to the floor.
        e = polar(radii[0], a, RISE)
        facing(m, [polar(radii[0], a, 0.0), e, polar(radii[0], a, -3.0) + out * 7],
               "lawn", -Vector((math.sin(a), 0, math.cos(a))))
    # Floor: paved, from behind the stage to the first riser; skirted round its open edges.
    fx, fz0 = 24.0, AMPHI_BACK
    e = polar(R0, FAN, 0.0)
    arc = [polar(R0, a, 0.0) for a in _arc(FAN, -FAN, 16)]
    floor = [Vector((-fx, 0, fz0)), Vector((fx, 0, fz0)), Vector((fx, 0, e.z))] + arc + [Vector((-fx, 0, e.z))]
    floor = [p + Vector((0, 0.05, 0)) for p in floor]
    facing(m, floor, "paving", (0, 1, 0), uv=[(p.x, p.z) for p in floor])
    skirt(m, [Vector((-fx, 0, e.z)), Vector((-fx, 0, fz0)), Vector((fx, 0, fz0)), Vector((fx, 0, e.z)),
              Vector((e.x, 0, e.z))], 0.0, "concrete", lambda c: (c.x, 0, c.z - 0.5 * (fz0 + e.z)))
    skirt(m, [Vector((-e.x, 0, e.z)), Vector((-fx, 0, e.z))], 0.0, "concrete", lambda c: (0, 0, 1))
    # Stage: deck, front, stairs, back wall with lettering, screens.
    m.extend(bx(-13, 13, 0, 1.6, -13, 0, "stage"))
    m.extend(bx(-13.2, 13.2, 1.6, 1.7, -13.2, 0.15, "steel_dark"))
    for sx in (-1, 1):
        for k in range(5):
            m.extend(bx(sx * 13, sx * (13 + 0.3 * (k + 1)), 0, 1.6 - 0.32 * k, -6, -3.6, "steel_dark"))
    m.extend(bx(-15.5, 15.5, 0, 11, -14.6, -13.2, "panel"))
    m.extend(arch.text("REDLINE", 2.0, 0.08, "brand_white").transformed(T(0, 7.2, -13.2)))
    m.extend(arch.text("ARCBOX", 0.9, 0.06, "brand_white").transformed(T(0, 5.3, -13.2)))
    for sx in (-1, 1):
        x = sx * 20
        m.extend(bx(x - 3.8, x + 3.8, 3.6, 8.4, -3.3, -2.9, "steel_dark"))
        m.extend(bx(x - 3.5, x + 3.5, 3.9, 8.1, -2.9, -2.8, "screen"))
        for px in (-2.6, 2.6):
            m.extend(bx(x + px - 0.15, x + px + 0.15, 0, 3.6, -3.25, -2.95, "steel_dark"))
    # Canopy: a saddle-shaped white membrane rising towards the audience, on four masts with tie cables.
    m.extend(_canopy())
    # Lighting truss under the canopy, lamps hanging from it.
    m.extend(arch.truss((-15, 10.0, -5), (15, 10.0, -5), 0.6, 0.6, 1.5, 0.05, 0.035, "steel", chord_sides=6))
    for k in range(9):
        x = -12 + 3 * k
        m.extend(bx(x - 0.22, x + 0.22, 9.2, 9.65, -5.25, -4.75, "steel_dark"))
        m.extend(bx(x - 0.16, x + 0.16, 9.18, 9.22, -5.19, -4.81, "led_warm"))
    # Mixing riser on the middle aisle.
    fy = RISE * 4
    p = polar(R0 + RSTEP * 4.5, 0, fy)
    m.extend(bx(p.x - 3, p.x + 3, fy, fy + 1.0, p.z - 2, p.z + 2, "panel"))
    m.extend(bx(p.x - 3, p.x + 3, fy + 3.0, fy + 3.2, p.z - 2.2, p.z + 2.2, "canopy"))
    for dx in (-2.8, 2.8):
        for dz in (-1.9, 1.9):
            m.extend(bx(p.x + dx - 0.06, p.x + dx + 0.06, fy, fy + 3.0, p.z + dz - 0.06, p.z + dz + 0.06, "steel"))
    return part(m, "amphitheatre", sharp=35)


def _arc(a0, a1, n):
    return [a0 + (a1 - a0) * i / n for i in range(n + 1)]


def _canopy():
    m = Mesh()
    nx, nz = 12, 8
    x0, x1, z0, z1 = -18.0, 18.0, -16.0, 4.0

    def y(x, z):
        t = (z - z0) / (z1 - z0)
        u = x / x1
        return 11.8 + 4.2 * t ** 1.3 + 1.5 * u * u - 1.2 * math.sin(math.pi * t) * (1 - u * u)

    grid = [[Vector((x0 + (x1 - x0) * i / nx, 0, z0 + (z1 - z0) * j / nz)) for j in range(nz + 1)] for i in range(nx + 1)]
    for col in grid:
        for p in col:
            p.y = y(p.x, p.z)
    for i in range(nx):
        for j in range(nz):
            q = [grid[i][j], grid[i + 1][j], grid[i + 1][j + 1], grid[i][j + 1]]
            both(m, q, "canopy", (0, 1, 0))
    # Edge cable (a slim tube round the rim) and four masts with ties to the corners.
    rim = [grid[i][0] for i in range(nx + 1)] + [grid[nx][j] for j in range(1, nz + 1)] + \
        [grid[i][nz] for i in range(nx - 1, -1, -1)] + [grid[0][j] for j in range(nz - 1, 0, -1)]
    m.extend(arch.ring([tuple(p) for p in rim], 0.08, 6, "steel"))
    for cx, cz in ((x0, z0), (x1, z0), (x0, z1), (x1, z1)):
        corner = Vector((cx, y(cx, cz), cz))
        base = Vector((cx * 1.15, -1.0, cz + (2.5 if cz > 0 else 1.0)))
        tip = Vector((base.x, corner.y + 4.0, base.z))
        m.extend(tube([tuple(base), tuple(tip)], 0.32, sides=10, mat="steel"))
        m.extend(tube([tuple(tip), tuple(corner)], 0.05, sides=5, mat="steel_dark"))
        anchor = (tip.x * 1.25, -0.5, tip.z + 4) if cz > 0 else (tip.x * 1.35, -0.5, tip.z)
        m.extend(tube([tuple(tip), anchor], 0.05, sides=5, mat="steel_dark"))
    return m


# ---- plaza props ------------------------------------------------------------------------------------------

def light_pole():
    m = Mesh()
    m.extend(cylinder(0.22, 0.6, 10, "concrete", "y", (0, 0.1, 0)))
    m.extend(cylinder(0.09, 8.0, 8, "steel_dark", "y", (0, 4.2, 0)))
    m.extend(bx(-0.08, 0.08, 7.9, 8.0, -0.05, 1.3, "steel_dark"))
    m.extend(bx(-0.25, 0.25, 7.75, 7.95, 0.7, 1.6, "steel_dark"))
    m.extend(bx(-0.2, 0.2, 7.73, 7.75, 0.8, 1.5, "led_white"))
    m.extend(bx(-0.02, 0.02, 4.6, 6.6, 0.1, 0.8, "red"))
    return part(m, "light_pole")


def planter():
    m = Mesh()
    m.extend(bx(-1.2, 1.2, -0.3, 0.65, -1.2, 1.2, "concrete"))
    m.extend(bx(-1.05, 1.05, 0.55, 0.62, -1.05, 1.05, "deck"))
    m.extend(tube([(0, 0.5, 0), (0.05, 1.6, 0.02), (0.1, 2.3, 0)], 0.09, sides=6, mat="bark"))
    for i, (c, r) in enumerate((((0, 2.9, 0), (1.1, 0.75, 1.1)), ((0.5, 2.6, 0.3), (0.7, 0.5, 0.7)),
                                ((-0.5, 2.65, -0.3), (0.7, 0.5, 0.7)))):
        m.extend(arch.blob(c, r, 300 + i, "shrub", segs=8, rings=5, lumps=0.2, freq=2.0, flat_bottom=0.3))
    return part(m, "planter", sharp=60)


def kiosk():
    m = Mesh()
    w, d, h = 4.6, 3.2, 2.7
    m.extend(bx(-w / 2, w / 2, -0.3, h, -d / 2, d / 2, "kiosk"))
    # Counter opening (a dark recess) and counter on the +Z face.
    m.extend(bx(-1.8, 1.8, 1.0, 2.2, d / 2 - 0.02, d / 2 + 0.01, "panel"))
    m.extend(bx(-1.9, 1.9, 0.95, 1.05, d / 2, d / 2 + 0.45, "steel"))
    # Red awning sloping down over the counter.
    aw = [Vector((-2.2, 2.55, d / 2)), Vector((2.2, 2.55, d / 2)), Vector((2.2, 2.15, d / 2 + 1.3)),
          Vector((-2.2, 2.15, d / 2 + 1.3))]
    both(m, aw, "red", (0, 1, 0.3))
    m.extend(bx(-2.2, 2.2, 1.95, 2.15, d / 2 + 1.28, d / 2 + 1.32, "red"))
    # Roof sign: a red box with REDLINE on both long sides.
    m.extend(bx(-2.0, 2.0, h, h + 0.7, -0.25, 0.25, "red"))
    m.extend(arch.text("REDLINE", 0.38, 0.02, "brand_white").transformed(T(0, h + 0.17, 0.25)))
    m.extend(arch.text("REDLINE", 0.38, 0.02, "brand_white").transformed(T(0, h + 0.17, -0.25) @ Ry(math.pi)))
    return part(m, "kiosk")


def flag_pole():
    m = Mesh()
    m.extend(cylinder(0.25, 0.4, 10, "concrete", "y", (0, 0.0, 0)))
    m.extend(cylinder(0.06, 12.0, 8, "steel", "y", (0, 6.0, 0)))
    m.extend(cylinder(0.1, 0.12, 8, "steel", "y", (0, 12.05, 0)))
    # A vertical banner flag on a top arm: red cloth with REDLINE down it on both faces.
    m.extend(bx(0.0, 1.6, 11.6, 11.68, -0.03, 0.03, "steel"))
    m.extend(bx(0.06, 1.56, 7.0, 11.6, -0.015, 0.015, "red"))
    for sgn in (1, -1):
        word = arch.text("REDLINE", 0.62, 0.012, "brand_white")
        # Reading downwards: turned a quarter so the baseline runs along -Y.
        xf = T(0.81, 9.3, 0.015 * sgn) @ Rz(-math.pi / 2)
        if sgn < 0:
            xf = T(0.81, 9.3, -0.015) @ Ry(math.pi) @ Rz(math.pi / 2)
        m.extend(word.transformed(xf))
    return part(m, "flag_pole")


# ---- tents ------------------------------------------------------------------------------------------------

def marquee():
    m = Mesh()
    hl, hw, wall, ridge = 10.5, 5.5, 3.0, 5.2
    m.extend(bx(-hl, hl, -0.3, 0.15, -hw, hw, "deck"))
    # Walls: canvas dado, glazing above, on both long sides; canvas gable ends.
    for sz in (-1, 1):
        z = sz * hw
        m.extend(bx(-hl, hl, 0.15, 1.0, z - 0.03, z + 0.03, "canvas"))
        m.extend(bx(-hl, hl, 1.0, wall - 0.35, z - 0.02, z + 0.02, "glass"))
    for sx in (-1, 1):
        x = sx * hl
        m.extend(bx(x - 0.03, x + 0.03, 0.15, wall, -hw, hw, "canvas"))
        facing(m, [Vector((x, wall, -hw)), Vector((x, wall, hw)), Vector((x, ridge, 0))], "canvas", (sx, 0, 0))
        facing(m, [Vector((x, wall, -hw)), Vector((x, wall, hw)), Vector((x, ridge, 0))], "canvas", (-sx, 0, 0))
    # Frame: uprights every 5.25 m and rafters.
    for k in range(5):
        x = -hl + k * (2 * hl / 4)
        for sz in (-1, 1):
            m.extend(bx(x - 0.07, x + 0.07, 0.15, wall, sz * hw - 0.07, sz * hw + 0.07, "steel"))
    # Roof (both slopes overhanging the walls a little) and a red fascia band with lettering.
    for sz in (-1, 1):
        roof = [Vector((-hl - 0.2, ridge, 0)), Vector((hl + 0.2, ridge, 0)),
                Vector((hl + 0.2, wall - 0.12, sz * (hw + 0.35))), Vector((-hl - 0.2, wall - 0.12, sz * (hw + 0.35)))]
        both(m, roof, "canvas", (0, 1, sz * 0.5))
        m.extend(bx(-hl - 0.2, hl + 0.2, wall - 0.4, wall - 0.05, sz * (hw + 0.36) - 0.04, sz * (hw + 0.36) + 0.04, "red"))
        for x, word in ((-5.0, "REDLINE"), (5.0, "ARCBOX")):
            w = arch.text(word, 0.22, 0.01, "brand_white")
            xf = T(x, wall - 0.33, hw + 0.4) if sz > 0 else T(-x, wall - 0.33, -hw - 0.4) @ Ry(math.pi)
            m.extend(w.transformed(xf))
    return part(m, "marquee")


def pagoda():
    m = Mesh()
    h, peak, s = 2.5, 5.0, 3.0
    m.extend(bx(-s, s, -0.3, 0.12, -s, s, "deck"))
    for sx in (-1, 1):
        for sz in (-1, 1):
            m.extend(bx(sx * s - 0.06, sx * s + 0.06, 0.12, h, sz * s - 0.06, sz * s + 0.06, "steel"))
    # Back wall canvas (local -Z), red valance all round, concave pyramid roof with a spike.
    m.extend(bx(-s, s, 0.12, h, -s - 0.02, -s + 0.02, "canvas"))
    corners = [(-s, -s), (s, -s), (s, s), (-s, s)]
    for (ax, az), (bx_, bz) in zip(corners, corners[1:] + corners[:1]):
        out = Vector(((ax + bx_) / 2, 0, (az + bz) / 2)).normalized()
        val = [Vector((ax * 1.05, h, az * 1.05)), Vector((bx_ * 1.05, h, bz * 1.05)), Vector((bx_ * 1.05, h - 0.4, bz * 1.05)),
               Vector((ax * 1.05, h - 0.4, az * 1.05))]
        both(m, val, "red", out)
        mid = 0.45
        for t0, t1 in ((0.0, mid), (mid, 1.0)):
            def at(px, pz, t):
                k = 1.05 * (1 - t)
                return Vector((px * k, h + (peak - h) * (t ** 0.8 if t < 1 else 1), pz * k))
            quad = [at(ax, az, t0), at(bx_, bz, t0), at(bx_, bz, t1), at(ax, az, t1)]
            if t1 == 1.0:
                quad = quad[:3]
            both(m, quad, "canvas", out + Vector((0, 1, 0)))
    m.extend(cylinder(0.04, 1.0, 6, "steel", "y", (0, peak + 0.5, 0)))
    return part(m, "pagoda")


# ---- parked cars ------------------------------------------------------------------------------------------

def _car(name, body, cabin, wheel_z, width=1.82, cabin_width=1.52, wheel_r=0.34):
    m = Mesh()
    lower = arch.prism(body, -width / 2, width / 2, "car_paint")
    arch.repaint(lower, "car_dark", lambda n, c: c.y < 0.42)
    m.extend(lower)
    upper = arch.prism(cabin, -cabin_width / 2, cabin_width / 2, "car_glass")
    arch.repaint(upper, "car_paint", lambda n, c: n.y > 0.8)
    m.extend(upper)
    for z in wheel_z:
        for sx in (-1, 1):
            m.extend(cylinder(wheel_r, 0.24, 6, "car_dark", "x", (sx * (width / 2 - 0.04), wheel_r, z)))
    return part(m, name, sharp=30)


def cars():
    sedan = _car("sedan",
                 [(-2.4, 0.3), (2.35, 0.3), (2.42, 0.55), (2.3, 0.82), (1.25, 0.98), (-2.25, 0.98), (-2.45, 0.75)],
                 [(-1.35, 0.96), (1.2, 0.96), (0.45, 1.44), (-0.95, 1.42)], (-1.45, 1.45))
    suv = _car("suv",
               [(-2.4, 0.38), (2.35, 0.38), (2.42, 0.7), (2.3, 1.02), (1.5, 1.12), (-2.35, 1.12), (-2.45, 0.85)],
               [(-2.3, 1.1), (1.45, 1.1), (0.8, 1.76), (-2.2, 1.78)], (-1.5, 1.5), width=1.92, cabin_width=1.7,
               wheel_r=0.38)
    pickup = _car("pickup",
                  [(-2.9, 0.42), (2.85, 0.42), (2.95, 0.75), (2.85, 1.08), (1.7, 1.15), (-2.9, 1.12), (-2.95, 0.9)],
                  [(-0.6, 1.12), (1.65, 1.12), (1.0, 1.85), (-0.5, 1.86)], (-1.9, 1.85), width=2.0, cabin_width=1.82,
                  wheel_r=0.4)
    hatch = _car("hatch",
                 [(-2.0, 0.3), (1.95, 0.3), (2.05, 0.55), (1.95, 0.8), (1.0, 0.95), (-1.95, 0.98), (-2.05, 0.7)],
                 [(-1.95, 0.96), (0.95, 0.94), (0.25, 1.46), (-1.75, 1.45)], (-1.25, 1.25), width=1.76,
                 cabin_width=1.5, wheel_r=0.31)
    return [sedan, suv, pickup, hatch]


# ---- shrubs -----------------------------------------------------------------------------------------------

def shrubs():
    """About 100 triangles each: they are drawn a couple of thousand times."""
    out = []
    m = Mesh()
    for i, (c, r) in enumerate((((0, 0.32, 0), (0.85, 0.6, 0.85)), ((0.5, 0.2, 0.3), (0.55, 0.42, 0.5)))):
        m.extend(arch.blob(c, r, 11 + i, "shrub", segs=7, rings=4, lumps=0.28, freq=2.2, flat_bottom=0.5))
    out.append(part(m, "shrub_round", sharp=70))
    m = Mesh()
    m.extend(arch.blob((0, 0.75, 0), (0.6, 0.95, 0.6), 23, "shrub", segs=7, rings=5, lumps=0.22, freq=2.5, flat_bottom=0.1))
    m.extend(arch.blob((0.1, 1.6, 0.05), (0.3, 0.5, 0.3), 24, "shrub", segs=5, rings=3, lumps=0.2, freq=2.5))
    out.append(part(m, "shrub_juniper", sharp=70))
    m = Mesh()
    for i in range(3):
        a = 2 * math.pi * i / 3 + 0.4
        c = (0.55 * math.cos(a), 0.12, 0.55 * math.sin(a))
        m.extend(arch.blob(c, (0.75, 0.38, 0.75), 31 + i, "shrub", segs=6, rings=3, lumps=0.25, freq=2.0, flat_bottom=0.6))
    out.append(part(m, "shrub_low", sharp=70))
    return out


# ---- groves and far clumps --------------------------------------------------------------------------------

def groves():
    """Dense clumps of trees at unit scale (radius ~1, top ~0.95) for treelines and creek lines, ~150 triangles each:
    grove_oak (broad flattened crowns over trunk stubs) and grove_cedar (taller, narrower, darker crowns)."""
    out = []
    rnd = random.Random(77)
    m = Mesh()
    for i in range(4):
        a = 2 * math.pi * i / 4 + rnd.uniform(-0.3, 0.3)
        r = rnd.uniform(0.35, 0.55)
        c = Vector((r * math.cos(a), rnd.uniform(0.55, 0.7), r * math.sin(a)))
        m.extend(arch.blob(tuple(c), (0.5, 0.32, 0.5), 400 + i, "grove", segs=7, rings=4, lumps=0.3, freq=3.0,
                           flat_bottom=0.45))
        if i < 3:
            m.extend(bx(c.x - 0.035, c.x + 0.035, -0.1, c.y - 0.15, c.z - 0.035, c.z + 0.035, "bark"))
    m.extend(arch.blob((0, 0.74, 0), (0.45, 0.3, 0.45), 410, "grove", segs=7, rings=4, lumps=0.3, freq=3.0,
                       flat_bottom=0.45))
    out.append(part(m, "grove_oak", sharp=70))
    m = Mesh()
    for i in range(5):
        a = 2 * math.pi * i / 5 + rnd.uniform(-0.3, 0.3)
        r = rnd.uniform(0.3, 0.6) if i else 0.0
        h = rnd.uniform(0.8, 1.1)
        c = (r * math.cos(a), h * 0.5, r * math.sin(a))
        m.extend(arch.blob(c, (0.32, h * 0.55, 0.32), 420 + i, "grove", segs=7, rings=4, lumps=0.25, freq=3.2,
                           flat_bottom=0.0))
    out.append(part(m, "grove_cedar", sharp=70))
    return out


def far_clump():
    """One low dome for distant tree clumps (36 triangles), unit radius, sunk a little into the ground."""
    m = arch.blob((0, 0.25, 0), (1.0, 0.6, 1.0), 430, "grove", segs=6, rings=3, lumps=0.25, freq=1.8, flat_bottom=0.5)
    return [part(m, "far_clump", sharp=70)]


# ---- skyline ----------------------------------------------------------------------------------------------

def skyline():
    rnd = random.Random(512)
    m = Mesh()
    # Low-rise mass across the whole width, then towers clustered towards the middle.
    for i in range(70):
        x = rnd.uniform(-1300, 1300)
        z = rnd.uniform(-500, 300)
        w, d = rnd.uniform(40, 110), rnd.uniform(40, 110)
        h = rnd.uniform(12, 40) * (1.2 - abs(x) / 1600)
        m.extend(bx(x - w / 2, x + w / 2, -20, h, z - d / 2, z + d / 2, "skyline"))
    for i in range(34):
        x = rnd.gauss(0, 380)
        z = rnd.uniform(-350, 200)
        h = max(45.0, 215 * math.exp(-(x / 520) ** 2) * rnd.uniform(0.35, 1.0))
        w = rnd.uniform(28, 48)
        d = rnd.uniform(26, 44)
        glass = "skyline_glass" if rnd.random() < 0.55 else "skyline"
        style = rnd.random()
        if style < 0.35:
            # Setback tower: two or three stacked, narrowing blocks.
            y0 = -20.0
            for k, frac in enumerate((0.55, 0.85, 1.0)):
                s = 1 - 0.18 * k
                m.extend(bx(x - w * s / 2, x + w * s / 2, y0, h * frac, z - d * s / 2, z + d * s / 2, glass))
                y0 = h * frac
        elif style < 0.6:
            # Slender tower with a pointed crown and a spire.
            m.extend(bx(x - w / 2, x + w / 2, -20, h, z - d / 2, z + d / 2, glass))
            c = Vector((x, h, z))
            pts = [c + Vector((sx * w / 2, 0, sz * d / 2)) for sx, sz in ((-1, -1), (1, -1), (1, 1), (-1, 1))]
            tip = c + Vector((0, w * 0.9, 0))
            for a, b in zip(pts, pts[1:] + pts[:1]):
                facing(m, [a, b, tip], "skyline", ((a + b) / 2 - c) + Vector((0, 0.5, 0)))
            m.extend(bx(x - 0.6, x + 0.6, h + w * 0.9, h + w * 0.9 + 18, z - 0.6, z + 0.6, "skyline"))
        else:
            m.extend(bx(x - w / 2, x + w / 2, -20, h, z - d / 2, z + d / 2, glass))
            m.extend(bx(x - w * 0.3, x + w * 0.3, h, h + 6, z - d * 0.3, z + d * 0.3, "skyline"))
    # Two construction cranes.
    for x, z, h in ((-260.0, -40.0, 150.0), (330.0, 60.0, 120.0)):
        m.extend(bx(x - 1.2, x + 1.2, -20, h, z - 1.2, z + 1.2, "skyline"))
        m.extend(bx(x - 45, x + 15, h, h + 2, z - 0.8, z + 0.8, "skyline"))
    return part(m, "skyline", sharp=30)


# ---- build and contracts ----------------------------------------------------------------------------------

def build(ctx):
    exports = [
        ("surroundings/amphitheatre.glb", lambda: [amphitheatre()]),
        ("surroundings/plaza_props.glb", lambda: [light_pole(), planter(), kiosk(), flag_pole()]),
        ("surroundings/tents.glb", lambda: [marquee(), pagoda()]),
        ("surroundings/parked_cars.glb", cars),
        ("surroundings/shrubs.glb", shrubs),
        ("surroundings/groves.glb", groves),
        ("surroundings/far_clumps.glb", far_clump),
        ("surroundings/skyline.glb", lambda: [skyline()]),
    ]
    for path, make in exports:
        blend.reset()
        define_materials()
        objs = make()
        print(f"[surroundings] {path}: {[blend.triangles(o) for o in objs]} triangles")
        export.glb(ctx.model(path), objs)


def contracts():
    return [
        Contract("surroundings/amphitheatre.glb", 40_000, nodes=("amphitheatre",), materials=AMPHI_SLOTS,
                 size={"x": (2 * 38.0, 2 * AMPHI_HALF_WIDTH), "z": (55.0, AMPHI_RIM - AMPHI_BACK)},
                 min_y=(SKIRT - 0.01, SKIRT + 0.01), max_bytes=3 * 1024 * 1024),
        Contract("surroundings/plaza_props.glb", 8_000, nodes=PROPS,
                 materials=("concrete", "steel", "steel_dark", "led_white", "red", "brand_white", "kiosk", "panel",
                            "shrub", "bark", "deck"),
                 max_bytes=1024 * 1024),
        Contract("surroundings/tents.glb", 8_000, nodes=TENTS,
                 materials=("canvas", "glass", "steel", "red", "brand_white", "deck"),
                 size={"x": (21.0, 21.8), "y": (5.5, 6.5)}, max_bytes=1024 * 1024),
        Contract("surroundings/parked_cars.glb", 1_000, nodes=CARS, materials=("car_paint", "car_glass", "car_dark"),
                 size={"z": (5.8, 6.0), "y": (1.8, 1.9)}, min_y=(-0.01, 0.01), max_bytes=256 * 1024),
        Contract("surroundings/shrubs.glb", 400, nodes=SHRUBS, materials=("shrub",), max_bytes=256 * 1024),
        Contract("surroundings/groves.glb", 700, nodes=GROVES, materials=("grove", "bark"), max_bytes=256 * 1024),
        Contract("surroundings/far_clumps.glb", 60, nodes=("far_clump",), materials=("grove",), max_bytes=64 * 1024),
        Contract("surroundings/skyline.glb", 8_000, nodes=("skyline",), materials=("skyline", "skyline_glass"),
                 size={"y": (200.0, 280.0)}, max_bytes=1024 * 1024),
    ]


# ---- previews ---------------------------------------------------------------------------------------------

def preview(ctx):
    res = tuple(int(v) for v in ctx.args.get("res", "1280x720").split("x"))
    samples = int(ctx.args.get("samples", "32"))
    which = ctx.args.get("models", "amphi,plaza,tents,cars,shrubs,groves,skyline").split(",")

    def render(name, eye, target, lens):
        path = f"{ctx.preview_dir}/surroundings_{name}.png"
        pv.render(path, eye, target, lens, res, samples)
        print(f"[surroundings] preview {path}")

    if "amphi" in which:
        blend.reset()
        define_materials()
        amphitheatre()
        pv.stage(ground_hex="#8c8a50")
        render("amphitheatre", (55.0, 38.0, 85.0), (0.0, 4.0, 8.0), 28)
        render("amphitheatre_stage", (14.0, 9.0, 34.0), (0.0, 8.0, -8.0), 30)
    if "plaza" in which:
        blend.reset()
        define_materials()
        for i, o in enumerate([light_pole(), planter(), kiosk(), flag_pole()]):
            o.location.x = (i - 1.5) * 5.5
        pv.stage(ground_hex="#cfc6b6")
        render("plaza_props", (4.0, 6.0, 20.0), (0.0, 4.0, 0.0), 35)
    if "tents" in which:
        blend.reset()
        define_materials()
        a, b = marquee(), pagoda()
        b.location.x = 18.0
        pv.stage(ground_hex="#9a8f6a")
        render("tents", (30.0, 12.0, 30.0), (6.0, 2.5, 0.0), 32)
    if "cars" in which:
        blend.reset()
        define_materials()
        for i, o in enumerate(cars()):
            o.location.x = (i - 1.5) * 2.8
        pv.stage(ground_hex="#55565a")
        render("parked_cars", (9.0, 5.5, 10.0), (0.0, 0.7, 0.0), 35)
    if "shrubs" in which:
        blend.reset()
        define_materials()
        for i, o in enumerate(shrubs()):
            o.location.x = (i - 1) * 2.6
        pv.stage(ground_hex="#8c8a50")
        render("shrubs", (0.0, 2.2, 7.0), (0.0, 0.7, 0.0), 40)
    if "groves" in which:
        blend.reset()
        define_materials()
        objs = groves() + far_clump()
        for i, o in enumerate(objs):
            o.scale = (6, 6, 6)
            o.location.x = (i - 1) * 14.0
        pv.stage(ground_hex="#8c8a50")
        render("groves", (0.0, 9.0, 38.0), (0.0, 3.0, 0.0), 40)
    if "skyline" in which:
        blend.reset()
        define_materials()
        # The preview camera clips at 100 m: render a 1:50 copy.
        sky = skyline()
        sky.scale = (0.02, 0.02, 0.02)
        pv.stage(ground_hex="#8c8a50")
        render("skyline", (0.0, 1.5, 64.0), (0.0, 1.8, 0.0), 35)
