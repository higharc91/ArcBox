"""COTA's landmarks: landmarks/*.glb (<= 600k triangles in total).

Every model is authored in the local frame the procedural landmark used (World/Landmarks.cs, World/Trees.cs), so
placement, footprints and cameras are unchanged:

  grandstand_main.glb, grandstand_t1.glb, grandstand_corner.glb
      Frame: origin on the track surface at the stand's front edge, +X along the track, +Y up, +Z away from the track
      (rows climb towards +Z). Tier contract (Redline.Presentation.Models.GrandstandModel; the instanced crowd sits on
      it): row r's tread is the strip RUN*r <= z <= RUN*(r+1) at y = 1 + 0.9 r; seats every 0.85 m from 0.6 m inside
      each end; aisles 1.5 m wide every 25 m (first 12.5 m in) carry half-height steps instead of seats. The roof's
      underside is 4.5 m above the top row's tread line, overhanging the front by 4.5 m. Nodes: grandstand,
      tier_first and tier_last (empties at the front-middle of the first and the last tread). Slots: concrete
      concrete_dark seat safety glass_clear metal steel roof trim brand (roof, trim and brand take the stand's colour in
      Godot).
  pit_building.glb
      Frame: origin at the pit lane's outer edge + 1 m (Landmarks.PitLaneOuter + 1), +X along the building (against
      the direction of travel), +Z away from the track. 330 m: 27 garages behind roller shutters (every fourth one
      half open, lit inside), team boards, a leaning glass hospitality floor, a roof with a red edge and a glass
      terrace rail, rooftop plant. Nodes: pit_building.
  pit_station.glb
      One pit-wall station, instanced by the TV along the pit wall (each at its own place on the track, so they follow
      its rise): a long, low covered stand on the pit-lane side of the wall with a console, screens, stools, a canopy
      reaching over the wall with a red edge, lights, posts and glass ends. Frame: origin on the track surface at the
      wall's pit-lane face, X along the wall, +Z towards the garages. Nodes: pit_station.
  start_gantry.glb
      Frame: X across the track (to the left of travel), +Y up, +Z along travel; the lights face -Z (the grid).
      Columns at x = +/-(Width(0)/2 + 2.6), a clad girder with REDLINE/ARCBOX lettering, a dot-matrix board on top and
      the light board with five pods. Nodes: gantry, light_1..light_5 (each pod's pair of lenses, its own mesh, origin
      at the pod; Godot switches their material: StartGantry.SetLit).
  tower.glb
      Origin on the ground at the tower's centre. The Observation Tower: a tapering white shaft with glass lift
      strips, the round glass deck at 68-71.5 m under a white roof and mast, and the red veil: two counter-twisted
      families of 18 curved steel tubes sweeping from the crown to a 26 m ring, a hyperboloid lattice. Nodes: tower.
  trees.glb
      Three live-oak variants for MultiMesh: unit scale (crown radius ~1, top ~1.5, trunk base at 0), a short trunk
      splitting into low sweeping limbs under lumpy, flattened crown clusters. Nodes: oak_a, oak_b, oak_c, each one mesh
      with the slots bark and leaves (Godot tints the leaves per instance).
"""
import math
import random

from mathutils import Vector

from lib import arch, blend, export, materials, preview as pv
from lib.mesh import Mesh, Ry, T, box, cylinder, lathe, spline, tube
from lib.validate import Contract

GROUP = "landmarks"

# ---- tier contract (Redline.Presentation.Models.GrandstandModel) --------------------------------------------
RISE = 0.9
RUN = 1.2
FIRST_TOP = 1.0
SEAT_PITCH = 0.85
SEAT_INSET = 0.6
SEAT_END = 0.4
AISLE_SPACING = 25.0
AISLE_OFFSET = 12.5
AISLE_HALF = 0.75
AISLE_END = 5.0
ROOF_CLEAR = 4.5

STANDS = {
    "grandstand_main.glb": (300.0, 22),
    "grandstand_t1.glb": (130.0, 16),
    "grandstand_corner.glb": (90.0, 10),
}

PIT_LENGTH = 330.0
PIT_DEPTH = 18.0
PIT_FRAME_LATERAL = 32.0          # Landmarks.PitLaneOuter + 1
STATION_LENGTH = 16.0
GANTRY_HALF = 15.0 / 2 + 2.6      # TrackGeometry.Width(0) / 2 + 2.6
LIGHT_SPACING = 1.15
LIGHT_Y = 7.75
LIGHT_Z = -0.4

LIGHTS = tuple(f"light_{k}" for k in range(1, 6))
TREES = ("oak_a", "oak_b", "oak_c")


def tread(r):
    return FIRST_TOP + RISE * r


def seat_xs(length):
    xs = []
    k = 0
    while True:
        x = -length / 2 + SEAT_INSET + SEAT_PITCH * k
        if x >= length / 2 - SEAT_END:
            return xs
        xs.append(x)
        k += 1


def aisles(length):
    out = []
    k = 0
    while True:
        a = -length / 2 + AISLE_OFFSET + AISLE_SPACING * k
        if a > length / 2 - AISLE_END:
            return out
        out.append(a)
        k += 1


def in_aisle(x, length):
    return any(abs(x - a) < AISLE_HALF for a in aisles(length))


