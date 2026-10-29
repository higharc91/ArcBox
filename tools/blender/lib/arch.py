"""Architecture helpers for the podium set and the circuit landmarks, authored in the Godot frame (metres, +Y up).

Everything returns a lib.mesh.Mesh (pure geometry with slot names) except text(), which needs bpy to shape the glyphs.
  frame(origin, right, up, normal)        4x4 matrix whose columns are the given axes (local -> parent)
  prism(profile, x0, x1, slot)            a (z, y) polygon extruded along X (tiers, stairs, roofs, fascias)
  stair_profile(n, run, rise, base)       the (z, y) outline of a solid flight of n steps
  beam(a, b, w, h, slot, up)              a rectangular member from a to b (w across, h along `up`)
  truss(a, b, w, h, bay, chord, brace, slot, up)
                                          a four-chord box truss with diagonals on every face
  ring(points, r, sides, slot)            a closed round tube through a loop of points
  blob(centre, radii, seed, slot, ...)    a lumpy low-poly ellipsoid (tree crowns)
  text(body, height, depth, slot, ...)    extruded lettering in the brand face (Titillium Web Black, slanted),
                                          lying in the local XY plane, reading from +Z, front face at z = depth
"""
import math
import os

from mathutils import Matrix, Vector, noise

from .asset import REPO_ROOT
from .mesh import Mesh, ellipsoid, tube

FONT = os.path.join(REPO_ROOT, "src", "ArcBox.Host", "fonts", "TitilliumWeb-Black.ttf")


def frame(origin, right, up, normal):
    m = Matrix.Identity(4)
    for i, axis in enumerate((right, up, normal)):
        a = Vector(axis)
        m[0][i], m[1][i], m[2][i] = a.x, a.y, a.z
    m[0][3], m[1][3], m[2][3] = origin
    return m


def prism(profile, x0, x1, mat, caps=True):
    """Extrudes a simple (z, y) polygon from x = x0 to x = x1. UVs in metres (along X, around the outline)."""
    m = Mesh()
    a = [m.vert((x0, y, z)) for z, y in profile]
    b = [m.vert((x1, y, z)) for z, y in profile]
    n = len(profile)
    run = 0.0
    for i in range(n):
        j = (i + 1) % n
        step = math.hypot(profile[j][0] - profile[i][0], profile[j][1] - profile[i][1])
        m.face((a[i], a[j], b[j], b[i]), mat, uv=((x0, run), (x0, run + step), (x1, run + step), (x1, run)), smooth=False)
        run += step
    if caps:
        m.face(a, mat, uv=[(z, y) for z, y in profile], smooth=False)
        m.face(b[::-1], mat, uv=[(z, y) for z, y in reversed(profile)], smooth=False)
    return m.orient_closed()


def stair_profile(n, run, rise, base=0.0, z0=0.0, y0=0.0):
    """(z, y) outline of a solid flight climbing towards +z: n steps of `run` x `rise`, down to y = base."""
    pts = [(z0, base), (z0, y0)]
    for k in range(n):
        y = y0 + (k + 1) * rise
        pts += [(z0 + k * run, y), (z0 + (k + 1) * run, y)]
    pts.append((z0 + n * run, base))
    out = []
    for p in pts:
        if not out or (abs(p[0] - out[-1][0]) > 1e-9 or abs(p[1] - out[-1][1]) > 1e-9):
            out.append(p)
    return out


def _axes(a, b, up):
    a, b = Vector(a), Vector(b)
    d = b - a
    length = d.length
    d = d / length
    u = Vector(up)
    side = d.cross(u)
    if side.length < 1e-6:
        side = d.cross(Vector((1, 0, 0)))
        if side.length < 1e-6:
            side = d.cross(Vector((0, 0, 1)))
    side.normalize()
    u = side.cross(d).normalized()
    return a, b, d, side, u, length


def beam(a, b, w, h, mat, up=(0, 1, 0)):
    """A rectangular member from a to b: `w` wide across, `h` deep along the component of `up` normal to it."""
    a, b, d, side, u, length = _axes(a, b, up)
    m = Mesh()
    corners = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    ra = [m.vert(a + side * (sx * w / 2) + u * (sy * h / 2)) for sx, sy in corners]
    rb = [m.vert(b + side * (sx * w / 2) + u * (sy * h / 2)) for sx, sy in corners]
    for i in range(4):
        j = (i + 1) % 4
        m.face((ra[i], ra[j], rb[j], rb[i]), mat, uv=((0, 0), (w, 0), (w, length), (0, length)), smooth=False)
    m.face(ra, mat, smooth=False)
    m.face(rb[::-1], mat, smooth=False)
    return m.orient_closed()


def truss(a, b, w, h, bay, chord, brace, mat, up=(0, 1, 0), chord_sides=8, ends=True):
    """A four-chord box truss from a to b (centre line), `w` x `h` between chord centres, diagonals every `bay`."""
    a, b, d, side, u, length = _axes(a, b, up)
    n = max(1, round(length / bay))
    m = Mesh()
    offs = [(-1, -1), (1, -1), (1, 1), (-1, 1)]
    for sx, sy in offs:
        o = side * (sx * w / 2) + u * (sy * h / 2)
        m.extend(tube([a + o, b + o], chord, sides=chord_sides, major=tuple(u), mat=mat))
    for k in range(n):
        p0 = a + d * (length * k / n)
        p1 = a + d * (length * (k + 1) / n)
        for f in range(4):
            c0 = side * (offs[f][0] * w / 2) + u * (offs[f][1] * h / 2)
            c1 = side * (offs[(f + 1) % 4][0] * w / 2) + u * (offs[(f + 1) % 4][1] * h / 2)
            if k % 2:
                c0, c1 = c1, c0
            m.extend(beam(p0 + c0, p1 + c1, brace, brace, mat, up=tuple(d.cross(c1 - c0)) if (c1 - c0).length > 0 else up))
    if ends:
        for p in (a, b):
            for f in range(4):
                c0 = side * (offs[f][0] * w / 2) + u * (offs[f][1] * h / 2)
                c1 = side * (offs[(f + 1) % 4][0] * w / 2) + u * (offs[(f + 1) % 4][1] * h / 2)
                m.extend(beam(p + c0, p + c1, brace, brace, mat, up=tuple(d)))
    return m


