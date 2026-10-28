"""The team cap for the podium figures (Task 2), in the head-local frame of lib.head: a structured six-panel crown
that sits over the hair (an offset of the hair's smooth surface, so even curly hair stays under it), a stand at
the front panel, a curved brim and the top button. Slots: `cap` (crown and button), `cap_brim`. The empty
`number_cap` (returned as a frame) is where Godot draws the number on the front panel."""
import math

from mathutils import Vector

from . import figure as fg
from .figure import lerp, smoothstep
from .hair import cap_band
from .head import U, Y_TOP
from .mesh import Mesh


def _smooth_thickness(head, style, phi, y):
    u = phi * U
    p = head.pos(u, y)
    n = head.normal(u, y)
    t = style.thickness(phi, y, p, n) if style else 0.0
    return p, n, t


def cap(head, style):
    """Returns ((Mesh, normals), number frame (pos, right, up, normal)) in head-local coordinates."""
    extra = 0.0045 + (0.004 if style and style.style in ("curly", "coiled", "locs") else 0.0)

    def crown(phi, t):
        yb = cap_band(head, phi)
        y = lerp(yb, Y_TOP, t ** 0.85)
        p, n, th = _smooth_thickness(head, style, phi, y)
        front = smoothstep(1.3, 0.0, abs(phi)) * smoothstep(0.1, 0.55, t) * (1 - smoothstep(0.75, 1.0, t))
        lift = 0.006 * front + 0.003 * smoothstep(0.5, 1.0, t)
        flare = 0.0015 * (1 - smoothstep(0.0, 0.12, t))
        return p + n * (th + extra + lift + flare)

    phis = [-math.pi + 2 * math.pi * i / 40 for i in range(41)]
    ts = [0.0, 0.04, 0.12, 0.22, 0.34, 0.47, 0.6, 0.72, 0.83, 0.92, 0.975, 1.0]
    m, nrm = fg.grid_surface(lambda ph, t: crown(ph, t), phis, ts, lambda ph, t: "cap", wrap_u=True)
    m, nrm = fg.weld_mesh(m, nrm)
    parts = [(m, nrm)]

    # The band's inner wall (gives the crown's lower edge some thickness).
    def inner(phi, k):
        p = crown(phi, 0.0)
        c = Vector((0.0, p.y, -0.004))
        d = Vector((p.x - c.x, 0, p.z - c.z)).normalized()
        return p - d * 0.003 + Vector((0, 0.012 * k, 0))
    wall = fg.grid_surface(inner, phis, [0.0, 1.0], lambda ph, k: "cap", wrap_u=True)
    parts.append((wall[0].flip(), [-n for n in wall[1]]))

    # Brim: in front of the band, rounded, curved down at the sides.
    def brim_pt(s, r, top):
        phi = s * 1.0
        base = crown(phi, 0.0)
        c = Vector((0.0, base.y, -0.004))
        out = Vector((base.x - c.x, 0, base.z - c.z)).normalized()
        out = (out + Vector((0, 0, 0.9))).normalized()
        length = 0.074 * (1 - 0.78 * s * s) ** 0.6
        drop = 0.11 + 0.32 * s * s
        d = Vector((out.x, -math.sin(drop * (0.4 + r)), out.z)).normalized()
        p = base + d * (length * r) + Vector((0, -0.002 - 0.004 * s * s * r, 0))
        return p + Vector((0, 0.002 if top else -0.002, 0))
    ss = [-1 + 2 * i / 24 for i in range(25)]
    rs = [0.0, 0.25, 0.5, 0.75, 0.92, 1.0]
    top = fg.grid_surface(lambda s, r: brim_pt(s, r, True), ss, rs, lambda s, r: "cap_brim",
                          normal=lambda s, r: Vector((0, 1, 0.15)))
    bot = fg.grid_surface(lambda s, r: brim_pt(s, r, False), ss, rs, lambda s, r: "cap_brim",
                          normal=lambda s, r: Vector((0, -1, -0.1)))
    edge = fg.grid_surface(lambda s, k: brim_pt(s, 1.0, k > 0.5), ss, [0.0, 1.0], lambda s, k: "cap_brim",
                           normal=lambda s, k: (brim_pt(s, 1.0, True) - brim_pt(s, 0.9, True)).normalized())
    parts += [top, bot, edge]

    # The top button.
    apex = crown(0.0, 1.0)
    bm = Mesh()
    ring = [bm.vert(apex + Vector((0.008 * math.cos(a), 0.0, 0.008 * math.sin(a))))
            for a in [2 * math.pi * i / 10 for i in range(10)]]
    tip = bm.vert(apex + Vector((0, 0.0045, 0)))
    for j in range(10):
        bm.face((ring[j], tip, ring[(j + 1) % 10]), "cap")
    for k, f in enumerate(bm.faces):
        if bm.face_normal(f).y < 0:
            bm.faces[k] = f[::-1]
    parts.append((bm, fg.flat_normals(bm)))

    # Number frame on the front panel.
    t = 0.42
    p = crown(0.0, t)
    q = crown(0.0, t + 0.02)
    n = (p - crown(0.0, t - 0.02)).cross(crown(0.05, t) - crown(-0.05, t))
    n = -n.normalized() if n.z < 0 else n.normalized()
    up = (q - p).normalized()
    up = (up - n * up.dot(n)).normalized()
    right = up.cross(n)
    return fg.concat(parts), (p, right, up, n)
