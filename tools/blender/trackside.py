"""Barriers and trackside furniture: trackside/*.glb. The TV places them from Redline.Presentation.Course.Trackside
(the plan) as MultiMeshes and binds every slot to its own material (World/Trackside.cs).

Frame for every prop: origin on the ground, +Y up, X along the track, and -Z towards the track: a barrier's face is
the plane z = 0 and it reaches back to +Z; a sign, hut, camera or screen looks along -Z. Everything that meets the
ground reaches down to y = -0.25 (a footing; -0.45 for the barriers, which stand on the verge at the track's
height), so a slope under a prop never shows a gap.

  barriers.glb
      tecpro_block (1.5 m long, 0.95 m deep, 1.0 m high: a rounded energy-absorbing block with two black straps; slot
      tecpro takes the per-instance red, white or blue), tecpro_block_lod (a 10-triangle stand-in), tyre_module (1.8 m
      of tyre wall: three stacks of four tyres, two rows deep, behind a bulging conveyor-belt face), tyre_module_lod,
      armco_post (a post with two spacer blocks; the double W-beam rails are drawn by the TV along the track),
      fence_post and fence_post_overhang (catch fence posts, 4.2 m; the overhang one cranks over towards the track
      at 3.4 m up to 4.4 m, 1.0 m over; the TV draws the mesh, overhang panel and cables along the track).
  furniture.glb
      marshal_post (a white hut with a red roof on a concrete plinth, an open front, a number board on the roof and a
      digital light panel on a pole), corner_board (a 2.4 x 1.5 m turn-number board on two posts, its face 3.0-4.5 m
      up), brake_board (a 1.2 x 0.75 m distance board on a post), camera_platform (a 4 m scaffold tower with a deck,
      rails, a ladder, a broadcast camera on a tripod, its operator and an umbrella). Slot number is the board face,
      UVs 0..1 across it (aspect 1.6): the TV draws the turn, distance or post number from an atlas per instance.
      Slot led_panel is the light panel's face (UVs 0..1).
  big_screen.glb
      big_screen: a 12 x 6.75 m LED screen (slot screen, UVs 0..1 across it, top-left at UV (0, 0) in Godot) in a
      black frame with a REDLINE header, on two box-truss legs with a back frame; its face is 5.5-12.25 m up.

No logos or trademarks: the only lettering is REDLINE.
"""
import math

import bpy
from mathutils import Vector

from lib import arch, blend, export, materials, preview as pv
from lib.mesh import Mesh, T, Ry, box, cylinder, rounded_outline, tube
from lib.validate import Contract

GROUP = "trackside"

FOOT = -0.25
# Barriers stand on the verge at the track's height; on a slope or a bend the ground behind them can drop away faster.
BARRIER_FOOT = -0.45
# The catch fence (posts and mesh) reaches further down: the ground behind it can fall away from the track.
FENCE_FOOT = -1.0

TECPRO_LENGTH, TECPRO_DEPTH, TECPRO_HEIGHT = 1.5, 0.95, 1.0
TYRE_LENGTH, TYRE_DEPTH, TYRE_HEIGHT = 1.8, 1.25, 1.02
ARMCO_RAILS = (0.50, 0.85)          # rail centre heights; each W-beam is 0.31 m tall and 0.085 m deep
ARMCO_POST_Z = (0.20, 0.30)
FENCE_HEIGHT, KNEE, OVERHANG, OVERHANG_TOP = 4.4, 3.4, 1.0, 4.4

BARRIERS = ("tecpro_block", "tecpro_block_lod", "tyre_module", "tyre_module_lod", "armco_post", "fence_post",
            "fence_post_overhang")
FURNITURE = ("marshal_post", "corner_board", "brake_board", "camera_platform")
BARRIER_SLOTS = ("tecpro", "strap", "belt", "tyre", "tyre_inner", "galvanised")
FURNITURE_SLOTS = ("hut", "hut_dark", "hut_roof", "concrete", "number", "board_back", "steel_dark", "galvanised",
                   "led_panel", "red", "camera", "operator", "canvas", "deck")
SCREEN_SLOTS = ("screen", "screen_frame", "steel", "brand_white", "led_white")