def define_materials():
    materials.define("concrete", "#b9b6ae", roughness=0.9)
    materials.define("concrete_dark", "#8f8d87", roughness=0.92)
    materials.define("tower_white", "#eceef0", roughness=0.55)
    materials.define("seat", "#2d3a4f", roughness=0.35, coat=0.3, coat_roughness=0.2)
    materials.define("safety", "#e8c21a", roughness=0.6)
    materials.define("glass", "#2a3b4f", metallic=0.7, roughness=0.05)
    materials.define("glass_clear", "#9fb4c4", metallic=0.2, roughness=0.05, alpha=0.3)
    materials.metal("metal", "#9aa0a8", roughness=0.35, anisotropic=0.2)
    materials.define("steel", "#dcdfe3", metallic=0.3, roughness=0.4)
    materials.define("steel_dark", "#3a3d44", metallic=0.5, roughness=0.45)
    materials.define("roof", "#eeeff1", metallic=0.1, roughness=0.5)
    materials.define("trim", "#c9ccd2", metallic=0.2, roughness=0.4)
    materials.define("brand", "#e10600", roughness=0.35, coat=0.4)
    materials.metal("shutter", "#c3c7cc", roughness=0.42, anisotropic=0.5)
    materials.define("interior", "#24262b", roughness=0.8)
    materials.define("wall_white", "#e3e6ea", roughness=0.5)
    materials.define("epoxy", "#8a8d92", roughness=0.25, coat=0.6)
    materials.define("panel", "#16171c", metallic=0.2, roughness=0.25, coat=0.8)
    materials.emissive("led_white", "#f4f7ff", 4.0)
    materials.paint("red", "#d0101c")
    materials.define("board", "#0c0d11", roughness=0.3, coat=0.8)
    materials.emissive("led_red", "#ff1a12", 4.0)
    materials.define("lamp", "#2a0606", roughness=0.25, coat=1.0)
    materials.emissive("monitor", "#3a5f8f", 1.5)
    materials.define("bark", "#4a3b2c", roughness=0.95)
    materials.define("leaves", "#5a6a32", roughness=0.9)


def bx(x0, x1, y0, y1, z0, z1, mat):
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))
    z0, z1 = sorted((z0, z1))
    return box((x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), mat)


def part(mesh, name, bevel=0.0, segments=1, angle=35.0, sharp=40.0):
    o = blend.make(mesh, name)
    if bevel:
        blend.bevel(o, bevel, segments, angle)
    return blend.finish(o, sharp)


def facing_track(word_mesh):
    """Lettering authored reading from +Z turned to read from -Z (the track side of a stand or building)."""
    return word_mesh.transformed(Ry(math.pi))


# ---- grandstands ---------------------------------------------------------------------------------------------

def seats(length, rows):
    """One bucket seat per crowd place: a pan and a back sharing the fold (smooth across it), 4 triangles."""
    m = Mesh()
    hw = 0.29
    for r in range(rows):
        y0, z0 = tread(r), RUN * r
        for x in seat_xs(length):
            if in_aisle(x, length):
                continue
            fl = m.vert((x - hw, y0 + 0.45, z0 + 0.26))
            fr = m.vert((x + hw, y0 + 0.45, z0 + 0.26))
            bl = m.vert((x - hw, y0 + 0.40, z0 + 0.68))
            br = m.vert((x + hw, y0 + 0.40, z0 + 0.68))
            tl = m.vert((x - hw * 0.94, y0 + 0.90, z0 + 0.78))
            tr = m.vert((x + hw * 0.94, y0 + 0.90, z0 + 0.78))
            # Pan faces up, back faces the track (-Z): counter-clockwise seen from those sides.
            m.face((fl, bl, br, fr), "seat", uv=((0, 0), (0, 0.42), (1, 0.42), (1, 0)))
            m.face((bl, tl, tr, br), "seat", uv=((0, 0.42), (0, 0.93), (1, 0.93), (1, 0.42)))
    return m