def ring(points, r, sides, mat):
    """A closed round tube through a loop of points (the last joins the first)."""
    pts = [Vector(p) for p in points]
    m = Mesh()
    rings = []
    n = len(pts)
    for i, p in enumerate(pts):
        t = (pts[(i + 1) % n] - pts[i - 1]).normalized()
        a = Vector((0, 1, 0)) - t * t.y
        if a.length < 1e-4:
            a = Vector((1, 0, 0)) - t * t.x
        a.normalize()
        b = t.cross(a)
        rings.append([m.vert(p + a * (r * math.cos(2 * math.pi * j / sides)) + b * (r * math.sin(2 * math.pi * j / sides)))
                      for j in range(sides)])
    for i in range(n):
        i2 = (i + 1) % n
        for j in range(sides):
            j2 = (j + 1) % sides
            m.face((rings[i][j], rings[i][j2], rings[i2][j2], rings[i2][j]), mat,
                   uv=((j / sides, i), ((j + 1) / sides, i), ((j + 1) / sides, i + 1), (j / sides, i + 1)))
    return m.orient_outward(lambda c: c - min(pts, key=lambda q: (q - c).length))


def blob(centre, radii, seed, mat, segs=10, rings=6, lumps=0.22, freq=1.6, flat_bottom=0.0):
    """A lumpy ellipsoid: each vertex pushed out or in by deterministic Perlin noise (seeded by offset). With
    flat_bottom > 0 the lower part is squashed up (a crown's shaded underside)."""
    m = ellipsoid(radii, centre, segs, rings, mat)
    c = Vector(centre)
    off = Vector((seed * 17.31, seed * 5.77, seed * 11.13))
    for i, v in enumerate(m.verts):
        d = v - c
        k = 1.0 + lumps * noise.noise(d * freq + off)
        nv = c + d * k
        if flat_bottom and nv.y < c.y:
            nv.y = c.y - (c.y - nv.y) * (1 - flat_bottom)
        m.verts[i] = nv
    return m


_FONT_CACHE = {}


def text(body, height, depth, mat, align="CENTER", shear=0.2, resolution=3, spacing=1.0):
    """Extruded lettering in Titillium Web Black, slanted like the brand's italic. Cap height `height` (for capitals and
    numerals), lying in the XY plane reading from +Z, from z = 0 back to z = depth front. align: LEFT, CENTER, RIGHT
    (x = 0 at that edge); the baseline is at y = 0. Returns a closed (welded) Mesh."""
    import bmesh
    import bpy

    font = _FONT_CACHE.get("black")
    try:
        alive = font is not None and font.name in bpy.data.fonts
    except ReferenceError:  # freed by blend.reset()
        alive = False
    if not alive:
        font = bpy.data.fonts.load(FONT, check_existing=True)
        _FONT_CACHE["black"] = font
    cu = bpy.data.curves.new("lettering", "FONT")
    cu.body = body
    cu.font = font
    cu.size = 1.0
    cu.extrude = 0.5
    cu.shear = shear
    cu.space_character = spacing
    cu.align_x = align
    cu.resolution_u = resolution
    ob = bpy.data.objects.new("lettering", cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), depsgraph=dg)
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    # Cap height of the face: an "H" at size 1 (the glyphs' own extent would vary with descenders and slants).
    scale = height / _cap_height(font)
    out = Mesh()
    idx = {}
    for v in bm.verts:
        idx[v.index] = out.vert((v.co.x * scale, v.co.y * scale, (v.co.z + 0.5) * depth))
    for f in bm.faces:
        out.face([idx[v.index] for v in f.verts], mat, smooth=False)
    bm.free()
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(cu)
    bpy.data.meshes.remove(me)
    return out.orient_closed()


_CAP = {}


def _cap_height(font):
    if font.name in _CAP:
        return _CAP[font.name]
    import bpy
    cu = bpy.data.curves.new("cap", "FONT")
    cu.body = "H"
    cu.font = font
    ob = bpy.data.objects.new("cap", cu)
    bpy.context.scene.collection.objects.link(ob)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(ob.evaluated_get(dg), depsgraph=dg)
    h = max(v.co.y for v in me.vertices) - min(v.co.y for v in me.vertices)
    bpy.data.objects.remove(ob, do_unlink=True)
    bpy.data.curves.remove(cu)
    bpy.data.meshes.remove(me)
    _CAP[font.name] = h
    return h


def repaint(mesh, slot, where):
    """Sets `slot` on every face for which where(unit_normal, centroid) is true (e.g. treads vs risers)."""
    for k, f in enumerate(mesh.faces):
        n = mesh.face_normal(f)
        if n.length > 1e-12 and where(n.normalized(), mesh.centroid(f)):
            mesh.mats[k] = slot
    return mesh


def quad(a, b, c, d, slot, uv=None, smooth=False):
    """A single quad a-b-c-d (counter-clockwise seen from its front); UVs default to metres in its own plane."""
    m = Mesh()
    idx = [m.vert(p) for p in (a, b, c, d)]
    m.face(idx, slot, uv=uv, smooth=smooth)
    return m