def define_materials():
    materials.define("tecpro", "#f2f2f2", roughness=0.55, coat=0.3)
    materials.define("strap", "#16171a", roughness=0.7)
    materials.rubber("belt", "#1a1b1e")
    materials.rubber("tyre", "#131315")
    materials.define("tyre_inner", "#060607", roughness=1.0)
    materials.metal("galvanised", "#a9adb2", roughness=0.42, anisotropic=0.0)
    materials.define("hut", "#eeeeea", roughness=0.6)
    materials.define("hut_dark", "#1e2024", roughness=0.9)
    materials.paint("hut_roof", "#d0101c", matte=True)
    materials.define("concrete", "#c4c1b9", roughness=0.9)
    materials.define("number", "#f4f4f4", roughness=0.5)
    materials.define("board_back", "#5c6068", metallic=0.4, roughness=0.5)
    materials.define("steel_dark", "#383b42", metallic=0.5, roughness=0.42)
    materials.emissive("led_panel", "#18e05a", 2.0)
    materials.paint("red", "#d0101c")
    materials.define("camera", "#1d1f24", metallic=0.3, roughness=0.4)
    materials.define("operator", "#ff6a13", roughness=0.7)
    materials.define("canvas", "#f4f3ef", roughness=0.7)
    materials.define("deck", "#7a7d82", metallic=0.6, roughness=0.5)
    materials.emissive("screen", "#2a4f8a", 1.5)
    materials.define("screen_frame", "#141518", metallic=0.4, roughness=0.45)
    materials.define("steel", "#dcdfe3", metallic=0.3, roughness=0.4)
    materials.define("brand_white", "#f4f4f4", roughness=0.35, coat=0.4)
    materials.emissive("led_white", "#f4f7ff", 4.0)


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


def bx(x0, x1, y0, y1, z0, z1, mat):
    x0, x1 = sorted((x0, x1))
    y0, y1 = sorted((y0, y1))
    z0, z1 = sorted((z0, z1))
    return box((x1 - x0, y1 - y0, z1 - z0), ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), mat)


def open_box(x0, x1, y0, y1, z0, z1, mat, skip=("bottom",)):
    """A box without the faces named in `skip` (bottom, top, front = -Z, back = +Z, left = -X, right = +X)."""
    m = Mesh()
    c = [Vector((x, y, z)) for x in (x0, x1) for y in (y0, y1) for z in (z0, z1)]

    def q(a, b, cc, d, want, name):
        if name not in skip:
            facing(m, [c[a], c[b], c[cc], c[d]], mat, want, uv=[(0, 0), (1, 0), (1, 1), (0, 1)])
    # Corner k is (x, y, z) = (x0 or x1, y0 or y1, z0 or z1) with k = 4 xi + 2 yi + zi.
    q(0, 1, 3, 2, (-1, 0, 0), "left")
    q(4, 5, 7, 6, (1, 0, 0), "right")
    q(0, 2, 6, 4, (0, 0, -1), "front")
    q(1, 3, 7, 5, (0, 0, 1), "back")
    q(0, 4, 5, 1, (0, -1, 0), "bottom")
    q(2, 3, 7, 6, (0, 1, 0), "top")
    return m


def plate_face(m, x0, x1, y0, y1, z, slot, want_z=-1):
    """A rectangle in the plane z facing -Z (or +Z), UVs 0..1 with v up in Blender (so v = 0 is the top in Godot)."""
    pts = [Vector((x0, y0, z)), Vector((x1, y0, z)), Vector((x1, y1, z)), Vector((x0, y1, z))]
    # A viewer on the -Z side looking along +Z has +X on their left: u runs from +X (u = 0) to -X so it reads left to
    # right for them.
    if want_z < 0:
        uv = [(1, 0), (0, 0), (0, 1), (1, 1)]
    else:
        uv = [(0, 0), (1, 0), (1, 1), (0, 1)]
    return facing(m, pts, slot, (0, 0, want_z), uv=uv)


def part(mesh, name, sharp=40.0):
    return blend.finish(blend.make(mesh, name), sharp)


def prism_xz(outline, y0, y1, slot, top=True):
    """Walls of a polygon outline in the XZ plane (counter-clockwise seen from above) from y0 to y1, and its top."""
    m = Mesh()
    n = len(outline)
    cx = sum(p[0] for p in outline) / n
    cz = sum(p[1] for p in outline) / n
    run = 0.0
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        step = math.hypot(b[0] - a[0], b[1] - a[1])
        mid = ((a[0] + b[0]) / 2 - cx, 0, (a[1] + b[1]) / 2 - cz)
        facing(m, [Vector((a[0], y0, a[1])), Vector((b[0], y0, b[1])), Vector((b[0], y1, b[1])), Vector((a[0], y1, a[1]))],
               slot, mid, uv=[(run, y0), (run + step, y0), (run + step, y1), (run, y1)], smooth=True)
        run += step
    if top:
        facing(m, [Vector((p[0], y1, p[1])) for p in outline], slot, (0, 1, 0), uv=[(p[0], p[1]) for p in outline])
    return m