def grandstand(length, rows):
    hw = length / 2
    depth = RUN * rows
    ytop = tread(rows - 1)
    ry = ytop + ROOF_CLEAR
    z_front, z_back = -4.5, depth + 1.9
    parts = []

    # Tiers: one stepped concrete section; treads light, risers and the front wall darker.
    prof = [(0.0, -4.0), (0.0, FIRST_TOP)]
    for r in range(rows):
        if r:
            prof.append((RUN * r, tread(r)))
        prof.append((RUN * (r + 1), tread(r)))
    prof.append((depth, -4.0))
    tiers = arch.prism(prof, -hw, hw, "concrete_dark")
    arch.repaint(tiers, "concrete", lambda n, c: n.y > 0.9)
    m = Mesh()
    m.extend(tiers)
    for r in range(rows):
        m.extend(bx(-hw, hw, tread(r) - 0.03, tread(r) + 0.008, RUN * r - 0.006, RUN * r + 0.05, "safety"))
    # Aisles: a half step on every row, a rail down the middle.
    for a in aisles(length):
        for r in range(rows - 1):
            m.extend(bx(a - AISLE_HALF + 0.05, a + AISLE_HALF - 0.05, tread(r), tread(r) + RISE / 2,
                        RUN * r + RUN / 2, RUN * (r + 1), "concrete"))
        m.extend(tube([(a, tread(0) + 1.0, 0.35), (a, tread(rows - 1) + 1.0, RUN * (rows - 1) + 0.35)], 0.03,
                      sides=6, mat="metal"))
        for r in range(0, rows, 3):
            m.extend(bx(a - 0.025, a + 0.025, tread(r), tread(r) + 1.0, RUN * r + 0.32, RUN * r + 0.38, "metal"))
    # Front: coloured fascia, glass balustrade with a rail and posts.
    m.extend(bx(-hw, hw, -0.4, FIRST_TOP - 0.02, -0.03, 0.0, "trim"))
    m.extend(bx(-hw, hw, FIRST_TOP, FIRST_TOP + 0.95, 0.03, 0.05, "glass_clear"))
    m.extend(tube([(-hw, FIRST_TOP + 1.0, 0.04), (hw, FIRST_TOP + 1.0, 0.04)], 0.035, sides=8, mat="metal"))
    x = -hw
    while x <= hw + 1e-6:
        m.extend(bx(x - 0.03, x + 0.03, FIRST_TOP, FIRST_TOP + 1.0, 0.015, 0.075, "metal"))
        x += 2.5
    # Back wall with fins.
    wall_top = ytop + 1.2
    m.extend(bx(-hw, hw, -4.0, wall_top, depth, depth + 0.4, "concrete"))
    x = -hw
    while x <= hw + 1e-6:
        m.extend(bx(x - 0.18, x + 0.18, -4.0, wall_top, depth + 0.4, depth + 0.9, "concrete_dark"))
        x += 6.25
    # End stairs, climbing from the front at track level to the top row.
    n = max(4, round(ytop / 0.3))
    for s in (-1, 1):
        x0, x1 = sorted((s * (hw + 0.2), s * (hw + 2.6)))
        m.extend(arch.prism(arch.stair_profile(n, depth / n, ytop / n, base=-4.0), x0, x1, "concrete"))
        m.extend(bx(x0, x1, ytop - 0.3, ytop, depth, depth + 1.6, "concrete"))
        xr = s * (hw + 2.55)
        m.extend(tube([(xr, 1.0, 0.0), (xr, ytop + 1.0, depth), (xr, ytop + 1.0, depth + 1.6)], 0.035, sides=6,
                      mat="metal"))
        for k in range(0, 9):
            t = k / 8
            m.extend(bx(xr - 0.03, xr + 0.03, ytop * t, ytop * t + 1.0, depth * t - 0.03, depth * t + 0.03, "metal"))
    parts.append(part(m, "stand"))

    # Roof: a cantilevered membrane rising towards the front, its fascia carrying the lettering, steel trusses every
    # 12.5 m on a back girder and columns every 25 m.
    r = Mesh()
    under = lambda z: ry + (z - z_back) / (z_front - z_back) * 1.4  # noqa: E731  (underside height at z)
    r.extend(arch.prism([(z_back, ry + 0.25), (z_front, ry + 1.65), (z_front, ry + 1.4), (z_back, ry)],
                        -hw - 2, hw + 2, "roof"))
    r.extend(arch.prism([(z_front - 0.3, ry + 0.55), (z_front + 0.02, ry + 0.55), (z_front + 0.02, ry + 1.85),
                         (z_front - 0.3, ry + 1.85)], -hw - 2.05, hw + 2.05, "trim"))
    words = ["REDLINE", "ARCBOX"]
    count = max(1, int(length // 50))
    for i in range(count):
        x = (i - (count - 1) / 2) * 50
        word = arch.text(words[i % 2], 0.78, 0.03, "brand")
        r.extend(facing_track(word).transformed(T(x, ry + 0.82, z_front - 0.3)))
    xt = -hw
    while xt <= hw + 1e-6:
        top0 = Vector((xt, under(z_back) - 0.2, z_back))
        top1 = Vector((xt, under(z_front + 0.4) - 0.2, z_front + 0.4))
        bot0 = Vector((xt, ry - 3.2, z_back))
        bot1 = Vector((xt, under(z_front + 0.4) - 0.65, z_front + 0.4))
        r.extend(arch.beam(top0, top1, 0.3, 0.4, "steel"))
        r.extend(arch.beam(bot0, bot1, 0.3, 0.4, "steel"))
        nb = max(3, int((z_back - z_front) / 3.2))
        for i in range(nb + 1):
            t = i / nb
            a = bot0.lerp(bot1, t)
            b = top0.lerp(top1, t)
            r.extend(arch.beam(a, b, 0.16, 0.16, "steel"))
            if i < nb:
                r.extend(arch.beam(a, top0.lerp(top1, (i + 1) / nb), 0.14, 0.14, "steel"))
        xt += 12.5
    for y in (ry - 3.2, under(z_back) - 0.2):
        r.extend(arch.beam((-hw - 1, y, z_back), (hw + 1, y, z_back), 0.5, 0.6, "steel"))
    xc = -hw
    while xc <= hw + 1e-6:
        r.extend(bx(xc - 0.35, xc + 0.35, -4.0, ry - 0.3, z_back - 0.35, z_back + 0.35, "steel"))
        xc += 25
    parts.append(part(r, "roof"))

    st = blend.make(seats(length, rows), "seats")
    parts.append(blend.finish(st, 89.0))
    body = blend.join(parts, "grandstand")
    empties = [blend.empty("tier_first", (0.0, tread(0), 0.0)),
               blend.empty("tier_last", (0.0, tread(rows - 1), RUN * (rows - 1)))]
    return body, empties


# ---- pit building -----------------------------------------------------------------------------------------

def shutter(xc, bottom, top=4.6, z=0.45, width=10.6):
    prof = [(z + 0.1, bottom), (z, bottom)]
    y = bottom + 0.15
    while y < top - 0.1:
        prof += [(z, y - 0.03), (z + 0.028, y), (z, y + 0.03)]
        y += 0.3
    prof += [(z, top), (z + 0.1, top)]
    m = arch.prism(prof, xc - width / 2, xc + width / 2, "shutter")
    m.extend(bx(xc - width / 2, xc + width / 2, bottom, bottom + 0.12, z - 0.03, z + 0.11, "steel_dark"))
    return m


def garage_interior(xc, width=10.6, depth=12.0, height=4.6):
    m = Mesh()
    x0, x1 = xc - width / 2, xc + width / 2
    m.extend(arch.quad((x0, 0.02, 0.5), (x0, 0.02, depth), (x1, 0.02, depth), (x1, 0.02, 0.5), "epoxy"))
    m.extend(arch.quad((x0, 0.0, depth - 0.01), (x0, height, depth - 0.01), (x1, height, depth - 0.01),
                       (x1, 0.0, depth - 0.01), "wall_white"))
    m.extend(arch.quad((x0 + 0.01, 0, 0.5), (x0 + 0.01, height, 0.5), (x0 + 0.01, height, depth),
                       (x0 + 0.01, 0, depth), "wall_white"))
    m.extend(arch.quad((x1 - 0.01, 0, depth), (x1 - 0.01, height, depth), (x1 - 0.01, height, 0.5),
                       (x1 - 0.01, 0, 0.5), "wall_white"))
    m.extend(arch.quad((x0, height, 0.5), (x0, height, depth), (x1, height, depth), (x1, height, 0.5), "interior"))
    # Normals: the floor up, the walls and the ceiling inward.
    m.orient_outward(lambda c: Vector((xc - c.x if abs(c.x - xc) > width / 2 - 0.1 else 0,
                                       1 if c.y < 0.1 else (-1 if c.y > height - 0.1 else 0),
                                       -1 if c.z > depth - 0.1 else 0)))
    for k in range(3):
        xs = xc - 3.0 + 3.0 * k
        m.extend(bx(xs - 0.08, xs + 0.08, height - 0.06, height - 0.01, 1.0, depth - 1.0, "led_white"))
    for k in range(4):
        xs = x0 + 1.4 + k * 2.6
        m.extend(bx(xs, xs + 1.6, 0.0, 1.1, depth - 0.75, depth - 0.02, "panel"))
    return m


def pit_station():
    """One pit-wall station: a long, low covered stand on the pit-lane side of the pit wall. Frame: origin on the
    track surface at the wall's pit-lane face (Landmarks.PitWallOuter from the centre line), X along the wall, +Z
    towards the garages (the wall itself is drawn by the TV, following the track)."""
    hw, depth, roof = STATION_LENGTH / 2, 2.3, 2.55
    m = Mesh()
    m.extend(bx(-hw, hw, -0.6, 0.22, 0.0, depth, "steel_dark"))
    m.extend(bx(-hw, hw, 0.18, 0.225, depth - 0.06, depth, "safety"))
    # Console against the wall, a row of screens facing the crew, stools.
    m.extend(bx(-hw + 0.3, hw - 0.3, 0.22, 1.02, 0.0, 0.55, "panel"))
    n = 10
    for k in range(n):
        x = -hw + 1.0 + k * (STATION_LENGTH - 2.0) / (n - 1)
        m.extend(bx(x - 0.55, x + 0.55, 1.08, 1.6, 0.16, 0.24, "panel"))
        m.extend(arch.quad((x - 0.5, 1.12, 0.245), (x + 0.5, 1.12, 0.245), (x + 0.5, 1.56, 0.245),
                           (x - 0.5, 1.56, 0.245), "monitor", uv=((0, 0), (1, 0), (1, 1), (0, 1))))
        m.extend(bx(x - 0.05, x + 0.05, 1.02, 1.08, 0.17, 0.23, "steel_dark"))
        m.extend(cylinder(0.2, 0.07, 12, "seat", "y", (x, 0.82, 1.15)))
        m.extend(cylinder(0.03, 0.6, 6, "metal", "y", (x, 0.52, 1.15)))
    # Low canopy reaching over the wall, a red edge to the track, lights underneath; posts and glass ends.
    m.extend(bx(-hw - 0.15, hw + 0.15, roof, roof + 0.16, -1.0, depth + 0.3, "steel"))
    m.extend(bx(-hw - 0.15, hw + 0.15, roof - 0.04, roof + 0.2, -1.06, -0.98, "red"))
    m.extend(bx(-hw + 0.4, hw - 0.4, roof - 0.03, roof, 1.0, 1.12, "led_white"))
    for x in (-hw + 0.1, 0.0, hw - 0.1):
        for z in (0.08, depth - 0.12):
            m.extend(bx(x - 0.05, x + 0.05, 0.22, roof, z - 0.05, z + 0.05, "steel_dark"))
    for x in (-hw - 0.02, hw + 0.02):
        m.extend(bx(x - 0.015, x + 0.015, 0.22, roof - 0.05, 0.05, depth - 0.05, "glass_clear"))
    return part(m, "pit_station")


def pit_building():
    hw = PIT_LENGTH / 2
    D = PIT_DEPTH
    m = Mesh()
    garages = [-hw + 6 + 12 * g for g in range(27)]
    # Piers (full-depth walls between garages), the end office and the block behind.
    for g in range(28):
        x = -hw + 12 * g
        m.extend(bx(x - 0.7 if g else x, x + 0.7, -3.0, 4.6, 0.0, 12.0, "concrete"))
    m.extend(bx(-hw + 12 * 27 + 0.7, hw, -3.0, 4.6, 0.0, 12.0, "concrete"))
    m.extend(bx(-hw, hw, -3.0, 4.6, 12.0, D, "concrete"))
    for i, xc in enumerate(garages):
        open_ = i % 4 == 2
        m.extend(shutter(xc, 3.3 if open_ else 0.0))
        if open_:
            m.extend(garage_interior(xc))
        m.extend(bx(xc - 3.8, xc + 3.8, 4.72, 5.2, -0.06, 0.0, "panel"))
        m.extend(bx(xc - 3.6, xc + 3.6, 4.78, 4.82, -0.08, -0.06, "led_white"))
    # Header and first-floor slab, team-board band with an LED line.
    m.extend(bx(-hw, hw, 4.6, 6.1, 0.0, D, "concrete"))
    m.extend(bx(-hw, hw, 5.3, 6.1, -0.4, 0.0, "panel"))
    m.extend(bx(-hw, hw, 5.33, 5.4, -0.42, -0.4, "led_white"))
    # Hospitality floor: leaning glass wall with mullions and a transom, solid behind.
    m.extend(bx(-hw, hw, 6.1, 9.4, 1.0, D, "concrete"))
    g0, g1 = (6.1, 0.3), (9.4, -0.6)
    m.extend(arch.quad((-hw, g0[0], g0[1]), (hw, g0[0], g0[1]), (hw, g1[0], g1[1]), (-hw, g1[0], g1[1]), "glass",
                       uv=((-hw, 0), (hw, 0), (hw, 3.4), (-hw, 3.4))))
    for k in range(-1, 2, 2):
        x = k * hw
        m.extend(arch.quad((x, 6.1, 0.3), (x, 9.4, -0.6), (x, 9.4, 1.0), (x, 6.1, 1.0), "glass"))
    x = -hw
    while x <= hw + 1e-6:
        m.extend(arch.beam((x, g0[0], g0[1] - 0.06), (x, g1[0], g1[1] - 0.06), 0.12, 0.16, "steel_dark"))
        x += 3.0
    mid = 0.5
    m.extend(arch.beam((-hw, 6.1 + 3.3 * mid, 0.3 - 0.9 * mid - 0.06), (hw, 6.1 + 3.3 * mid, 0.3 - 0.9 * mid - 0.06),
                       0.1, 0.12, "steel_dark"))
    # Roof with a red edge, lettering, terrace rail and plant.
    m.extend(bx(-hw - 1.5, hw + 1.5, 9.4, 10.2, -2.8, D + 1.5, "steel"))
    m.extend(bx(-hw - 1.5, hw + 1.5, 9.85, 10.22, -2.86, -2.78, "red"))
    words = ["REDLINE", "ARCBOX"]
    for i, x in enumerate(range(-int(hw) + 45, int(hw) - 30, 60)):
        m.extend(facing_track(arch.text(words[i % 2], 0.32, 0.02, "red")).transformed(T(x, 9.47, -2.8)))
    m.extend(bx(-hw, hw, 10.2, 11.15, -2.45, -2.42, "glass"))
    m.extend(tube([(-hw, 11.2, -2.44), (hw, 11.2, -2.44)], 0.04, sides=8, mat="metal"))
    x = -hw
    while x <= hw + 1e-6:
        m.extend(bx(x - 0.03, x + 0.03, 10.2, 11.2, -2.47, -2.41, "metal"))
        x += 3.0
    x = -hw + 20
    while x < hw - 10:
        m.extend(bx(x - 3.5, x + 3.5, 10.2, 11.8, D / 2 + 2, D / 2 + 6, "steel_dark"))
        for k in range(5):
            m.extend(bx(x - 3.3 + 1.4 * k, x - 2.5 + 1.4 * k, 11.8, 11.95, D / 2 + 2.4, D / 2 + 5.6, "metal"))
        x += 37
    return part(m, "pit_building")


# ---- start gantry ------------------------------------------------------------------------------------------

def light_pod(k):
    """light_k: the pod's two red lenses (one mesh, origin at the pod) with their visors in the body."""
    x = (k - 3) * LIGHT_SPACING
    lens = Mesh()
    body = Mesh()
    for dy in (0.33, -0.33):
        c = (x, LIGHT_Y + dy, LIGHT_Z - 0.17)
        lens.extend(cylinder(0.23, 0.06, 24, "lamp", "z", c))
        body.extend(tube([(x, LIGHT_Y + dy, LIGHT_Z - 0.12), (x, LIGHT_Y + dy, LIGHT_Z - 0.3)], 0.27, sides=24,
                         mat="steel_dark", caps=False))
        body.extend(bx(x - 0.29, x + 0.29, LIGHT_Y + dy + 0.27, LIGHT_Y + dy + 0.3, LIGHT_Z - 0.45, LIGHT_Z - 0.1,
                       "steel_dark"))
    body.extend(bx(x - 0.42, x + 0.42, LIGHT_Y - 0.7, LIGHT_Y + 0.72, LIGHT_Z - 0.12, LIGHT_Z + 0.1, "steel_dark"))
    o = blend.make(lens, f"light_{k}")
    blend.finish(o)
    blend.set_origin(o, (x, LIGHT_Y, LIGHT_Z))
    return o, body


def start_gantry():
    hw = GANTRY_HALF
    m = Mesh()
    for s in (-1, 1):
        x = s * hw
        m.extend(bx(x - 0.75, x + 0.75, -1.0, 0.5, -0.75, 0.75, "concrete"))
        m.extend(arch.beam((x, 0.5, 0), (x, 9.0, 0), 0.62, 0.62, "steel_dark", up=(0, 0, 1)))
        m.extend(bx(x - 0.36, x + 0.36, 0.5, 0.56, -0.36, 0.36, "metal"))
    # Girder: clad box beam with a red cap and lettering both sides.
    gy0, gy1 = 8.55, 9.75
    m.extend(bx(-hw - 0.6, hw + 0.6, gy0, gy1, -0.45, 0.45, "steel_dark"))
    m.extend(bx(-hw - 0.6, hw + 0.6, gy1, gy1 + 0.14, -0.48, 0.48, "red"))
    m.extend(bx(-hw - 0.55, hw + 0.55, gy0 + 0.08, gy1 - 0.08, -0.47, 0.47, "panel"))
    m.extend(bx(-hw - 0.6, hw + 0.6, gy0 - 0.05, gy0, -0.48, 0.48, "red"))
    for side, word in ((-1, "REDLINE"), (1, "ARCBOX")):
        w = arch.text(word, 0.62, 0.02, "led_white")
        w = (facing_track(w) if side < 0 else w).transformed(T(0, gy0 + 0.29, side * 0.47))
        m.extend(w)
        for x in (-hw + 2.2, hw - 2.2):
            m.extend(bx(x - 0.9, x + 0.9, gy0 + 0.35, gy0 + 0.45, side * 0.47, side * 0.49, "led_red"))
    # Digital board on top: dot-matrix face (UVs in decimetres for Godot's dot texture), frame and legs.
    bw, by0, by1 = 4.6, gy1 + 0.45, gy1 + 1.95
    m.extend(bx(-bw - 0.12, bw + 0.12, by0 - 0.12, by1 + 0.12, -0.25, 0.25, "steel_dark"))
    for side in (-1, 1):
        z = side * 0.26
        pts = [(-bw, by0, z), (bw, by0, z), (bw, by1, z), (-bw, by1, z)]
        uv = [(p[0] * 10, p[1] * 10) for p in pts]
        if side < 0:
            pts, uv = pts[::-1], uv[::-1]
        m.extend(arch.quad(*pts, "board", uv=uv))
    # Its message in lit LEDs: ARCBOX to the grid, REDLINE to the far side.
    m.extend(facing_track(arch.text("ARCBOX", 0.8, 0.02, "led_red")).transformed(T(0, by0 + 0.35, -0.26)))
    m.extend(arch.text("REDLINE", 0.8, 0.02, "led_red").transformed(T(0, by0 + 0.35, 0.26)))
    for x in (-bw + 0.8, 0, bw - 0.8):
        m.extend(bx(x - 0.12, x + 0.12, gy1 + 0.14, by0 - 0.12, -0.12, 0.12, "steel_dark"))
    # Light board under the girder with its hangers.
    m.extend(bx(-3.45, 3.45, LIGHT_Y - 0.85, LIGHT_Y + 0.85, LIGHT_Z + 0.1, LIGHT_Z + 0.6, "panel"))
    m.extend(bx(-3.5, 3.5, LIGHT_Y - 0.9, LIGHT_Y - 0.82, LIGHT_Z + 0.05, LIGHT_Z + 0.65, "steel_dark"))
    for x in (-2.9, 2.9):
        m.extend(bx(x - 0.06, x + 0.06, LIGHT_Y + 0.85, gy0, LIGHT_Z + 0.3, LIGHT_Z + 0.42, "steel_dark"))
    lights = []
    for k in range(1, 6):
        o, body = light_pod(k)
        lights.append(o)
        m.extend(body)
    gantry = part(m, "gantry")
    for o in lights:
        blend.parent(o, gantry)
    return gantry, lights


# ---- Observation Tower ---------------------------------------------------------------------------------------

def tower():
    m = Mesh()
    shaft = [(0.0, -2.0), (2.6, -2.0), (2.35, 20.0), (1.95, 45.0), (1.6, 66.0), (1.6, 68.5), (0.0, 68.5)]
    rot = lambda mesh: mesh.transformed(T(0, 0, 0) @ _rz90())  # noqa: E731
    m.extend(rot(lathe([(r, y) for r, y in shaft], 32, "tower_white", outward=-1)))
    # Lift glass on two faces.
    for a in (0.0, math.pi):
        d = Vector((math.cos(a), 0, math.sin(a)))
        b0 = Vector((0, 1.0, 0)) + d * 2.62
        b1 = Vector((0, 66.0, 0)) + d * 1.66
        m.extend(arch.beam(b0, b1, 1.1, 0.12, "glass", up=tuple(d)))
    # Deck: soffit cone, floor, flared glass wall with mullions, roof, crown and mast.
    m.extend(rot(lathe([(1.7, 61.0), (8.2, 67.8), (8.6, 68.2)], 48, "tower_white", outward=-1)))
    m.extend(rot(lathe([(8.6, 68.2), (7.6, 68.25), (0.0, 68.25)], 48, "steel", outward=-1)))
    m.extend(rot(lathe([(7.6, 68.25), (8.5, 71.4)], 48, "glass", outward=-1)))
    for k in range(24):
        a = 2 * math.pi * k / 24
        d = Vector((math.cos(a), 0, math.sin(a)))
        m.extend(arch.beam(d * 7.62 + Vector((0, 68.25, 0)), d * 8.52 + Vector((0, 71.4, 0)), 0.1, 0.12, "steel_dark",
                           up=tuple(d)))
    m.extend(rot(lathe([(0.0, 71.4), (8.9, 71.4), (9.0, 71.7), (8.6, 72.1), (3.2, 72.3), (3.0, 73.6), (0.0, 73.6)],
                       48, "tower_white", outward=-1)))
    m.extend(rot(lathe([(0.6, 73.6), (0.15, 78.5), (0.0, 78.5)], 12, "steel", outward=-1)))
    m.extend(cylinder(0.12, 0.2, 8, "red", "y", (0, 78.55, 0)))
    # The red veil: two counter-twisted families of curved tubes, crown to ground ring.
    for fam in (1, -1):
        for k in range(18):
            a0 = 2 * math.pi * (k + (0.5 if fam < 0 else 0)) / 18
            pts = []
            for i in range(13):
                t = i / 12
                r = 2.9 + 23.1 * t ** 1.55
                y = 74.6 - 75.6 * t
                a = a0 + fam * 0.75 * t
                pts.append((r * math.cos(a), y, r * math.sin(a)))
            m.extend(tube(pts, 0.3, sides=8, mat="red", per=2))
    m.extend(arch.ring([(3.0 * math.cos(a), 74.6, 3.0 * math.sin(a)) for a in
                        (2 * math.pi * i / 36 for i in range(36))], 0.3, 8, "red"))
    m.extend(arch.ring([(26.0 * math.cos(a), -0.8, 26.0 * math.sin(a)) for a in
                        (2 * math.pi * i / 96 for i in range(96))], 0.55, 8, "concrete"))
    return part(m, "tower", sharp=50)


def _rz90():
    """lathe() revolves about X; this turns it about Y (the tower's axis): x -> y."""
    from mathutils import Matrix
    return Matrix.Rotation(math.pi / 2, 4, "Z")


# ---- trees ---------------------------------------------------------------------------------------------------

def oak(name, seed):
    """A live oak at unit scale: a short, thick trunk forking low into heavy limbs that sweep out almost level, under
    a broad, deep crown of lumpy clusters (a dome over the middle, clusters over each limb, lower drooping ones at the
    rim). Crown radius ~1, top ~1.5, like the procedural tree it replaces."""
    rnd = random.Random(seed)
    bark = Mesh()
    leaves = Mesh()
    lean = rnd.uniform(-0.06, 0.06)
    trunk_top = Vector((lean, 0.3 + rnd.uniform(-0.03, 0.03), lean * 0.5))
    bark.extend(tube([(0, -0.15, 0), (lean * 0.4, 0.15, 0), trunk_top], 0.13, sides=8, mat="bark", per=2,
                     taper=lambda t: 1.0 - 0.25 * t))
    limbs = rnd.randint(4, 5)
    tips = []
    for i in range(limbs):
        a = 2 * math.pi * (i + rnd.uniform(-0.2, 0.2)) / limbs + seed
        reach = rnd.uniform(0.6, 0.78)
        d = Vector((math.cos(a), 0, math.sin(a)))
        mid = trunk_top + d * reach * 0.42 + Vector((0, rnd.uniform(0.12, 0.2), 0))
        tip = trunk_top + d * reach + Vector((0, rnd.uniform(0.2, 0.3), 0))
        bark.extend(tube([trunk_top, mid, tip], 0.075, sides=6, mat="bark", per=2, taper=lambda t: 1.0 - 0.6 * t))
        tips.append((tip, d))
    blobs = [(Vector((lean, 1.02, 0)), Vector((0.72, 0.44, 0.72)))]
    for tip, d in tips:
        r = rnd.uniform(0.42, 0.52)
        blobs.append((tip + Vector((0, 0.22, 0)) + d * 0.06, Vector((r, r * 0.68, r))))
    for (t0, d0), (t1, d1) in zip(tips, tips[1:] + tips[:1]):
        d = (d0 + d1).normalized() if (d0 + d1).length > 1e-3 else d0
        r = rnd.uniform(0.34, 0.42)
        blobs.append((d * rnd.uniform(0.62, 0.72) + Vector((lean, 0.88, 0)), Vector((r, r * 0.66, r))))
        blobs.append((d * 0.82 + Vector((lean, 0.6, 0)), Vector((r * 0.8, r * 0.5, r * 0.8))))
    for i, (c, r) in enumerate(blobs):
        leaves.extend(arch.blob(tuple(c), tuple(r), seed * 31 + i, "leaves", segs=8, rings=5, lumps=0.26,
                                freq=2.6, flat_bottom=0.35))
    o = blend.make(Mesh().extend(bark).extend(leaves), name)
    return blend.finish(o, 60)


# ---- build and contracts -------------------------------------------------------------------------------------

def build(ctx):
    for fname, (length, rows) in STANDS.items():
        blend.reset()
        define_materials()
        body, empties = grandstand(length, rows)
        print(f"[landmarks] {fname}: {blend.triangles(body)} triangles")
        export.glb(ctx.model("landmarks/" + fname), [body] + empties)
    blend.reset()
    define_materials()
    pit = pit_building()
    print(f"[landmarks] pit_building.glb: {blend.triangles(pit)} triangles")
    export.glb(ctx.model("landmarks/pit_building.glb"), [pit])
    blend.reset()
    define_materials()
    station = pit_station()
    print(f"[landmarks] pit_station.glb: {blend.triangles(station)} triangles")
    export.glb(ctx.model("landmarks/pit_station.glb"), [station])
    blend.reset()
    define_materials()
    gantry, lights = start_gantry()
    print(f"[landmarks] start_gantry.glb: {blend.triangles(gantry) + sum(blend.triangles(o) for o in lights)} triangles")
    export.glb(ctx.model("landmarks/start_gantry.glb"), [gantry])
    blend.reset()
    define_materials()
    t = tower()
    print(f"[landmarks] tower.glb: {blend.triangles(t)} triangles")
    export.glb(ctx.model("landmarks/tower.glb"), [t])
    blend.reset()
    define_materials()
    trees = [oak(name, 11 + 7 * i) for i, name in enumerate(TREES)]
    print(f"[landmarks] trees.glb: {[blend.triangles(o) for o in trees]} triangles")
    export.glb(ctx.model("landmarks/trees.glb"), trees)


STAND_SLOTS = ("concrete", "concrete_dark", "seat", "safety", "glass_clear", "metal", "steel", "roof", "trim", "brand")


def _stand_checks(rows):
    def extra(s):
        errs = []
        want = {"tier_first": (0.0, tread(0), 0.0), "tier_last": (0.0, tread(rows - 1), RUN * (rows - 1))}
        for name, p in want.items():
            t = s["nodes"].get(name, {}).get("translation")
            if t is None or max(abs(t[i] - p[i]) for i in range(3)) > 1e-3:
                errs.append(f"{name} at {t}, expected {p}")
        return errs
    return extra


def _gantry_checks(s):
    errs = []
    for k, name in enumerate(LIGHTS, start=1):
        t = s["nodes"].get(name, {}).get("translation")
        want = ((k - 3) * LIGHT_SPACING, LIGHT_Y, LIGHT_Z)
        if t is None or max(abs(t[i] - want[i]) for i in range(3)) > 1e-3:
            errs.append(f"{name} at {t}, expected {want}")
    return errs


def contracts():
    out = []
    budgets = {"grandstand_main.glb": 90_000, "grandstand_t1.glb": 40_000, "grandstand_corner.glb": 25_000}
    for fname, (length, rows) in STANDS.items():
        out.append(Contract("landmarks/" + fname, budgets[fname], nodes=("grandstand", "tier_first", "tier_last"),
                            materials=STAND_SLOTS, size={"x": (length + 4, length + 6)}, min_y=(-4.01, -3.99),
                            max_bytes=6 * 1024 * 1024, extra=_stand_checks(rows)))
    out.append(Contract("landmarks/pit_building.glb", 80_000, nodes=("pit_building",),
                        materials=("concrete", "shutter", "glass", "steel", "red", "panel", "led_white", "epoxy",
                                   "wall_white"),
                        size={"x": (PIT_LENGTH + 2.9, PIT_LENGTH + 3.1)}, max_bytes=4 * 1024 * 1024))
    out.append(Contract("landmarks/pit_station.glb", 8_000, nodes=("pit_station",),
                        materials=("steel_dark", "panel", "monitor", "seat", "steel", "red", "led_white", "glass_clear"),
                        size={"x": (STATION_LENGTH + 0.25, STATION_LENGTH + 0.4)}, max_bytes=1024 * 1024))
    out.append(Contract("landmarks/start_gantry.glb", 30_000, nodes=("gantry",) + LIGHTS,
                        materials=("steel_dark", "panel", "red", "board", "lamp", "led_white", "concrete"),
                        size={"x": (2 * GANTRY_HALF + 1.4, 2 * GANTRY_HALF + 1.6)}, max_bytes=2 * 1024 * 1024,
                        extra=_gantry_checks))
    out.append(Contract("landmarks/tower.glb", 40_000, nodes=("tower",),
                        materials=("tower_white", "glass", "red", "steel", "concrete"),
                        size={"y": (79.0, 82.0), "x": (52.0, 54.0)}, max_bytes=3 * 1024 * 1024))
    out.append(Contract("landmarks/trees.glb", 6_000, nodes=TREES, materials=("bark", "leaves"),
                        size={"y": (1.4, 1.8), "x": (1.8, 2.6)}, max_bytes=1024 * 1024))
    return out


# ---- previews ------------------------------------------------------------------------------------------------

def preview(ctx):
    res = tuple(int(v) for v in ctx.args.get("res", "1280x720").split("x"))
    samples = int(ctx.args.get("samples", "32"))
    which = ctx.args.get("models", "stand,pit,gantry,tower,trees").split(",")

    def render(name, eye, target, lens):
        path = f"{ctx.preview_dir}/landmarks_{name}.png"
        pv.render(path, eye, target, lens, res, samples)
        print(f"[landmarks] preview {path}")

    if "stand" in which:
        blend.reset()
        define_materials()
        grandstand(90.0, 10)
        pv.stage(ground_hex="#59633a")
        render("stand_front", (30.0, 12.0, -38.0), (0.0, 7.0, 6.0), 35)
        render("stand_end", (-62.0, 9.0, -18.0), (-35.0, 8.0, 7.0), 35)
    if "pit" in which:
        blend.reset()
        define_materials()
        pit_building()
        pv.stage(ground_hex="#4a4b50")
        render("pit", (-60.0, 9.0, -36.0), (-120.0, 4.0, 2.0), 35)
    if "gantry" in which:
        blend.reset()
        define_materials()
        start_gantry()
        pv.stage(ground_hex="#3a3b3e")
        render("gantry", (6.0, 5.0, -24.0), (0.0, 8.0, 0.0), 40)
    if "tower" in which:
        blend.reset()
        define_materials()
        tower()
        pv.stage(ground_hex="#59633a")
        render("tower", (120.0, 22.0, 160.0), (0.0, 38.0, 0.0), 30)
    if "trees" in which:
        blend.reset()
        define_materials()
        for i, name in enumerate(TREES):
            o = oak(name, 11 + 7 * i)
            o.location.x = (i - 1) * 2.6
        pv.stage(ground_hex="#59633a")
        render("trees", (0.0, 1.6, 6.5), (0.0, 0.7, 0.0), 40)