# ---- barriers -----------------------------------------------------------------------------------------------

def tecpro_block():
    m = Mesh()
    hx = TECPRO_LENGTH / 2 - 0.01
    outline = rounded_outline([(-hx, 0.0), (hx, 0.0), (hx, TECPRO_DEPTH), (-hx, TECPRO_DEPTH)], 0.2, segs=3)
    m.extend(prism_xz(outline, BARRIER_FOOT, TECPRO_HEIGHT - 0.05, "tecpro", top=False))
    # A chamfered top edge and the lid.
    inset = rounded_outline([(-hx + 0.05, 0.05), (hx - 0.05, 0.05), (hx - 0.05, TECPRO_DEPTH - 0.05),
                             (-hx + 0.05, TECPRO_DEPTH - 0.05)], 0.15, segs=3)
    n = len(outline)
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        c, d = inset[(i + 1) % n], inset[i]
        y0, y1 = TECPRO_HEIGHT - 0.05, TECPRO_HEIGHT
        facing(m, [Vector((a[0], y0, a[1])), Vector((b[0], y0, b[1])), Vector((c[0], y1, c[1])), Vector((d[0], y1, d[1]))],
               "tecpro", ((a[0] + b[0]) / 2, 1.0, (a[1] + b[1]) / 2 - TECPRO_DEPTH / 2), smooth=True)
    facing(m, [Vector((p[0], TECPRO_HEIGHT, p[1])) for p in inset], "tecpro", (0, 1, 0),
           uv=[(p[0], p[1]) for p in inset])
    # Two straps across the face.
    for y in (0.24, 0.70):
        m.extend(open_box(-hx + 0.16, hx - 0.16, y, y + 0.07, -0.012, 0.02, "strap", skip=("back",)))
    return part(m, "tecpro_block", sharp=50)


def tecpro_block_lod():
    hx = TECPRO_LENGTH / 2 - 0.01
    m = open_box(-hx, hx, BARRIER_FOOT, TECPRO_HEIGHT, 0.0, TECPRO_DEPTH, "tecpro", skip=("bottom",))
    return part(m, "tecpro_block_lod")


TYRE_R, TYRE_HOLE = 0.30, 0.19
TYRE_X = (-0.6, 0.0, 0.6)
TYRE_Z = (0.04 + TYRE_R, 0.04 + 3 * TYRE_R + 0.02)


def _belt_z(x):
    """The conveyor belt's face bulges towards the track over each tyre stack."""
    return -0.03 * (0.5 + 0.5 * math.cos(2 * math.pi * x / 0.6))


def tyre_module():
    m = Mesh()
    hx = TYRE_LENGTH / 2
    xs = [-hx + i * TYRE_LENGTH / 12 for i in range(13)]
    top = TYRE_HEIGHT
    for a, b in zip(xs, xs[1:]):
        za, zb = _belt_z(a), _belt_z(b)
        # Face (u along in metres, v up) and the belt's top edge.
        facing(m, [Vector((a, BARRIER_FOOT, za)), Vector((b, BARRIER_FOOT, zb)), Vector((b, top, zb)), Vector((a, top, za))], "belt",
               (0, 0, -1), uv=[(a, BARRIER_FOOT), (b, BARRIER_FOOT), (b, top), (a, top)], smooth=True)
        facing(m, [Vector((a, top, za)), Vector((b, top, zb)), Vector((b, top, 0.04)), Vector((a, top, 0.04))], "belt",
               (0, 1, 0), uv=[(a, 0), (b, 0), (b, 0.05), (a, 0.05)])
    segs = 8
    stack_top = top - 0.04
    for z in TYRE_Z:
        for x in TYRE_X:
            ring_o = [Vector((x + TYRE_R * math.cos(2 * math.pi * k / segs), stack_top, z + TYRE_R * math.sin(2 * math.pi * k / segs)))
                      for k in range(segs)]
            ring_i = [Vector((x + TYRE_HOLE * math.cos(2 * math.pi * k / segs), stack_top - 0.03, z + TYRE_HOLE * math.sin(2 * math.pi * k / segs)))
                      for k in range(segs)]
            for k in range(segs):
                k2 = (k + 1) % segs
                facing(m, [ring_o[k], ring_o[k2], ring_i[k2], ring_i[k]], "tyre", (0, 1, 0), smooth=True)
                lo_k = Vector((ring_i[k].x, BARRIER_FOOT, ring_i[k].z))
                lo_k2 = Vector((ring_i[k2].x, BARRIER_FOOT, ring_i[k2].z))
                inward = (x - ring_i[k].x, 0, z - ring_i[k].z)
                facing(m, [ring_i[k], ring_i[k2], lo_k2, lo_k], "tyre_inner", inward)
            if z == TYRE_Z[-1]:
                # The back of the rear stacks (seen from behind the barrier); its tyre grooves are in the material.
                half = segs // 2
                for k in range(half):
                    a0 = 2 * math.pi * k / segs
                    a1 = 2 * math.pi * (k + 1) / segs
                    pa = Vector((x + TYRE_R * math.cos(a0), 0, z + TYRE_R * math.sin(a0)))
                    pb = Vector((x + TYRE_R * math.cos(a1), 0, z + TYRE_R * math.sin(a1)))
                    facing(m, [pa + Vector((0, BARRIER_FOOT, 0)), pb + Vector((0, BARRIER_FOOT, 0)), pb + Vector((0, stack_top, 0)),
                               pa + Vector((0, stack_top, 0))], "tyre", ((pa.x + pb.x) / 2 - x, 0, (pa.z + pb.z) / 2 - z),
                           uv=[(a0 * TYRE_R, BARRIER_FOOT), (a1 * TYRE_R, BARRIER_FOOT), (a1 * TYRE_R, stack_top), (a0 * TYRE_R, stack_top)],
                           smooth=True)
    return part(m, "tyre_module", sharp=60)


def tyre_module_lod():
    m = Mesh()
    hx = TYRE_LENGTH / 2
    facing(m, [Vector((-hx, BARRIER_FOOT, -0.015)), Vector((hx, BARRIER_FOOT, -0.015)), Vector((hx, TYRE_HEIGHT, -0.015)),
               Vector((-hx, TYRE_HEIGHT, -0.015))], "belt", (0, 0, -1),
           uv=[(-hx, BARRIER_FOOT), (hx, BARRIER_FOOT), (hx, TYRE_HEIGHT), (-hx, TYRE_HEIGHT)])
    facing(m, [Vector((-hx, TYRE_HEIGHT - 0.04, 0.0)), Vector((hx, TYRE_HEIGHT - 0.04, 0.0)),
               Vector((hx, TYRE_HEIGHT - 0.04, TYRE_DEPTH)), Vector((-hx, TYRE_HEIGHT - 0.04, TYRE_DEPTH))], "tyre", (0, 1, 0))
    facing(m, [Vector((-hx, BARRIER_FOOT, TYRE_DEPTH)), Vector((hx, BARRIER_FOOT, TYRE_DEPTH)), Vector((hx, TYRE_HEIGHT - 0.04, TYRE_DEPTH)),
               Vector((-hx, TYRE_HEIGHT - 0.04, TYRE_DEPTH))], "tyre", (0, 0, 1))
    return part(m, "tyre_module_lod")


def armco_post():
    m = Mesh()
    z0, z1 = ARMCO_POST_Z
    m.extend(open_box(-0.075, 0.075, BARRIER_FOOT, ARMCO_RAILS[1] + 0.17, z0, z1, "galvanised"))
    for y in ARMCO_RAILS:
        m.extend(open_box(-0.04, 0.04, y - 0.07, y + 0.07, 0.07, z0, "galvanised", skip=("back", "front", "bottom")))
        m.extend(open_box(-0.05, 0.05, y - 0.02, y + 0.02, -0.012, 0.0, "galvanised", skip=("back", "bottom")))
    return part(m, "armco_post")


def _pole(a, b, r, sides, mat):
    return tube([a, b], r, sides=sides, mat=mat, caps=True)


def fence_post():
    m = Mesh()
    m.extend(_pole((0, FENCE_FOOT, 0), (0, FENCE_HEIGHT + 0.05, 0), 0.055, 6, "galvanised"))
    return part(m, "fence_post")


def fence_post_overhang():
    m = Mesh()
    knee = Vector((0, KNEE, 0))
    tip = Vector((0, OVERHANG_TOP, -OVERHANG))
    m.extend(_pole((0, FENCE_FOOT, 0), tuple(knee + Vector((0, 0.04, 0))), 0.055, 6, "galvanised"))
    m.extend(_pole(tuple(knee), tuple(tip), 0.045, 6, "galvanised"))
    m.extend(_pole((0, KNEE - 1.1, 0), tuple(knee.lerp(tip, 0.6)), 0.03, 4, "galvanised"))
    return part(m, "fence_post_overhang")


# ---- furniture ----------------------------------------------------------------------------------------------

def marshal_post():
    m = Mesh()
    w, d = 1.3, 2.0
    # Plinth and deck.
    m.extend(open_box(-1.5, 1.5, FOOT, 0.25, -0.35, d + 0.15, "concrete"))
    # Walls: back, sides, the front below the opening and the header above it.
    m.extend(open_box(-w, w, 0.25, 2.3, d - 0.12, d, "hut"))
    for sx in (-1, 1):
        m.extend(open_box(sx * (w - 0.12), sx * w, 0.25, 2.3, 0.0, d - 0.12, "hut", skip=("bottom", "back", "front")))
    m.extend(open_box(-w, w, 0.25, 1.05, 0.0, 0.12, "hut"))
    m.extend(open_box(-w, w, 2.0, 2.3, 0.0, 0.12, "hut", skip=("top",)))
    # The dark interior seen through the opening.
    m.extend(open_box(-w + 0.12, w - 0.12, 1.05, 2.0, 0.14, d - 0.12, "hut_dark", skip=("front", "bottom")))
    m.extend(open_box(-w + 0.12, w - 0.12, 1.0, 1.05, 0.0, 0.14, "hut", skip=("bottom", "back")))
    # Roof slab overhanging the front, with a red fascia.
    m.extend(open_box(-1.55, 1.55, 2.3, 2.46, -0.45, d + 0.15, "hut_roof", skip=()))
    # Number board standing on the roof, facing the track.
    m.extend(open_box(-0.66, 0.66, 2.46, 3.36, 0.25, 0.33, "board_back", skip=("bottom", "front")))
    plate_face(m, -0.6, 0.6, 2.53, 3.28, 0.25, "number")
    m.extend(open_box(-0.66, 0.66, 2.46, 2.53, 0.25 - 0.01, 0.25, "board_back", skip=("bottom", "back")))
    m.extend(open_box(-0.66, 0.66, 3.28, 3.36, 0.25 - 0.01, 0.25, "board_back", skip=("bottom", "back")))
    # The digital light panel on a pole at the front corner.
    px = -(w + 0.55)
    m.extend(open_box(px - 0.05, px + 0.05, FOOT, 2.0, -0.05, 0.05, "steel_dark"))
    m.extend(open_box(px - 0.5, px + 0.5, 2.0, 3.0, -0.12, 0.05, "steel_dark", skip=("front",)))
    plate_face(m, px - 0.45, px + 0.45, 2.05, 2.95, -0.12, "led_panel")
    m.extend(open_box(px - 0.5, px + 0.5, 2.95, 3.0, -0.12 - 0.002, -0.12, "steel_dark", skip=("back",)))
    m.extend(open_box(px - 0.5, px + 0.5, 2.0, 2.05, -0.12 - 0.002, -0.12, "steel_dark", skip=("back",)))
    # A fire extinguisher by the door.
    m.extend(cylinder(0.09, 0.55, 8, "red", "y", (w + 0.25, 0.25 + 0.28, -0.2)))
    m.extend(open_box(w + 0.08, w + 0.42, 0.25, 0.85, 0.0, 0.05, "steel_dark", skip=("bottom",)))
    return part(m, "marshal_post", sharp=50)


def corner_board():
    m = Mesh()
    for sx in (-0.9, 0.9):
        m.extend(open_box(sx - 0.06, sx + 0.06, FOOT, 4.4, 0.06, 0.18, "galvanised"))
    m.extend(open_box(-1.26, 1.26, 2.94, 4.56, 0.0, 0.06, "board_back", skip=("front",)))
    plate_face(m, -1.2, 1.2, 3.0, 4.5, 0.0, "number")
    for y0, y1 in ((2.94, 3.0), (4.5, 4.56)):
        m.extend(open_box(-1.26, 1.26, y0, y1, -0.004, 0.0, "board_back", skip=("back",)))
    for x0, x1 in ((-1.26, -1.2), (1.2, 1.26)):
        m.extend(open_box(x0, x1, 3.0, 4.5, -0.004, 0.0, "board_back", skip=("back", "top", "bottom")))
    return part(m, "corner_board")


def brake_board():
    m = Mesh()
    m.extend(open_box(-0.05, 0.05, FOOT, 0.55, 0.03, 0.12, "galvanised"))
    m.extend(open_box(-0.64, 0.64, 0.5, 1.35, 0.0, 0.03, "board_back", skip=("front",)))
    plate_face(m, -0.6, 0.6, 0.55, 1.3, 0.0, "number")
    return part(m, "brake_board")


def camera_platform():
    m = Mesh()
    h, s = 4.0, 1.2
    legs = [(-s, -s), (s, -s), (s, s), (-s, s)]
    for x, z in legs:
        m.extend(_pole((x, FOOT, z), (x, h + 1.1, z), 0.04, 5, "galvanised"))
    # Braces: a ring at 1.4 and 2.8 m and diagonals on each side.
    for y in (1.4, 2.8):
        for i in range(4):
            a, b = legs[i], legs[(i + 1) % 4]
            m.extend(_pole((a[0], y, a[1]), (b[0], y, b[1]), 0.025, 4, "galvanised"))
    for i in range(4):
        a, b = legs[i], legs[(i + 1) % 4]
        m.extend(_pole((a[0], 0.1, a[1]), (b[0], 1.4, b[1]), 0.022, 4, "galvanised"))
        m.extend(_pole((b[0], 1.4, b[1]), (a[0], 2.8, a[1]), 0.022, 4, "galvanised"))
        m.extend(_pole((a[0], 2.8, a[1]), (b[0], h, b[1]), 0.022, 4, "galvanised"))
    # Deck, toe boards and rails.
    m.extend(open_box(-s - 0.15, s + 0.15, h - 0.08, h, -s - 0.15, s + 0.15, "deck", skip=()))
    for y in (h + 0.55, h + 1.05):
        for i in range(4):
            a, b = legs[i], legs[(i + 1) % 4]
            m.extend(_pole((a[0], y, a[1]), (b[0], y, b[1]), 0.022, 4, "galvanised"))
    # Ladder up the back.
    for x in (-0.25, 0.25):
        m.extend(_pole((x, FOOT, s + 0.12), (x, h + 0.9, s + 0.12), 0.02, 4, "galvanised"))
    for k in range(1, 13):
        y = k * 0.33
        m.extend(_pole((-0.25, y, s + 0.12), (0.25, y, s + 0.12), 0.015, 4, "galvanised"))
    # Camera on a tripod, the lens towards -Z.
    cy = h + 1.45
    for k in range(3):
        a = 2 * math.pi * k / 3 + math.pi / 6
        m.extend(_pole((0.42 * math.cos(a), h, 0.42 * math.sin(a) + 0.1), (0, cy - 0.2, 0.1), 0.018, 4, "camera"))
    m.extend(open_box(-0.16, 0.16, cy - 0.2, cy + 0.12, -0.25, 0.35, "camera", skip=()))
    m.extend(cylinder(0.09, 0.55, 8, "camera", "z", (0, cy - 0.04, -0.5)))
    m.extend(cylinder(0.11, 0.06, 8, "camera", "z", (0, cy - 0.04, -0.8)))
    m.extend(open_box(-0.24, -0.16, cy + 0.02, cy + 0.14, -0.05, 0.12, "camera", skip=()))
    # Operator behind it: legs, body, head.
    for x in (-0.12, 0.12):
        m.extend(open_box(x - 0.07, x + 0.07, h, h + 0.85, 0.55, 0.69, "camera", skip=("bottom",)))
    m.extend(tube([(0, h + 0.85, 0.6), (0, h + 1.45, 0.55)], 0.2, 0.13, sides=8, mat="operator"))
    m.extend(tube([(0, h + 1.5, 0.52), (0, h + 1.75, 0.5)], 0.1, sides=6, mat="camera"))
    for sx in (-1, 1):
        m.extend(tube([(sx * 0.2, h + 1.38, 0.55), (sx * 0.15, cy - 0.05, 0.3), (sx * 0.12, cy - 0.02, 0.1)], 0.05,
                      sides=4, mat="operator"))
    # Umbrella over them.
    pole_top = h + 2.6
    m.extend(_pole((0.5, h, 0.5), (0.5, pole_top, 0.5), 0.02, 4, "galvanised"))
    segs = 8
    rim = [Vector((0.5 + 1.25 * math.cos(2 * math.pi * k / segs), pole_top - 0.45, 0.5 + 1.25 * math.sin(2 * math.pi * k / segs)))
           for k in range(segs)]
    apex = Vector((0.5, pole_top, 0.5))
    for k in range(segs):
        facing(m, [apex, rim[k], rim[(k + 1) % segs]], "canvas", (0, 1, 0))
        facing(m, [apex - Vector((0, 0.01, 0)), rim[k] - Vector((0, 0.01, 0)), rim[(k + 1) % segs] - Vector((0, 0.01, 0))],
               "canvas", (0, -1, 0))
    return part(m, "camera_platform", sharp=50)


# ---- the big screen -----------------------------------------------------------------------------------------

SCREEN_W, SCREEN_H, SCREEN_Y = 12.0, 6.75, 5.5


def big_screen():
    m = Mesh()
    w2 = SCREEN_W / 2
    y0, y1 = SCREEN_Y, SCREEN_Y + SCREEN_H
    # Cabinet and frame.
    m.extend(open_box(-w2 - 0.35, w2 + 0.35, y0 - 0.35, y1 + 0.35, 0.02, 0.9, "screen_frame", skip=("front",)))
    plate_face(m, -w2, w2, y0, y1, 0.0, "screen")
    for (xa, xb, ya, yb) in ((-w2 - 0.35, w2 + 0.35, y0 - 0.35, y0), (-w2 - 0.35, w2 + 0.35, y1, y1 + 0.35),
                             (-w2 - 0.35, -w2, y0, y1), (w2, w2 + 0.35, y0, y1)):
        m.extend(open_box(xa, xb, ya, yb, -0.04, 0.02, "screen_frame", skip=("back",)))
    # A thin LED line under the screen, the header with lettering on top.
    m.extend(open_box(-w2, w2, y0 - 0.28, y0 - 0.2, -0.06, -0.04, "led_white", skip=("back",)))
    m.extend(open_box(-3.4, 3.4, y1 + 0.35, y1 + 1.55, 0.1, 0.7, "screen_frame", skip=("bottom",)))
    m.extend(arch.text("REDLINE", 0.8, 0.05, "brand_white").transformed(T(0, y1 + 0.55, 0.1) @ Ry(math.pi)))
    # Two box-truss legs and the back frame.
    for x in (-w2 + 2.0, w2 - 2.0):
        m.extend(arch.truss((x, FOOT + 0.02, 1.4), (x, y1 + 0.2, 1.4), 0.7, 0.7, 1.2, 0.06, 0.035, "steel", chord_sides=6))
        m.extend(open_box(x - 0.7, x + 0.7, FOOT, 0.3, 0.7, 2.1, "steel", skip=("bottom",)))
        m.extend(arch.beam((x, y0 + 1.0, 1.05), (x, y0 + 1.0, 0.9), 0.3, 0.3, "steel"))
        m.extend(arch.beam((x, y1 - 1.0, 1.05), (x, y1 - 1.0, 0.9), 0.3, 0.3, "steel"))
    m.extend(arch.truss((-w2 + 0.5, y0 + 1.0, 1.4), (w2 - 0.5, y0 + 1.0, 1.4), 0.5, 0.5, 1.5, 0.05, 0.03, "steel",
                        chord_sides=6, ends=False))
    m.extend(arch.truss((-w2 + 0.5, y1 - 1.0, 1.4), (w2 - 0.5, y1 - 1.0, 1.4), 0.5, 0.5, 1.5, 0.05, 0.03, "steel",
                        chord_sides=6, ends=False))
    return part(m, "big_screen", sharp=45)


# ---- build and contracts ------------------------------------------------------------------------------------

def build(ctx):
    exports = [
        ("trackside/barriers.glb", lambda: [tecpro_block(), tecpro_block_lod(), tyre_module(), tyre_module_lod(),
                                            armco_post(), fence_post(), fence_post_overhang()]),
        ("trackside/furniture.glb", lambda: [marshal_post(), corner_board(), brake_board(), camera_platform()]),
        ("trackside/big_screen.glb", lambda: [big_screen()]),
    ]
    for path, make in exports:
        blend.reset()
        define_materials()
        objs = make()
        print(f"[trackside] {path}: {[(o.name, blend.triangles(o)) for o in objs]}")
        export.glb(ctx.model(path), objs)


def contracts():
    return [
        Contract("trackside/barriers.glb", 1_200, nodes=BARRIERS, materials=BARRIER_SLOTS,
                 min_y=(FENCE_FOOT - 0.01, FENCE_FOOT + 0.01), max_bytes=256 * 1024),
        Contract("trackside/furniture.glb", 4_000, nodes=FURNITURE, materials=FURNITURE_SLOTS,
                 min_y=(FOOT - 0.01, FOOT + 0.01), max_bytes=512 * 1024),
        Contract("trackside/big_screen.glb", 12_000, nodes=("big_screen",), materials=SCREEN_SLOTS,
                 size={"x": (12.5, 13.0), "y": (13.0, 14.5)}, min_y=(FOOT - 0.01, FOOT + 0.01), max_bytes=1024 * 1024),
    ]


# ---- previews -----------------------------------------------------------------------------------------------

def preview(ctx):
    res = tuple(int(v) for v in ctx.args.get("res", "1280x720").split("x"))
    samples = int(ctx.args.get("samples", "32"))
    which = ctx.args.get("models", "barriers,furniture,screen").split(",")

    def render(name, eye, target, lens):
        path = f"{ctx.preview_dir}/trackside_{name}.png"
        pv.render(path, eye, target, lens, res, samples)
        print(f"[trackside] preview {path}")

    def tint(obj, hexes):
        # Preview only: per-instance colours are Godot's; copy the object with its tecpro slot repainted.
        out = []
        for i, hx in enumerate(hexes):
            o = obj.copy()
            o.data = obj.data.copy()
            name = f"tecpro_{i}"
            materials.define(name, hx, roughness=0.55, coat=0.3)
            for k, mat in enumerate(o.data.materials):
                if mat.name.startswith("tecpro"):
                    o.data.materials[k] = materials.get(name)

            bpy.context.scene.collection.objects.link(o)
            out.append(o)
        return out

    if "barriers" in which:
        blend.reset()
        define_materials()
        tec = tecpro_block()
        blocks = tint(tec, ["#d0101c", "#f2f2f2", "#1f4fa0", "#d0101c", "#f2f2f2"])
        # Blender's X is Godot's X: the rows run along it, the track side (Godot -Z) towards the camera.
        for i, o in enumerate(blocks):
            o.location = (-7.5 + i * TECPRO_LENGTH, 0.0, 0.0)
        tec.location = (100, 0, 0)
        tyres = tyre_module()

        for i in range(3):
            o = tyres if i == 0 else tyres.copy()
            if i:
                bpy.context.scene.collection.objects.link(o)
            o.location = (0.5 + TYRE_LENGTH / 2 + i * TYRE_LENGTH, 0.0, 0.0)
        for o in (tecpro_block_lod(), tyre_module_lod()):
            o.location = (100, 0, 0)
        for i in range(3):
            post = armco_post()
            post.location = (7.5 + i * 2.0, 0.0, 0.0)
        fp = fence_post()
        fp.location = (13.0, -2.0, 0.0)
        fo = fence_post_overhang()
        fo.location = (15.0, -2.0, 0.0)
        pv.stage(ground_hex="#55565a", sun_rotation=150)
        render("barriers", (1.5, 4.0, -14.0), (3.5, 1.2, 0.5), 30)
        render("barriers_close", (-2.0, 2.2, -4.0), (0.5, 0.6, 0.5), 35)
    if "furniture" in which:
        blend.reset()
        define_materials()
        objs = [marshal_post(), corner_board(), brake_board(), camera_platform()]
        for i, o in enumerate(objs):
            o.location.x = (i - 1.5) * 5.0
        pv.stage(ground_hex="#7a8a50", sun_rotation=150)
        render("furniture", (4.0, 5.0, -18.0), (0.0, 2.5, 0.0), 35)
    if "screen" in which:
        blend.reset()
        define_materials()
        big_screen()
        pv.stage(ground_hex="#7a8a50", sun_rotation=150)
        render("big_screen", (12.0, 6.0, -26.0), (0.0, 7.0, 0.0), 32)
